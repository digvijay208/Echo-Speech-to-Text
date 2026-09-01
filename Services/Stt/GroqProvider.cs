using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Echo.Services.Stt;

/// <summary>
/// Groq's OpenAI-compatible transcription endpoint. Free tier gives
/// generous access to whisper-large-v3-turbo (~120x realtime), which is
/// the right default for cloud because it is both fast and as accurate
/// as full Large V3 for dictation.
///
/// API: <c>POST https://api.groq.com/openai/v1/audio/transcriptions</c>
/// Auth: <c>Authorization: Bearer &lt;apiKey&gt;</c>
/// Body: multipart with <c>file</c> (WAV), <c>model</c>, <c>response_format</c>.
/// </summary>
public sealed class GroqProvider : ISttProvider
{
    private const string Endpoint = "https://api.groq.com/openai/v1/audio/transcriptions";
    private const string Model = "whisper-large-v3-turbo";

    private readonly HttpClient _http;
    private string? _apiKey;
    private bool _disposed;

    public string Name => $"Groq {Model}";
    public string Tier => "cloud";
    public bool IsLoaded => !string.IsNullOrEmpty(_apiKey);

    public GroqProvider()
    {
        _http = new HttpClient
        {
            // 30s is plenty for even long utterances at Groq's actual speed.
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public void Load(string modelPathOrKey)
    {
        if (string.IsNullOrWhiteSpace(modelPathOrKey))
            throw new ArgumentException("Groq API key is empty.", nameof(modelPathOrKey));

        _apiKey = modelPathOrKey.Trim();
        Logger.Info($"GroqProvider loaded (key length {_apiKey.Length}).");
    }

    public async Task<string> TranscribeAsync(float[] samples16k, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_apiKey))
            throw new InvalidOperationException("Groq API key not set. Call Load() first.");

        if (samples16k.Length == 0) return string.Empty;

        // Reuse the existing WAV writer so we get the same byte layout Echo
        // already produces. ~5s of 16-bit mono WAV is well under 1 MB.
        using var wavStream = TranscriptionService.CreateWavStream(samples16k, 16_000);
        var wavBytes = wavStream.ToArray();

        using var content = new MultipartFormDataContent("----EchoBoundary" + Guid.NewGuid().ToString("N"));
        var fileContent = new ByteArrayContent(wavBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(fileContent, "file", "audio.wav");
        content.Add(new StringContent(Model), "model");
        content.Add(new StringContent("json"), "response_format");
        content.Add(new StringContent("en"), "language");

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        sw.Stop();

        if (!response.IsSuccessStatusCode)
        {
            // Free tier caps at ~20 requests/min. Surface a useful hint when
            // we hit it so the user knows to back off or pay.
            if ((int)response.StatusCode == 429)
                throw new HttpRequestException(
                    $"Groq rate limit hit (429). Free tier allows ~20 requests/minute. Body: {body}");

            throw new HttpRequestException(
                $"Groq returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("text", out var textProp))
                return textProp.GetString()?.Trim() ?? string.Empty;
        }
        catch (JsonException ex)
        {
            throw new HttpRequestException($"Groq response was not valid JSON: {body}", ex);
        }

        return string.Empty;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
    }
}
