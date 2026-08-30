using System.Text.Json.Serialization;

namespace Echo.Models;

/// <summary>
/// A dictionary keyword or correction entry.
/// Type 1: Keyword to bias engine ("Anthropic", "Vercel")
/// Type 2: Correction pair ("cloud code" -> "Claude Code")
/// </summary>
public class DictionaryEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    [JsonPropertyName("from")]
    public string From { get; set; } = "";

    [JsonPropertyName("to")]
    public string To { get; set; } = "";

    [JsonPropertyName("isKeywordOnly")]
    public bool IsKeywordOnly { get; set; }

    [JsonPropertyName("matchGluedWords")]
    public bool MatchGluedWords { get; set; } = true;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public string DisplayRule => IsKeywordOnly ? From : $"{From} ? {To}";

    [JsonIgnore]
    public string DisplayType => IsKeywordOnly ? "KEYWORD" : "CORRECTION";
}

/// <summary>
/// Root dictionary persistence container.
/// </summary>
public class DictionaryContainer
{
    [JsonPropertyName("keywords")]
    public List<string> Keywords { get; set; } = new();

    [JsonPropertyName("corrections")]
    public List<DictionaryEntry> Corrections { get; set; } = new();
}
