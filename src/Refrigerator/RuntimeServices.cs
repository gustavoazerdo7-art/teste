using System.Diagnostics;
using NAudio.Wave;

namespace Refrigerator;

public sealed class MicrophoneService : IDisposable
{
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private MemoryStream? _stream;
    public bool IsRecording => _waveIn is not null;
    public event Action<string>? StatusChanged;

    public void Start()
    {
        if (_waveIn is not null) return;
        _stream = new MemoryStream();
        _waveIn = new WaveInEvent { WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 100 };
        _writer = new WaveFileWriter(_stream, _waveIn.WaveFormat);
        _waveIn.DataAvailable += (_, e) => _writer?.Write(e.Buffer, 0, e.BytesRecorded);
        _waveIn.StartRecording();
        StatusChanged?.Invoke("Ouvindo... Ctrl+Shift+Espaco novamente para concluir.");
    }

    public byte[] Stop()
    {
        if (_waveIn is null || _stream is null) return Array.Empty<byte>();
        try { _waveIn.StopRecording(); } catch { }
        _writer?.Flush();
        _writer?.Dispose();
        var bytes = _stream.ToArray();
        _waveIn.Dispose();
        _stream.Dispose();
        _waveIn = null; _writer = null; _stream = null;
        StatusChanged?.Invoke("Microfone parado.");
        return bytes;
    }

    public void Dispose() { try { if (_waveIn is not null) Stop(); } catch { } }
}

public sealed record RegistratorModel(string DisplayName, string OllamaName, string Disk, string Ram, string Note);

public static class Registrator
{
    public static readonly RegistratorModel[] Models =
    [
        new("Qwen 2.5 0.5B - ultraleve", "qwen2.5:0.5b", "~400 MB", "~1 GB", "Mais rapido; qualidade basica."),
        new("Qwen 2.5 1.5B - recomendado", "qwen2.5:1.5b", "~1 GB", "~2 GB", "Melhor equilibrio para 8 GB de RAM."),
        new("Qwen 2.5 3B - qualidade", "qwen2.5:3b", "~2 GB", "~3-4 GB", "Pode pesar junto do Roblox no seu PC."),
        new("Gemma 3 1B - alternativo", "gemma3:1b", "~815 MB", "~2 GB", "Modelo pequeno multilingue.")
    ];
}

public sealed class LocalAiManager
{
    private readonly AppConfig _config;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
    public LocalAiManager(AppConfig config) => _config = config;

    public string? FindOllama()
    {
        string[] candidates =
        [
            Path.Combine(AppPaths.Ollama, "ollama.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe")
        ];
        foreach (var p in candidates) if (File.Exists(p)) return p;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try { var p = Path.Combine(dir.Trim(), "ollama.exe"); if (File.Exists(p)) return p; } catch { }
        }
        return null;
    }

    public async Task<bool> IsServerAliveAsync()
    {
        try { using var r = await _http.GetAsync(_config.OllamaBaseUrl.TrimEnd('/') + "/api/tags"); return r.IsSuccessStatusCode; }
        catch { return false; }
    }

    public async Task<string> InstallOllamaAsync(IProgress<string>? progress = null)
    {
        if (!Directory.Exists(@"D:\")) throw new InvalidOperationException("O disco D: nao foi encontrado.");
        AppPaths.Ensure();
        Directory.CreateDirectory(Path.Combine(AppPaths.Models, "Ollama"));
        string models = Path.Combine(AppPaths.Models, "Ollama");
        Environment.SetEnvironmentVariable("OLLAMA_MODELS", models, EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable("OLLAMA_MODELS", models, EnvironmentVariableTarget.Process);
        var existing = FindOllama(); if (existing is not null) return existing;

        string installer = Path.Combine(AppPaths.Downloads, "OllamaSetup.exe");
        progress?.Report("Baixando instalador oficial do Ollama...");
        using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) })
        using (var response = await client.GetAsync("https://ollama.com/download/OllamaSetup.exe", HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            await using var src = await response.Content.ReadAsStreamAsync();
            await using var dst = File.Create(installer);
            await src.CopyToAsync(dst);
        }
        progress?.Report("Abrindo instalador oficial. Os modelos ficarao no disco D:.");
        using var p = Process.Start(new ProcessStartInfo { FileName = installer, UseShellExecute = true }) ?? throw new InvalidOperationException("Nao foi possivel abrir o instalador.");
        await p.WaitForExitAsync();
        var found = FindOllama();
        if (found is null) throw new InvalidOperationException("Ollama ainda nao foi detectado. Termine a instalacao e clique em Detectar Ollama.");
        return found;
    }

    public async Task StartServerIfNeededAsync(string exe)
    {
        if (await IsServerAliveAsync()) return;
        string models = Path.Combine(AppPaths.Models, "Ollama"); Directory.CreateDirectory(models);
        var psi = new ProcessStartInfo { FileName = exe, Arguments = "serve", UseShellExecute = false, CreateNoWindow = true };
        psi.Environment["OLLAMA_MODELS"] = models;
        Process.Start(psi);
        for (int i = 0; i < 20; i++) { await Task.Delay(500); if (await IsServerAliveAsync()) return; }
        throw new InvalidOperationException("A API local do Ollama nao respondeu.");
    }

    public async Task PullModelAsync(string model, IProgress<string>? progress = null)
    {
        var exe = FindOllama() ?? throw new InvalidOperationException("Ollama nao encontrado.");
        await StartServerIfNeededAsync(exe);
        progress?.Report("Baixando " + model + " para D:\\Refrigerator\\Models\\Ollama...");
        var psi = new ProcessStartInfo { FileName = exe, Arguments = $"pull {model}", UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        psi.Environment["OLLAMA_MODELS"] = Path.Combine(AppPaths.Models, "Ollama");
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Nao foi possivel iniciar o download do modelo.");
        string output = await p.StandardOutput.ReadToEndAsync(); string error = await p.StandardError.ReadToEndAsync(); await p.WaitForExitAsync();
        if (p.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? output : error);
        progress?.Report("Modelo instalado: " + model);
    }
}
