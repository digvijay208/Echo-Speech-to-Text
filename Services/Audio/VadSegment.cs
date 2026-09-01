namespace Echo.Services.Audio;

/// <summary>
/// One captured utterance as decided by the VAD. Samples are 16 kHz mono
/// float32 in [-1, 1] and ready to hand to any STT engine that expects
/// that format (Whisper, Parakeet, Nemotron).
/// </summary>
public sealed record VadSegment(float[] Samples, int SampleRate, TimeSpan Duration)
{
    public int SampleCount => Samples.Length;

    public float DurationSeconds => (float)Duration.TotalSeconds;
}
