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
