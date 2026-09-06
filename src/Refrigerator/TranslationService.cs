using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Refrigerator;

public sealed class TranslationService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(25) };
    private readonly AppConfig _config;
    public TranslationService(AppConfig config) => _config = config;

    public async Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        string prompt = BuildPrompt(text, sourceLanguage, targetLanguage, _config.Style);
        return _config.Provider switch
        {
            "Gemini" => await TranslateGeminiAsync(prompt, ct),
            "Groq" => await TranslateOpenAiCompatibleAsync("https://api.groq.com/openai/v1", _config.GroqApiKey, _config.GroqModel, prompt, ct),
            "OpenAI-compatible" => await TranslateOpenAiCompatibleAsync(_config.CustomBaseUrl, _config.CustomApiKey, _config.CustomModel, prompt, ct),
            "Local (Ollama)" => await TranslateOllamaAsync(prompt, ct),
            _ => throw new InvalidOperationException("Provedor de tradução inválido.")
        };
    }

    public async Task<string> TranscribeGroqAsync(byte[] wavBytes, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_config.GroqApiKey)) throw new InvalidOperationException("Configure a API key da Groq para usar o microfone.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/audio/transcriptions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.GroqApiKey);
        using var form = new MultipartFormDataContent();
        var audio = new ByteArrayContent(wavBytes);
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(audio, "file", "refrigerator.wav");
        form.Add(new StringContent(_config.SpeechModel), "model");
        form.Add(new StringContent("json"), "response_format");
        request.Content = form;
        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Groq STT retornou {(int)response.StatusCode}: {TrimError(body)}");
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("text", out var text) ? text.GetString() ?? "" : "";
    }

    public async Task<bool> TestOllamaAsync(CancellationToken ct = default)
    {
        try { using var response = await _http.GetAsync(_config.OllamaBaseUrl.TrimEnd('/') + "/api/tags", ct); return response.IsSuccessStatusCode; }
        catch { return false; }
    }

    private async Task<string> TranslateGeminiAsync(string prompt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_config.GeminiApiKey)) throw new InvalidOperationException("Configure a API key do Gemini.");
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(_config.GeminiModel)}:generateContent?key={Uri.EscapeDataString(_config.GeminiApiKey)}";
        var payload = new { contents = new[] { new { role = "user", parts = new[] { new { text = prompt } } } }, generationConfig = new { temperature = 0.15, maxOutputTokens = 1200 } };
        using var response = await _http.PostAsJsonAsync(url, payload, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Gemini retornou {(int)response.StatusCode}: {TrimError(body)}");
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
        {
            var content = candidates[0].GetProperty("content");
            if (content.TryGetProperty("parts", out var parts) && parts.GetArrayLength() > 0) return CleanResult(parts[0].GetProperty("text").GetString() ?? "");
        }
        throw new InvalidOperationException("Gemini não retornou texto traduzido.");
    }

    private async Task<string> TranslateOpenAiCompatibleAsync(string baseUrl, string apiKey, string model, string prompt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("Configure a API key do provedor selecionado.");
        string url = baseUrl.TrimEnd('/'); if (!url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) url += "/v1"; url += "/chat/completions";
        var payload = new { model, temperature = 0.15, messages = new[] { new { role = "user", content = prompt } } };
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"API retornou {(int)response.StatusCode}: {TrimError(body)}");
        using var doc = JsonDocument.Parse(body);
        return CleanResult(doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "");
    }

    private async Task<string> TranslateOllamaAsync(string prompt, CancellationToken ct)
    {
        var payload = new { model = _config.LocalModel, stream = false, messages = new[] { new { role = "user", content = prompt } }, options = new { temperature = 0.15 } };
        using var response = await _http.PostAsJsonAsync(_config.OllamaBaseUrl.TrimEnd('/') + "/api/chat", payload, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Ollama retornou {(int)response.StatusCode}: {TrimError(body)}");
        using var doc = JsonDocument.Parse(body);
        return CleanResult(doc.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "");
    }

    private static string BuildPrompt(string text, string source, string target, string style)
    {
        string styleInstruction = style switch
        {
            "Gaming" => "Use natural gamer/chat language. Translate intent rather than literally. Keep common gaming terms such as GG, AFK, OP, nerf, buff, rush and camper natural in the target language.",
            "Casual" => "Use natural, informal everyday chat language.",
            "Formal" => "Use clear, polite and formal language.",
            "Literal" => "Stay close to the original wording while remaining grammatical.",
            _ => "Use natural conversational language."
        };
        return $"You are Refrigerator, a real-time chat translator.\nTranslate the text from {source} to {target}.\n{styleInstruction}\nRules:\n- Return ONLY the translated text. No quotes, explanations, labels or markdown.\n- Preserve usernames, @mentions, URLs, IDs, emojis and game-specific names.\n- Do not censor or add content that was not present.\n- If the input is already naturally written in the target language, return it unchanged.\n\nTEXT:\n{text}";
    }

    private static string CleanResult(string value)
    {
        var s = value.Trim();
        if (s.Length >= 2 && ((s.StartsWith('"') && s.EndsWith('"')) || (s.StartsWith('“') && s.EndsWith('”')))) s = s[1..^1].Trim();
        return s;
    }

    private static string TrimError(string body) { body = body.Replace('\r', ' ').Replace('\n', ' ').Trim(); return body.Length <= 260 ? body : body[..260] + "..."; }
}
