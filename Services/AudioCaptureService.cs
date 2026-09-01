using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Echo.Services;

/// <summary>
/// Captures audio from the active Windows microphone using WASAPI (or WaveIn as fallback).
/// Automatically handles device sample rates (44.1kHz, 48kHz, etc.) and channels (stereo/mono),
/// converting cleanly to 16kHz mono float[] samples required by Whisper.
/// </summary>
public sealed class AudioCaptureService : IDisposable
{
    private WasapiCapture? _wasapiCapture;
    private WaveInEvent? _waveIn;
    private MemoryStream? _audioBuffer;
    private WaveFormat? _captureFormat;
    private readonly object _lock = new();
    private bool _isRecording;

    // Running peak for the live VAD stream AGC. The full-buffer path peak-normalizes
    // in StopRecording, but the VAD consumes chunks in real time and was being fed
    // quiet raw mic audio (peaks ~0.03-0.09) that rarely crossed Silero's 0.5 speech
    // threshold — so VAD reported "0 ms of speech" and dropped every utterance before
    // it ever reached an STT engine.
    private float _vadRunningPeak;

    public bool IsRecording => _isRecording;

    /// <summary>
    /// Selected device ID. Empty string = Windows Default Recording Device.
    /// </summary>
    public string SelectedDeviceId { get; set; } = "";

    /// <summary>
    /// Raised when audio level changes (0.0 - 1.0) for waveform visualization.
    /// </summary>
    public event Action<float>? AudioLevelChanged;

    /// <summary>
    /// Raised on every captured buffer after it has been resampled to
    /// 16 kHz mono float32. Subscribers (SileroVadService) process chunks
    /// incrementally instead of waiting for the full recording to stop.
    /// Always fired on the audio capture thread; subscribers must marshal
    /// to the UI thread if they touch WPF.
    /// </summary>
    public event Action<float[]>? ChunkAvailable;

    /// <summary>
    /// Starts capturing audio from the microphone.
    /// </summary>
    public void StartRecording()
    {
        lock (_lock)
        {
            if (_isRecording) return;

            _audioBuffer = new MemoryStream();

            // Fresh AGC state each recording: a loud transient (click, pop) in a
            // previous clip would otherwise hold the running peak high and suppress
            // gain for the first seconds of the next one.
            _vadRunningPeak = 0f;

            try
            {
                MMDevice device;
                using var enumerator = new MMDeviceEnumerator();

                if (!string.IsNullOrEmpty(SelectedDeviceId))
                {
                    device = enumerator.GetDevice(SelectedDeviceId);
                }
                else
                {
                    // Prefer the physical laptop mic array. The OS default is
                    // often a virtual device (DroidCam, Steam Streaming) that is
                    // useless for dictation.
                    string? preferredId = FindPreferredDeviceId(enumerator);
                    device = preferredId != null
                        ? enumerator.GetDevice(preferredId)
                        : enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
                }

                Logger.Info($"Opening WASAPI audio capture on device: '{device.FriendlyName}' (Format: {device.AudioClient.MixFormat})");

                _wasapiCapture = new WasapiCapture(device)
                {
                    ShareMode = AudioClientShareMode.Shared
                };
                _captureFormat = _wasapiCapture.WaveFormat;
                _wasapiCapture.DataAvailable += OnDataAvailable;
                _wasapiCapture.RecordingStopped += OnRecordingStopped;
                _wasapiCapture.StartRecording();
                _isRecording = true;
            }
            catch (Exception ex)
            {
                Logger.Error("WASAPI capture failed to start; falling back to WaveIn", ex);
                StartWaveInFallback();
            }
        }
    }

    private void StartWaveInFallback()
    {
        try
        {
            // 16kHz mono is exactly what Whisper wants ? skip resampling entirely when the
            // driver accepts it. WaveIn is far more permissive about formats than WASAPI.
            _waveIn = new WaveInEvent
            {
                DeviceNumber = 0,
                WaveFormat = new WaveFormat(16000, 16, 1),
                BufferMilliseconds = 50
            };
            _captureFormat = _waveIn.WaveFormat;
            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;
            _waveIn.StartRecording();
            _isRecording = true;
            Logger.Info($"WaveIn fallback capture started ({_captureFormat}).");
        }
        catch (Exception ex)
        {
            Logger.Error("WaveIn fallback failed too", ex);
            _waveIn?.Dispose();
            _waveIn = null;
            _isRecording = false;
        }
    }

    /// <summary>
    /// Stops recording and returns 16kHz mono float[] samples normalized to [-1.0, 1.0].
    /// </summary>
    public float[] StopRecording()
    {
        lock (_lock)
        {
            if (!_isRecording || _audioBuffer == null)
                return Array.Empty<float>();

            _isRecording = false;

            if (_wasapiCapture != null)
            {
                try
                {
                    _wasapiCapture.StopRecording();
                    _wasapiCapture.DataAvailable -= OnDataAvailable;
                    _wasapiCapture.RecordingStopped -= OnRecordingStopped;
                    _wasapiCapture.Dispose();
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Error stopping WASAPI: {ex.Message}");
                }
                _wasapiCapture = null;
            }

            if (_waveIn != null)
            {
                try
                {
                    _waveIn.StopRecording();
                    _waveIn.DataAvailable -= OnDataAvailable;
                    _waveIn.RecordingStopped -= OnRecordingStopped;
                    _waveIn.Dispose();
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Error stopping WaveIn: {ex.Message}");
                }
                _waveIn = null;
            }

            byte[] rawBytes = _audioBuffer.ToArray();
            _audioBuffer.Dispose();
            _audioBuffer = null;

            if (rawBytes.Length == 0 || _captureFormat == null)
            {
                Logger.Warn("StopRecording: 0 raw bytes collected.");
                return Array.Empty<float>();
            }

            // Convert and resample to 16kHz mono float[]
            float[] resampled = ResampleTo16kMono(rawBytes, _captureFormat);
            Logger.Info($"Audio conversion complete: {rawBytes.Length} raw bytes -> {resampled.Length} samples @ 16kHz mono ({resampled.Length / 16000.0:F2}s)");

            NormalizeGain(resampled);

            return resampled;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        float? level = null;
        float[]? resampledChunk = null;

        lock (_lock)
        {
            if (_audioBuffer != null && _isRecording)
            {
                _audioBuffer.Write(e.Buffer, 0, e.BytesRecorded);

                if (_captureFormat != null)
                {
                    level = CalculateRms(e.Buffer, e.BytesRecorded, _captureFormat);
                    // Resample every incoming buffer to 16 kHz mono for the
                    // VAD stream. The same buffer is also captured into
                    // _audioBuffer so StopRecording() still returns the
                    // full utterance for backwards-compatible callers.
                    resampledChunk = ResampleBufferTo16kMono(e.Buffer, e.BytesRecorded, _captureFormat);
                }
            }
        }

        // Raise OUTSIDE the lock. Subscribers marshal to the UI thread, and the UI thread
        // also calls StopRecording() which takes _lock ? firing this while holding the lock
        // deadlocks the app (audio thread waits on UI, UI waits on _lock).
        if (level.HasValue)
            AudioLevelChanged?.Invoke(level.Value);
        if (resampledChunk != null && resampledChunk.Length > 0)
        {
            ApplyStreamGain(resampledChunk, ref _vadRunningPeak);
            ChunkAvailable?.Invoke(resampledChunk);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            Logger.Error("Recording stopped with exception", e.Exception);
        }
    }

    /// <summary>
    /// Converts any captured PCM/IEEE Float buffer to 16kHz mono float[] samples.
    /// </summary>
    private static float[] ResampleBufferTo16kMono(byte[] rawBytes, int bytesRecorded, WaveFormat format)
    {
        // The full-buffer path uses the exact same code, so reuse it. Slice
        // the byte[] to the actual BytesRecorded count because the input
        // buffer can be larger than the data filled in.
        if (bytesRecorded == rawBytes.Length) return ResampleTo16kMono(rawBytes, format);
        var slice = new byte[bytesRecorded];
        Buffer.BlockCopy(rawBytes, 0, slice, 0, bytesRecorded);
        return ResampleTo16kMono(slice, format);
    }

    /// <summary>
    /// Converts any captured PCM/IEEE Float buffer to 16kHz mono float[] samples.
    /// </summary>
    private static float[] ResampleTo16kMono(byte[] rawBytes, WaveFormat format)
    {
        // Step 1: Decode rawBytes to float[] in the source sample rate (downmixed to mono)
        float[] sourceSamplesMono;

        if (format.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            int floatCount = rawBytes.Length / 4;
            int frames = floatCount / format.Channels;
            sourceSamplesMono = new float[frames];

            for (int i = 0; i < frames; i++)
            {
                float sum = 0;
                for (int ch = 0; ch < format.Channels; ch++)
                {
                    int offset = (i * format.Channels + ch) * 4;
                    if (offset + 4 <= rawBytes.Length)
                        sum += BitConverter.ToSingle(rawBytes, offset);
                }
                sourceSamplesMono[i] = sum / format.Channels;
            }
        }
        else // 16-bit PCM
        {
            int sampleCount = rawBytes.Length / 2;
            int frames = sampleCount / format.Channels;
            sourceSamplesMono = new float[frames];

            for (int i = 0; i < frames; i++)
            {
                float sum = 0;
                for (int ch = 0; ch < format.Channels; ch++)
                {
                    int offset = (i * format.Channels + ch) * 2;
                    if (offset + 2 <= rawBytes.Length)
                    {
                        short val = BitConverter.ToInt16(rawBytes, offset);
                        sum += val / 32768f;
                    }
                }
                sourceSamplesMono[i] = sum / format.Channels;
            }
        }

        // Step 2: Resample from format.SampleRate to 16000Hz
        int targetRate = 16000;
        if (format.SampleRate == targetRate)
        {
            return sourceSamplesMono;
        }

        double ratio = (double)format.SampleRate / targetRate;
        int targetCount = (int)(sourceSamplesMono.Length / ratio);
        if (targetCount <= 0) return Array.Empty<float>();

        float[] targetSamples = new float[targetCount];

        if (ratio > 1.0)
        {
            // Downsampling (48k/44.1k -> 16k). Plain index-picking aliases everything above
            // 8 kHz back into the speech band and measurably hurts Whisper accuracy, so
            // average each source window first ? a cheap box lowpass ? then decimate.
            for (int i = 0; i < targetCount; i++)
            {
                int start = (int)(i * ratio);
                int end = (int)((i + 1) * ratio);
                if (end <= start) end = start + 1;
                if (end > sourceSamplesMono.Length) end = sourceSamplesMono.Length;
                if (start >= end) break;

                float sum = 0;
                for (int s = start; s < end; s++)
                    sum += sourceSamplesMono[s];

                targetSamples[i] = sum / (end - start);
            }
        }
        else
        {
            // Upsampling ? linear interpolation is fine.
            for (int i = 0; i < targetCount; i++)
            {
                double sourcePos = i * ratio;
                int index = (int)sourcePos;
                double frac = sourcePos - index;

                if (index + 1 < sourceSamplesMono.Length)
                {
                    targetSamples[i] = (float)((1.0 - frac) * sourceSamplesMono[index] + frac * sourceSamplesMono[index + 1]);
                }
                else if (index < sourceSamplesMono.Length)
                {
                    targetSamples[i] = sourceSamplesMono[index];
                }
            }
        }

        return targetSamples;
    }

    /// <summary>
    /// Adaptive gain for the live VAD stream. The full-buffer path peak-normalizes
    /// in <see cref="StopRecording"/>, but the VAD consumes chunks in real time and
    /// was being fed quiet raw mic audio (peaks ~0.03-0.09) that rarely crossed
    /// Silero's 0.5 speech threshold. Scale each chunk by a running peak with decay
    /// so gain follows the loudest recent speech without clipping.
    /// </summary>
    private static void ApplyStreamGain(float[] samples, ref float runningPeak)
    {
        if (samples.Length == 0) return;

        float chunkPeak = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float a = Math.Abs(samples[i]);
            if (a > chunkPeak) chunkPeak = a;
        }

        // Decay slowly so one loud word doesn't permanently slam the gain down.
        runningPeak = Math.Max(chunkPeak, runningPeak * 0.98f);
        if (runningPeak <= 0f) return;

        const float TargetPeak = 0.95f;
        const float MaxGain = 30f;
        float gain = Math.Min(TargetPeak / runningPeak, MaxGain);
        if (gain <= 1.01f) return;

        for (int i = 0; i < samples.Length; i++)
            samples[i] = Math.Clamp(samples[i] * gain, -1f, 1f);
    }

    /// <summary>
    /// Peak-normalizes the samples in place, and logs the measured level.
    /// Laptop mic arrays and USB webcams commonly peak around 0.03-0.15. Whisper handles
    /// quiet audio badly: it returns [BLANK_AUDIO] or invents a fluent sentence that was
    /// never spoken. Bringing the level up first is the largest single accuracy win.
    /// </summary>
    private static void NormalizeGain(float[] samples)
    {
        if (samples.Length == 0) return;

        float peak = 0;
        double sumSquares = 0;

        for (int i = 0; i < samples.Length; i++)
        {
            float abs = Math.Abs(samples[i]);
            if (abs > peak) peak = abs;
            sumSquares += (double)samples[i] * samples[i];
        }

        float rms = (float)Math.Sqrt(sumSquares / samples.Length);

        // Below this there is nothing but noise floor. Amplifying that is worse than
        // leaving it quiet ? Whisper turns loud noise into confident nonsense.
        const float SilenceRms = 0.0005f;

        if (peak <= 0f || rms < SilenceRms)
        {
            Logger.Warn($"Audio level far too low (peak {peak:F4}, RMS {rms:F4}). Check the input device and the Windows microphone level/boost.");
            return;
        }

        const float TargetPeak = 0.95f;
        const float MaxGain = 30f;

        float gain = Math.Min(TargetPeak / peak, MaxGain);

        if (gain > 1.01f)
        {
            for (int i = 0; i < samples.Length; i++)
                samples[i] = Math.Clamp(samples[i] * gain, -1f, 1f);
        }
        else
        {
            gain = 1f;
        }

        Logger.Info($"Audio level: peak {peak:F4}, RMS {rms:F4}, gain x{gain:F2}");
    }

    private static float CalculateRms(byte[] buffer, int bytesRecorded, WaveFormat format)
    {
        if (bytesRecorded == 0) return 0;

        double sum = 0;
        int count = 0;

        if (format.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            int floatCount = bytesRecorded / 4;
            for (int i = 0; i < floatCount; i += format.Channels)
            {
                if ((i + 1) * 4 <= bytesRecorded)
                {
                    float val = BitConverter.ToSingle(buffer, i * 4);
                    sum += val * val;
                    count++;
                }
            }
        }
        else
        {
            int shortCount = bytesRecorded / 2;
            for (int i = 0; i < shortCount; i += format.Channels)
            {
                if ((i + 1) * 2 <= bytesRecorded)
                {
                    short val = BitConverter.ToInt16(buffer, i * 2);
                    double norm = val / 32768.0;
                    sum += norm * norm;
                    count++;
                }
            }
        }

        return count > 0 ? (float)Math.Sqrt(sum / count) : 0;
    }

    /// <summary>
    /// Finds the capture endpoint this app should use when the user has not
    /// picked one explicitly: the physical "Microphone Array" (Intel Smart
    /// Sound) laptop mic when present, else null (caller falls back to the
    /// OS default). Matches by name hint so it stays robust to device-ID
    /// churn across reboots/driver updates.
    /// </summary>
    private const string PreferredDeviceNameHint = "microphone array";

    private static string? FindPreferredDeviceId(MMDeviceEnumerator enumerator)
    {
        foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            if (endpoint.FriendlyName.Contains(PreferredDeviceNameHint, StringComparison.OrdinalIgnoreCase))
                return endpoint.ID;
        }
        return null;
    }

    /// <summary>
    /// Returns the list of all available active audio recording devices with their MMDevice ID and Friendly Name.
    /// </summary>
    public static List<(string Id, string Name, bool IsDefault)> GetAvailableDevices()
    {
        var devices = new List<(string, string, bool)>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            string defaultId = defaultDevice?.ID ?? "";
            string? preferredId = FindPreferredDeviceId(enumerator);

            var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            foreach (var endpoint in endpoints)
            {
                // Mark the app-preferred device (Mic Array) as default so the
                // Settings UI auto-selects it and labels it "(Default)" instead
                // of whatever the OS default happens to be (e.g. DroidCam).
                bool isDefault = endpoint.ID == (preferredId ?? defaultId);
                devices.Add((endpoint.ID, endpoint.FriendlyName, isDefault));
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to enumerate audio devices", ex);
        }
        return devices;
    }

    public void Dispose()
    {
        if (_isRecording)
        {
            StopRecording();
        }
        _wasapiCapture?.Dispose();
        _wasapiCapture = null;
        _waveIn?.Dispose();
        _waveIn = null;
        _audioBuffer?.Dispose();
        _audioBuffer = null;
    }
}
