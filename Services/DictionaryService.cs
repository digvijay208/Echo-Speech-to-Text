using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Echo.Models;

namespace Echo.Services;

/// <summary>
/// Manages the custom dictionary: keywords for prompt biasing and post-processing correction rules.
/// Persists to %LOCALAPPDATA%\Echo\dictionary.json
/// </summary>
public sealed class DictionaryService
{
    private readonly string _filePath;
    private readonly List<DictionaryEntry> _entries = new();
    private readonly object _lock = new();

    private static readonly HashSet<string> CommonStopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "and", "or", "but", "if", "then", "else", "when", "at", "by", "for", "with",
        "about", "against", "between", "into", "through", "during", "before", "after", "above", "below",
        "to", "from", "up", "down", "in", "out", "on", "off", "over", "under", "again", "further", "then",
        "once", "here", "there", "all", "any", "both", "each", "few", "more", "most", "other", "some",
        "such", "no", "nor", "not", "only", "own", "same", "so", "than", "too", "very", "can", "will",
        "just", "don", "should", "now", "it", "its", "is", "was", "are", "were", "be", "been", "being",
        "have", "has", "had", "having", "do", "does", "did", "doing", "would", "could", "should"
    };

    public IReadOnlyList<DictionaryEntry> Entries
    {
        get
        {
            lock (_lock) return _entries.ToList();
        }
    }

    public event Action? DictionaryChanged;

    public DictionaryService()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Echo");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "dictionary.json");

        Load();
    }

    public void Load()
    {
        lock (_lock)
        {
            _entries.Clear();

            if (File.Exists(_filePath))
            {
                try
                {
                    string json = File.ReadAllText(_filePath);
                    var container = JsonSerializer.Deserialize<DictionaryContainer>(json);
                    if (container != null)
                    {
                        if (container.Corrections != null)
                            _entries.AddRange(container.Corrections);

                        if (container.Keywords != null)
                        {
                            foreach (var kw in container.Keywords)
                            {
                                if (!_entries.Any(e => e.From.Equals(kw, StringComparison.OrdinalIgnoreCase)))
                                {
                                    _entries.Add(new DictionaryEntry
                                    {
                                        From = kw,
                                        IsKeywordOnly = true
                                    });
                                }
                            }
                        }
                    }
                    Logger.Info($"Loaded {_entries.Count} dictionary entries from {_filePath}");
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to load dictionary.json", ex);
                }
            }
            else
            {
                // Seed with standard developer examples
                _entries.Add(new DictionaryEntry { From = "cloud code", To = "Claude Code", MatchGluedWords = true });
                _entries.Add(new DictionaryEntry { From = "Anthropic", IsKeywordOnly = true });
                _entries.Add(new DictionaryEntry { From = "Vercel", IsKeywordOnly = true });
                _entries.Add(new DictionaryEntry { From = "Supabase", IsKeywordOnly = true });
                SaveInternal();
            }
        }
        DictionaryChanged?.Invoke();
    }

    public void Save()
    {
        lock (_lock)
        {
            SaveInternal();
        }
        DictionaryChanged?.Invoke();
    }

    private void SaveInternal()
    {
        try
        {
            var container = new DictionaryContainer
            {
                Keywords = _entries.Where(e => e.IsKeywordOnly).Select(e => e.From).ToList(),
                Corrections = _entries.Where(e => !e.IsKeywordOnly).ToList()
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(container, options);

            // Write-then-replace so a crash mid-write cannot truncate the dictionary.
            string tempPath = _filePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _filePath, overwrite: true);

            Logger.Info($"Saved {_entries.Count} dictionary entries to {_filePath}");
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to save dictionary.json", ex);
        }
    }

    public void AddOrUpdate(DictionaryEntry entry)
    {
        lock (_lock)
        {
            int idx = _entries.FindIndex(e => e.Id == entry.Id);
            if (idx >= 0)
                _entries[idx] = entry;
            else
                _entries.Insert(0, entry);

            SaveInternal();
        }
        DictionaryChanged?.Invoke();
    }

    public void Delete(string id)
    {
        lock (_lock)
        {
            _entries.RemoveAll(e => e.Id == id);
            SaveInternal();
        }
        DictionaryChanged?.Invoke();
    }

    /// <summary>
    /// Builds a short prompt string to bias Whisper before transcribing.
    /// Returns "" when there is no vocabulary ? see the comment below for why that matters.
    /// </summary>
    public string GenerateBiasingPrompt()
    {
        lock (_lock)
        {
            var keywords = new List<string>();

            // Collect target words from corrections & keywords
            foreach (var entry in _entries.Take(15))
            {
                string word = entry.IsKeywordOnly ? entry.From : entry.To;
                if (!string.IsNullOrWhiteSpace(word) && !keywords.Contains(word, StringComparer.OrdinalIgnoreCase))
                {
                    keywords.Add(word);
                }
            }

            // Whisper's prompt is not an instruction ? it is treated as transcript text that
            // came immediately BEFORE the audio. The old sentence ("Vocabulary: ... Clear
            // English speech dictation with punctuation.") made the decoder continue in that
            // narrative register, so a two-word utterance came back as a fluent invented
            // sentence. A bare comma-separated vocabulary list biases spelling without
            // supplying grammar to continue, and no keywords means no prompt at all.
            if (keywords.Count == 0)
                return "";

            return string.Join(", ", keywords) + ".";
        }
    }

    /// <summary>
    /// Applies deterministic post-processing corrections:
    /// Whole-word, case-insensitive, longest match first, with glued-word support.
    /// </summary>
    public string ApplyCorrections(string text, out List<AppliedCorrection> applied)
    {
        applied = new List<AppliedCorrection>();
        if (string.IsNullOrWhiteSpace(text)) return text;

        List<DictionaryEntry> rules;
        lock (_lock)
        {
            rules = _entries
                .Where(e => !e.IsKeywordOnly && !string.IsNullOrWhiteSpace(e.From) && !string.IsNullOrWhiteSpace(e.To))
                .OrderByDescending(e => e.From.Length)
                .ToList();
        }

        string result = text;

        foreach (var rule in rules)
        {
            string pattern = BuildRegexPattern(rule.From, rule.MatchGluedWords);
            if (string.IsNullOrEmpty(pattern)) continue;

            // Regex.Replace treats $ specially in the replacement ($1, $&, $$ ...), so a
            // replacement like "$100" or "C$" would silently corrupt the output. Escape it.
            string replacement = rule.To.Replace("$", "$$");

            try
            {
                var matches = Regex.Matches(result, pattern, RegexOptions.IgnoreCase);
                if (matches.Count > 0)
                {
                    foreach (Match m in matches)
                    {
                        applied.Add(new AppliedCorrection
                        {
                            Original = m.Value,
                            Replacement = rule.To
                        });
                    }

                    result = Regex.Replace(result, pattern, replacement, RegexOptions.IgnoreCase);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Regex failure on rule '{rule.From}' -> '{rule.To}'", ex);
            }
        }

        return result;
    }

    /// <summary>
    /// Compiles a regex matching whole word boundaries and optional glued whitespace/hyphens.
    /// E.g. "cloud code" -> (?&lt;!\w)cloud(?:\s+|-)?code(?!\w)
    /// </summary>
    public static string BuildRegexPattern(string source, bool matchGlued)
    {
        if (string.IsNullOrWhiteSpace(source)) return "";

        string trimmed = source.Trim();

        // (?<!\w) / (?!\w) instead of \b: \b requires a word character on the inside, so a
        // phrase starting or ending with punctuation ("#tag", "C++") could never match.
        const string left = @"(?<!\w)";
        const string right = @"(?!\w)";

        if (matchGlued && trimmed.Contains(' '))
        {
            var words = trimmed.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
            var escapedWords = words.Select(Regex.Escape);
            return $@"{left}{string.Join(@"(?:\s+|-)?", escapedWords)}{right}";
        }
        else
        {
            return $@"{left}{Regex.Escape(trimmed)}{right}";
        }
    }

    /// <summary>
    /// Inspects whether a rule source pattern is too generic or likely to collide with common language.
    /// </summary>
    public static string? CheckCollisionWarning(string fromPattern)
    {
        if (string.IsNullOrWhiteSpace(fromPattern)) return null;

        string clean = fromPattern.Trim().ToLowerInvariant();

        if (clean.Length <= 2)
            return "Warning: 1-2 character patterns may match common prefixes or short words.";

        if (CommonStopwords.Contains(clean))
            return $"Warning: '{fromPattern}' is a common English word and may cause unintended replacements.";

        return null;
    }
}
