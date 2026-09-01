namespace Echo.Services.Audio;

/// <summary>
/// Tuning knobs for the Silero VAD v5 wrapper. Defaults match the recommended
/// settings for push-to-talk dictation: a segment is flushed when the user
/// has been silent for roughly half a second, but only if at least a quarter
/// second of speech preceded it (so key clicks don't trigger a transcription).
/// </summary>
public sealed record VadConfig
{
    /// <summary>Sample rate the VAD model expects. Hard-coded by Silero.</summary>
    public int SampleRate { get; init; } = 16_000;

    /// <summary>Samples per forward pass. 512 @ 16 kHz = 32 ms chunks.</summary>
    public int ChunkSize { get; init; } = 512;

    /// <summary>Probability above which a chunk is considered speech.</summary>
    /// <remarks>
    /// Stock Silero default is 0.5, but laptop mic arrays commonly deliver
    /// speech that hovers right at that line (real-world test: two near-identical
    /// recordings, same level, one detected and one "0 ms speech"). 0.35 catches
    /// that borderline audio while still rejecting pure noise.
    /// </remarks>
    public float SpeechThreshold { get; init; } = 0.35f;

    /// <summary>Probability below which a chunk is considered silence.</summary>
    public float SilenceThreshold { get; init; } = 0.25f;

    /// <summary>Trailing silence that triggers a segment flush.</summary>
    public int MinSilenceMs { get; init; } = 500;

    /// <summary>Drop utterances shorter than this (clicks, breaths, bumps).</summary>
    public int MinSpeechMs { get; init; } = 150;

    /// <summary>Force-flush a segment after this many ms of continuous speech.</summary>
    public int MaxSegmentMs { get; init; } = 30_000;

    /// <summary>Pre-roll audio to keep at the start of each segment.</summary>
    public int SpeechPadMs { get; init; } = 100;

    public static VadConfig Default => new();
}
