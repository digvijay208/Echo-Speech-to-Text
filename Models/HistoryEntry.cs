using System.Text.Json.Serialization;

namespace Echo.Models;

/// <summary>
/// Record of a single correction applied during post-processing.
/// </summary>
public class AppliedCorrection
{
    [JsonPropertyName("original")]
    public string Original { get; set; } = "";

    [JsonPropertyName("replacement")]
    public string Replacement { get; set; } = "";

    [JsonIgnore]
    public string DisplayText => $"'{Original}' ? '{Replacement}'";
}

/// <summary>
/// A single past transcription entry in the tape history log.
/// </summary>
public class HistoryEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.Now;

    [JsonPropertyName("durationSeconds")]
    public double DurationSeconds { get; set; }

    [JsonPropertyName("rawText")]
    public string RawText { get; set; } = "";

    [JsonPropertyName("finalText")]
    public string FinalText { get; set; } = "";

    [JsonPropertyName("appliedCorrections")]
    public List<AppliedCorrection> AppliedCorrections { get; set; } = new();

    [JsonIgnore]
    public bool HasCorrections => AppliedCorrections.Count > 0;

    [JsonIgnore]
    public string TimeFormatted => Timestamp.ToString("HH:mm:ss");

    [JsonIgnore]
    public string DateFormatted => Timestamp.ToString("yyyy-MM-dd");

    [JsonIgnore]
    public string DurationFormatted => $"{DurationSeconds:F1}s";

    [JsonIgnore]
    public string CorrectionsSummary => HasCorrections
        ? string.Join(", ", AppliedCorrections.Select(c => c.DisplayText))
        : "";
}
