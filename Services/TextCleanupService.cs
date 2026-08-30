using System.Text.RegularExpressions;

namespace Echo.Services;

/// <summary>
/// Cleans up raw transcription output: removes filler words, fixes capitalization,
/// adds terminal punctuation, and normalizes whitespace.
/// </summary>
public static partial class TextCleanupService
{
    // True speech disfluencies only. Words like "like", "actually", "right", "kind of"
    // used to be in here, but they are ordinary English ? stripping them turned
    // "I like this" into "I this" and "that's right" into "that's".
    private static readonly string[] FillerWords = [
        "um", "umm", "ummm", "uh", "uhh", "uhhh",
        "hmm", "hm", "er", "erm", "eh"
    ];

    /// <summary>
    /// Applies all cleanup rules to the raw transcript text.
    /// </summary>
    public static string Cleanup(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return string.Empty;

        string text = rawText.Trim();

        // Step 0: Remove Whisper non-speech markers like [BLANK_AUDIO], [MUSIC], (silence).
        // Only known markers ? a blanket \(.*?\) also deleted parentheses the user dictated.
        text = NonSpeechMarkers().Replace(text, " ").Trim();
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Step 1: Remove filler words (case-insensitive, whole word)
        text = RemoveFillers(text);

        // Step 2: Normalize whitespace
        text = NormalizeWhitespace(text);

        // Step 3: Capitalize first letter of each sentence
        text = CapitalizeSentences(text);

        // Step 4: Add terminal punctuation if missing
        text = EnsureTerminalPunctuation(text);

        // Step 5: Fix spacing around punctuation
        text = FixPunctuationSpacing(text);

        return text.Trim();
    }

    private static string RemoveFillers(string text)
    {
        foreach (var filler in FillerWords)
        {
            // Match whole words only, case-insensitive
            string pattern = $@"\b{Regex.Escape(filler)}\b[,]?\s*";
            text = Regex.Replace(text, pattern, " ", RegexOptions.IgnoreCase);
        }
        return text;
    }

    private static string NormalizeWhitespace(string text)
    {
        // Collapse runs of whitespace, then trim: filler removal leaves a leading space,
        // and a leading space makes CapitalizeSentences uppercase the space instead of the
        // first letter.
        return MultipleSpaces().Replace(text, " ").Trim();
    }

    private static string CapitalizeSentences(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // Capitalize first character
        text = char.ToUpper(text[0]) + text[1..];

        // Capitalize after sentence-ending punctuation. Group 2 is the whitespace and must
        // be preserved ? dropping it glued sentences together ("Hello. World" -> "Hello.World").
        text = SentenceEnd().Replace(text, m =>
            m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value.ToUpper());

        return text;
    }

    private static string EnsureTerminalPunctuation(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        char lastChar = text[^1];
        if (lastChar != '.' && lastChar != '!' && lastChar != '?' && lastChar != ':' && lastChar != ';')
        {
            text += ".";
        }

        return text;
    }

    private static string FixPunctuationSpacing(string text)
    {
        // Remove space before punctuation
        text = SpaceBeforePunctuation().Replace(text, "$1");

        // Insert the missing space after punctuation. Deliberately narrow: the old
        // ([.!?,;:])(\w) rule also rewrote "3.5" as "3. 5" and "12:30" as "12: 30".
        text = MissingSpaceAfterSentenceEnd().Replace(text, "$1 ");
        text = MissingSpaceAfterClause().Replace(text, "$1 ");

        return text;
    }

    [GeneratedRegex(@"\[[^\]]{0,40}\]|\((?:silence|music|blank_audio|inaudible|laughter|applause|noise|sound|no speech)\)",
        RegexOptions.IgnoreCase)]
    private static partial Regex NonSpeechMarkers();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultipleSpaces();

    [GeneratedRegex(@"([.!?])(\s+)(\w)")]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"\s+([.!?,;:])")]
    private static partial Regex SpaceBeforePunctuation();

    // "word.Word" -> "word. Word". The (?<![A-Z]) guard leaves initialisms like "U.S.A" alone.
    [GeneratedRegex(@"(?<![A-Z])([.!?])(?=[A-Z])")]
    private static partial Regex MissingSpaceAfterSentenceEnd();

    // "one,two" -> "one, two". The (?<!\d) guard leaves "1,000" and "12:30" alone.
    [GeneratedRegex(@"(?<!\d)([,;:])(?=[A-Za-z])")]
    private static partial Regex MissingSpaceAfterClause();
}
