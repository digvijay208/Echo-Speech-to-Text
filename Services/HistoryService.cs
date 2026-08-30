using System.IO;
using System.Text.Json;
using Echo.Models;

namespace Echo.Services;

/// <summary>
/// Manages the past transcription log with search, copy, and correction audit records.
/// Persists to %LOCALAPPDATA%\Echo\history.json
/// </summary>
public sealed class HistoryService
{
    private readonly string _filePath;
    private readonly List<HistoryEntry> _entries = new();
    private readonly object _lock = new();
    private const int MaxHistoryItems = 200;

    public IReadOnlyList<HistoryEntry> Entries
    {
        get
        {
            lock (_lock) return _entries.ToList();
        }
    }

    public event Action? HistoryChanged;

    public HistoryService()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Echo");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "history.json");

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
                    var items = JsonSerializer.Deserialize<List<HistoryEntry>>(json);
                    if (items != null)
                        _entries.AddRange(items);

                    // A file written when the cap was higher would otherwise stay oversized
                    // forever, since AddEntry only ever trimmed one item per call.
                    TrimToCapUnsafe();

                    Logger.Info($"Loaded {_entries.Count} history items from {_filePath}");
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to load history.json", ex);
                }
            }
        }
        HistoryChanged?.Invoke();
    }

    public void AddEntry(string rawText, string finalText, double durationSeconds, List<AppliedCorrection> corrections)
    {
        lock (_lock)
        {
            var entry = new HistoryEntry
            {
                RawText = rawText,
                FinalText = finalText,
                DurationSeconds = durationSeconds,
                AppliedCorrections = corrections?.ToList() ?? new List<AppliedCorrection>(),
                Timestamp = DateTime.Now
            };

            _entries.Insert(0, entry);
            TrimToCapUnsafe();

            SaveInternal();
        }
        HistoryChanged?.Invoke();
    }

    /// <summary>
    /// Caller must hold _lock.
    /// </summary>
    private void TrimToCapUnsafe()
    {
        if (_entries.Count > MaxHistoryItems)
            _entries.RemoveRange(MaxHistoryItems, _entries.Count - MaxHistoryItems);
    }

    public void DeleteEntry(string id)
    {
        lock (_lock)
        {
            _entries.RemoveAll(e => e.Id == id);
            SaveInternal();
        }
        HistoryChanged?.Invoke();
    }

    public void ClearAll()
    {
        lock (_lock)
        {
            _entries.Clear();
            SaveInternal();
        }
        HistoryChanged?.Invoke();
    }

    public List<HistoryEntry> Search(string query)
    {
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(query))
                return _entries.ToList();

            string q = query.Trim();
            return _entries
                .Where(e => e.FinalText.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                            e.RawText.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                            e.AppliedCorrections.Any(c => c.Original.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                                          c.Replacement.Contains(q, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }
    }

    private void SaveInternal()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(_entries, options);

            // Write-then-replace: a crash partway through a direct WriteAllText leaves a
            // truncated file and the whole history becomes unreadable on next launch.
            string tempPath = _filePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to save history.json", ex);
        }
    }
}
