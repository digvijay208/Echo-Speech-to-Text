using System.IO;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Echo.Services.Audio;

/// <summary>
/// Real-time voice activity detector backed by Silero VAD v5 (ONNX, ~2 MB).
///
/// Feed it 16 kHz mono float32 audio in 512-sample chunks via
/// <see cref="ProcessChunk"/>. Each chunk is a single 32 ms forward pass; the
/// model returns a probability in [0, 1] that the chunk contains speech.
///
/// The service is a tiny state machine:
///
///   Silent -> chunk crosses SpeechThreshold -> SpeechStarted (capture begins,
///             with <see cref="VadConfig.SpeechPadMs"/> of pre-roll attached)
///   SpeechStarted -> more speech -> Speaking
///   Speaking -> chunk drops below SilenceThreshold for <see cref="VadConfig.MinSilenceMs"/>
///             -> SegmentReady fires with the captured audio
///
/// <see cref="Flush"/> forces a segment out without waiting for silence (used
/// when the user releases push-to-talk).
/// </summary>
public sealed class SileroVadService : IDisposable
{
    private readonly VadConfig _cfg;
    private readonly InferenceSession _session;

    // LSTM hidden state. Shape [2, 1, 64] (num_layers, batch, hidden).
    private float[] _h = new float[2 * 1 * 64];
    private float[] _c = new float[2 * 1 * 64];

    // Pre-roll ring buffer (samples most recent enough audio to prepend to
    // a freshly-started segment so the first word isn't clipped).
    private readonly float[] _preRoll;
    private int _preRollFill;
    private int _preRollWriteIdx;

    // Current utterance capture.
    private readonly List<float> _capture = new(capacity: 16_000 * 5);

    // Per-chunk thresholds tracking.
    private int _silenceChunks;
    private int _speechChunks;
    private bool _isSpeech;
    private DateTime _segmentStartTime;

    private bool _disposed;

    /// <summary>Raised when a complete segment is ready (silence detected or Flush called).</summary>
    public event Action<VadSegment>? SegmentReady;

    /// <summary>Raised on the chunk that flipped state from silence to speech.</summary>
    public event Action? SpeechStarted;

    /// <summary>Raised on the chunk that flipped state from speech to silence.</summary>
    public event Action? SpeechEnded;

    public bool IsSpeechActive => _isSpeech;

    public SileroVadService(VadConfig? config = null, string? modelPath = null)
    {
        _cfg = config ?? VadConfig.Default;
        _preRoll = new float[_cfg.SampleRate * _cfg.SpeechPadMs / 1000];

        modelPath ??= ResolveDefaultModelPath();
        if (!File.Exists(modelPath))
            throw new FileNotFoundException(
                $"Silero VAD model not found at '{modelPath}'. The file should be bundled in the Assets folder.",
                modelPath);

        var options = new SessionOptions
        {
            // InterOpThreads = 1 is the ONNX-recommended setting for Silero VAD.
            // Multiple threads cost more in synchronization than they save on a
            // 2 MB model with 32 ms chunks.
            IntraOpNumThreads = 1,
            InterOpNumThreads = 1,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };

        _session = new InferenceSession(modelPath, options);
        Logger.Info($"SileroVadService initialized with model '{modelPath}' (sample rate {_cfg.SampleRate}, chunk size {_cfg.ChunkSize}).");
    }

    private static string ResolveDefaultModelPath()
    {
        // Look first next to the executable (bundled), then fall back to the
        // user's local app data (downloaded post-install).
        string bundled = Path.Combine(AppContext.BaseDirectory, "Assets", "silero_vad_v5.onnx");
        if (File.Exists(bundled)) return bundled;

        string local = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Echo", "models", "silero_vad_v5.onnx");
        return local;
    }

    /// <summary>
    /// Push one 512-sample (32 ms) chunk of 16 kHz mono float audio through the VAD.
    /// Any other size throws — Silero's exported model is rigid about input shape.
    /// </summary>
    public void ProcessChunk(ReadOnlySpan<float> chunk)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SileroVadService));
        if (chunk.Length != _cfg.ChunkSize)
            throw new ArgumentException(
                $"Silero VAD requires chunks of exactly {_cfg.ChunkSize} samples; got {chunk.Length}.",
                nameof(chunk));

        float prob = Infer(chunk);
        UpdateStateMachine(prob, chunk);
    }

    private float Infer(ReadOnlySpan<float> chunk)
    {
        // Copy into a dense array because Tensor needs a managed buffer.
        var input = new float[_cfg.ChunkSize];
        chunk.CopyTo(input);

        var inputTensor = new DenseTensor<float>(input, new[] { 1, _cfg.ChunkSize });
        var hTensor = new DenseTensor<float>(_h, new[] { 2, 1, 64 });
        var cTensor = new DenseTensor<float>(_c, new[] { 2, 1, 64 });

        // The bundled model is a trimmed export: inputs are "x", "h", "c"
        // (no "sr" — sample rate is baked in at 16 kHz). Older Silero exports
        // use "input"/"sr"; we must match the actual metadata or every chunk
        // throws and the VAD never detects speech.
        using var results = _session.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor("x", inputTensor),
            NamedOnnxValue.CreateFromTensor("h", hTensor),
            NamedOnnxValue.CreateFromTensor("c", cTensor)
        });

        // The first output is the speech probability; the next two are the
        // updated LSTM states. Copy the new states back so the next chunk
        // continues the recurrence.
        var probs = results[0].AsEnumerable<float>().ToArray();
        var newH = results[1].AsEnumerable<float>().ToArray();
        var newC = results[2].AsEnumerable<float>().ToArray();
        Array.Copy(newH, _h, _h.Length);
        Array.Copy(newC, _c, _c.Length);
        return probs[0];
    }

    private void UpdateStateMachine(float prob, ReadOnlySpan<float> chunk)
    {
        int silenceChunkMs = _cfg.ChunkSize * 1000 / _cfg.SampleRate;
        int silenceChunksNeeded = _cfg.MinSilenceMs / silenceChunkMs;

        // Always maintain the pre-roll ring so the first word of a new
        // utterance isn't clipped.
        AppendPreRoll(chunk);

        if (prob >= _cfg.SpeechThreshold)
        {
            _silenceChunks = 0;
            _speechChunks++;

            if (!_isSpeech)
            {
                _isSpeech = true;
                _segmentStartTime = DateTime.UtcNow;

                // Backfill with the pre-roll so the segment starts a hair
                // before the speech-probability spike (typically the spike
                // lands mid-vowel).
                _capture.Clear();
                for (int i = 0; i < _preRollFill; i++)
                    _capture.Add(_preRoll[(_preRollWriteIdx + i) % _preRoll.Length]);

                _capture.AddRange(chunk);
                SpeechStarted?.Invoke();
            }
            else
            {
                _capture.AddRange(chunk);
            }

            // Force-flush very long utterances so a runaway speaker doesn't
            // hold the same segment forever.
            int segMs = _capture.Count * 1000 / _cfg.SampleRate;
            if (segMs >= _cfg.MaxSegmentMs)
            {
                Logger.Warn($"VAD force-flushing {segMs} ms segment (MaxSegmentMs reached).");
                Flush();
            }
        }
        else if (prob <= _cfg.SilenceThreshold)
        {
            _speechChunks = 0;

            if (_isSpeech)
            {
                _capture.AddRange(chunk);
                _silenceChunks++;

                if (_silenceChunks >= silenceChunksNeeded)
                    Flush();
            }
        }
        else
        {
            // Mid-zone probability (0.35..0.5): don't change state, but if
            // we are mid-segment, count the chunk as speech so a brief
            // dip doesn't end the segment prematurely.
            _silenceChunks = 0;
            if (_isSpeech)
            {
                _capture.AddRange(chunk);
                _speechChunks++;
            }
        }
    }

    private void AppendPreRoll(ReadOnlySpan<float> chunk)
    {
        for (int i = 0; i < chunk.Length; i++)
        {
            _preRoll[_preRollWriteIdx] = chunk[i];
            _preRollWriteIdx = (_preRollWriteIdx + 1) % _preRoll.Length;
            if (_preRollFill < _preRoll.Length) _preRollFill++;
        }
    }

    /// <summary>
    /// Emit any captured audio as a segment right now, even if the user is
    /// still talking. Call this from push-to-talk release to make sure the
    /// tail of the utterance isn't lost.
    /// </summary>
    public void Flush()
    {
        if (!_isSpeech || _capture.Count == 0)
        {
            _isSpeech = false;
            _silenceChunks = 0;
            _speechChunks = 0;
            return;
        }

        // Measure the WHOLE captured segment, not _speechChunks: that counter
        // is zeroed by every silence chunk (prob <= SilenceThreshold), so a
        // user who stops talking before releasing PTT leaves it at 0 and a
        // real 9-second utterance was being dropped as "0 ms of speech".
        // A click/breath still produces a tiny capture, so the guard holds.
        int captureMs = _capture.Count * 1000 / _cfg.SampleRate;
        if (captureMs < _cfg.MinSpeechMs)
        {
            Logger.Info($"VAD dropped segment of only {captureMs} ms (below MinSpeechMs={_cfg.MinSpeechMs}).");
            _isSpeech = false;
            _capture.Clear();
            _silenceChunks = 0;
            _speechChunks = 0;
            return;
        }

        var samples = _capture.ToArray();
        var duration = TimeSpan.FromSeconds(samples.Length / (double)_cfg.SampleRate);
        var segment = new VadSegment(samples, _cfg.SampleRate, duration);

        Logger.Info($"VAD emitted segment: {samples.Length} samples ({duration.TotalMilliseconds:F0} ms).");
        _isSpeech = false;
        _capture.Clear();
        _silenceChunks = 0;
        _speechChunks = 0;

        SpeechEnded?.Invoke();
        SegmentReady?.Invoke(segment);
    }

    /// <summary>Drop any in-flight capture without emitting. Used on cancel.</summary>
    public void Reset()
    {
        _isSpeech = false;
        _capture.Clear();
        _silenceChunks = 0;
        _speechChunks = 0;
        _preRollFill = 0;
        _preRollWriteIdx = 0;
        Array.Clear(_h, 0, _h.Length);
        Array.Clear(_c, 0, _c.Length);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _session.Dispose(); }
        catch (Exception ex) { Logger.Warn($"Error disposing Silero VAD session: {ex.Message}"); }
    }
}
