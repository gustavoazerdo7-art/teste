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
        _writer = new WaveFileWriter(new IgnoreDisposeStream(_stream), _waveIn.WaveFormat);
        _waveIn.DataAvailable += (_, e) => _writer?.Write(e.Buffer, 0, e.BytesRecorded);
        _waveIn.RecordingStopped += (_, _) => StatusChanged?.Invoke("Microfone parado.");
        _waveIn.StartRecording();
        StatusChanged?.Invoke("🎙 Ouvindo... pressione Ctrl+Shift+Espaço novamente para concluir.");
    }

    public byte[] Stop()
    {
        if (_waveIn is null || _stream is null) return Array.Empty<byte>();
        try { _waveIn.StopRecording(); } catch { }
        _writer?.Flush(); _writer?.Dispose();
        var bytes = _stream.ToArray();
        _waveIn.Dispose(); _stream.Dispose();
        _waveIn = null; _writer = null; _stream = null;
        return bytes;
    }

    public void Dispose() { try { if (_waveIn is not null) Stop(); } catch { } }
}

public sealed record RegistratorModel(string DisplayName, string OllamaName, string Disk, string Ram, string Note);

public static class Registrator
{
    public static readonly RegistratorModel[] Models =
    [
        new("Qwen 2.5 0.5B — ultraleve", "qwen2.5:0.5b", "~400 MB", "~1 GB", "Mais rápido; qualidade básica."),
        new("Qwen 2.5 1.5B — recomendado", "qwen2.5:1.5b", "~1 GB", "~2 GB", "Melhor equilíbrio para 8 GB de RAM."),
        new("Qwen 2.5 3B — qualidade", "qwen2.5:3b", "~2 GB", "~3-4 GB", "Pode pesar junto do Roblox no seu PC."),
        new("Gemma 3 1B — alternativo", "gemma3:1b", "~815 MB", "~2 GB", "Modelo pequeno multilíngue.")
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
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(';', StringSplitOptions.RemoveEmptyEntries))
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

    public async Task<string> InstallOllamaToDAsync(IProgress<string>? progress = null)
    {
        if (!Directory.Exists(@"D:\")) throw new InvalidOperationException("O disco D: não foi encontrado.");
        AppPaths.Ensure();
        Directory.CreateDirectory(AppPaths.Ollama);
        Directory.CreateDirectory(Path.Combine(AppPaths.Models, "Ollama"));
        Environment.SetEnvironmentVariable("OLLAMA_MODELS", Path.Combine(AppPaths.Models, "Ollama"), EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable("OLLAMA_MODELS", Path.Combine(AppPaths.Models, "Ollama"), EnvironmentVariableTarget.Process);
        var existing = FindOllama(); if (existing is not null) return existing;
        var installer = Path.Combine(AppPaths.Downloads, "OllamaSetup.exe");
        progress?.Report("Baixando instalador oficial do Ollama...");
        using (var download = new HttpClient { Timeout = TimeSpan.FromMinutes(15) })
        using (var response = await download.GetAsync("https://ollama.com/download/OllamaSetup.exe", HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            await using var src = await response.Content.ReadAsStreamAsync();
            await using var dst = File.Create(installer);
            await src.CopyToAsync(dst);
        }
        progress?.Report("Abrindo instalador do Ollama. Os MODELOS ficarão no D:.");
        using var p = Process.Start(new ProcessStartInfo { FileName = installer, UseShellExecute = true }) ?? throw new InvalidOperationException("Não foi possível iniciar o instalador do Ollama.");
        await p.WaitForExitAsync();
        var found = FindOllama();
        if (found is null) throw new InvalidOperationException("O instalador terminou, mas Refrigerator ainda não encontrou ollama.exe. Use Detectar novamente depois da instalação.");
        return found;
    }

    public async Task StartServerIfNeededAsync(string ollamaExe)
    {
        if (await IsServerAliveAsync()) return;
        var models = Path.Combine(AppPaths.Models, "Ollama"); Directory.CreateDirectory(models);
        var psi = new ProcessStartInfo { FileName = ollamaExe, Arguments = "serve", UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        psi.Environment["OLLAMA_MODELS"] = models;
        Process.Start(psi);
        for (int i = 0; i < 20; i++) { await Task.Delay(500); if (await IsServerAliveAsync()) return; }
        throw new InvalidOperationException("Ollama foi iniciado, mas a API local não respondeu.");
    }

    public async Task PullModelAsync(string model, IProgress<string>? progress = null)
    {
        var exe = FindOllama() ?? throw new InvalidOperationException("Ollama não foi encontrado. Instale-o primeiro.");
        await StartServerIfNeededAsync(exe);
        progress?.Report("Baixando modelo " + model + " para D:\\Refrigerator\\Models...");
        var psi = new ProcessStartInfo { FileName = exe, Arguments = $"pull {model}", UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        psi.Environment["OLLAMA_MODELS"] = Path.Combine(AppPaths.Models, "Ollama");
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Não foi possível iniciar ollama pull.");
        var outputTask = p.StandardOutput.ReadToEndAsync(); var errorTask = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync(); var output = await outputTask; var error = await errorTask;
        if (p.ExitCode != 0) throw new InvalidOperationException("Falha ao baixar modelo: " + (string.IsNullOrWhiteSpace(error) ? output : error));
        progress?.Report("Modelo instalado: " + model);
    }
}
