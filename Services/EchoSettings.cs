using System.IO;
using System.Text.Json;

namespace Echo.Services;

/// <summary>
/// Persisted user preferences stored as JSON in
/// <c>%LOCALAPPDATA%\Echo\settings.json</c>.
///
/// Today: STT engine choice, VAD toggle, Groq opt-in + API key.
/// Tomorrow: bias-prompt language, hotkey modifier, etc.
///
/// Hot, in-process state (the loaded Whisper model, the active Groq
/// key) stays in the service that owns it; only what we want to
/// survive an app restart goes here.
/// </summary>
public sealed class EchoSettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Echo", "settings.json");

    public enum SttEngine
    {
        WhisperBase,        // ggml-base.bin (multilingual, default)
        WhisperSmallEn,     // ggml-small.en.bin (English, high accuracy)
        Parakeet,           // Parakeet TDT 0.6B V3 INT8 (local, English, fastest)
        Groq                // Groq Whisper Large V3 Turbo (cloud, free tier)
    }

    public SttEngine Engine { get; set; } = SttEngine.WhisperBase;
    public bool VadEnabled { get; set; } = true;
    public string GroqApiKey { get; set; } = "";

    /// <summary>
    /// ISO-639-1 code of the language you dictate in ("hi", "mr", …).
    /// Empty = auto-detect. Honored by multilingual engines (Whisper Base,
    /// Groq); English-only engines (Small.en, Parakeet) always use "en".
    /// </summary>
    public string SpokenLanguage { get; set; } = "";

    /// <summary>Target words per day, drives the Daily Goal ring on the home view.</summary>
    public int DailyGoalWords { get; set; } = 1600;

    /// <summary>How many words until the Voice Profile is "unlocked".</summary>
    public int VoiceProfileTargetWords { get; set; } = 1800;

    // Window placement (chromeless window has no OS-level memory of its
    // own, so we persist the last size/position the user resized to and
    // restore it on next launch. -1 means "no saved value yet".
    public double WindowLeft { get; set; } = -1;
    public double WindowTop { get; set; } = -1;
    public double WindowWidth { get; set; } = -1;
    public double WindowHeight { get; set; } = -1;
    public bool WindowMaximized { get; set; } = false;

    public static EchoSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                var loaded = JsonSerializer.Deserialize<EchoSettings>(json);
                if (loaded != null) return loaded;
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Could not read settings.json ({ex.Message}); using defaults.");
        }
        return new EchoSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var opts = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, opts));
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to save settings.json", ex);
        }
    }
}
