using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace Refrigerator;

public sealed class AppConfig
{
    public bool TranslationEnabled { get; set; } = true;
    public bool StartWithWindows { get; set; } = true;
    public bool StartMinimized { get; set; } = false;
    public string SourceLanguage { get; set; } = "Português (Brasil)";
    public string TargetLanguage { get; set; } = "English";
    public string IncomingTargetLanguage { get; set; } = "Português (Brasil)";
    public string Style { get; set; } = "Gaming";
    public string Provider { get; set; } = "Gemini";
    public string GeminiApiKeyProtected { get; set; } = "";
    public string GeminiModel { get; set; } = "gemini-2.5-flash";
    public string GroqApiKeyProtected { get; set; } = "";
    public string GroqModel { get; set; } = "llama-3.1-8b-instant";
    public string CustomApiKeyProtected { get; set; } = "";
    public string CustomBaseUrl { get; set; } = "https://api.openai.com/v1";
    public string CustomModel { get; set; } = "gpt-4.1-mini";
    public string LocalModel { get; set; } = "qwen2.5:1.5b";
    public string OllamaBaseUrl { get; set; } = "http://127.0.0.1:11434";
    public bool RobloxEnabled { get; set; } = true;
    public bool DiscordEnabled { get; set; } = true;
    public bool GenericAppsEnabled { get; set; } = true;
    public bool IncomingOverlayEnabled { get; set; } = true;
    public double RobloxRegionX { get; set; } = -1;
    public double RobloxRegionY { get; set; } = -1;
    public double RobloxRegionW { get; set; } = -1;
    public double RobloxRegionH { get; set; } = -1;
    public int OcrIntervalMs { get; set; } = 700;
    public bool MicrophoneEnabled { get; set; } = false;
    public string SpeechModel { get; set; } = "whisper-large-v3-turbo";
    public int TranslateHotkeyModifiers { get; set; } = 0x0001;
    public int TranslateHotkeyKey { get; set; } = 0x0D;
    public int MicHotkeyModifiers { get; set; } = 0x0002 | 0x0004;
    public int MicHotkeyKey { get; set; } = 0x20;

    public string GeminiApiKey { get => SecretBox.Unprotect(GeminiApiKeyProtected); set => GeminiApiKeyProtected = SecretBox.Protect(value); }
    public string GroqApiKey { get => SecretBox.Unprotect(GroqApiKeyProtected); set => GroqApiKeyProtected = SecretBox.Protect(value); }
    public string CustomApiKey { get => SecretBox.Unprotect(CustomApiKeyProtected); set => CustomApiKeyProtected = SecretBox.Protect(value); }
    public bool HasRobloxRegion => RobloxRegionX >= 0 && RobloxRegionY >= 0 && RobloxRegionW > 0 && RobloxRegionH > 0;
}

public static class AppPaths
{
    public static string Root => Directory.Exists(@"D:\") ? @"D:\Refrigerator" : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Refrigerator");
    public static string ConfigFile => Path.Combine(Root, "config.json");
    public static string Logs => Path.Combine(Root, "Logs");
    public static string Cache => Path.Combine(Root, "Cache");
    public static string Models => Path.Combine(Root, "Models");
    public static string Downloads => Path.Combine(Root, "Downloads");
    public static string Ollama => Path.Combine(Root, "Ollama");
    public static void Ensure() { Directory.CreateDirectory(Root); Directory.CreateDirectory(Logs); Directory.CreateDirectory(Cache); Directory.CreateDirectory(Models); Directory.CreateDirectory(Downloads); }
}

public static class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public static AppConfig Load()
    {
        AppPaths.Ensure();
        try { if (File.Exists(AppPaths.ConfigFile)) return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(AppPaths.ConfigFile), JsonOptions) ?? new AppConfig(); } catch { }
        return new AppConfig();
    }
    public static void Save(AppConfig config) { AppPaths.Ensure(); File.WriteAllText(AppPaths.ConfigFile, JsonSerializer.Serialize(config, JsonOptions)); }
    public static void ApplyStartup(AppConfig config)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key is null) return;
            if (config.StartWithWindows) key.SetValue("Refrigerator", $"\"{Environment.ProcessPath}\" --minimized"); else key.DeleteValue("Refrigerator", false);
        }
        catch { }
    }
}

public static class SecretBox
{
    public static string Protect(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        try { return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser)); } catch { return value; }
    }
    public static string Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser)); } catch { return value; }
    }
}
