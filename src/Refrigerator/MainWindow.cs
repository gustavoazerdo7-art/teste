namespace Refrigerator;

public sealed class MainForm : Form
{
    private const int HK_TRANSLATE = 101;
    private const int HK_MIC = 102;
    private readonly AppConfig cfg;
    private readonly TranslationService translator;
    private readonly LocalAiManager local;
    private readonly MicrophoneService mic = new();
    private readonly TranslationOverlayForm overlay = new();
    private readonly RobloxOcrMonitor ocr;
    private readonly NotifyIcon tray;
    private bool exitNow, busy;

    private readonly Label status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly CheckBox enabled = new() { Text = "Ativar traducao" };
    private readonly CheckBox startWin = new() { Text = "Iniciar com Windows" };
    private readonly CheckBox startMin = new() { Text = "Iniciar minimizado" };
    private readonly ComboBox source = Combo("Portugues (Brasil)", "English", "Spanish", "French", "German", "Japanese", "Korean", "Auto");
    private readonly ComboBox target = Combo("English", "Portugues (Brasil)", "Spanish", "French", "German", "Japanese", "Korean");
    private readonly ComboBox incoming = Combo("Portugues (Brasil)", "English", "Spanish");
    private readonly ComboBox style = Combo("Gaming", "Natural", "Casual", "Literal", "Formal");
    private readonly ComboBox provider = Combo("Gemini", "Groq", "OpenAI-compatible", "Local (Ollama)");
    private readonly TextBox geminiKey = Secret();
    private readonly TextBox geminiModel = new();
    private readonly TextBox groqKey = Secret();
    private readonly TextBox groqModel = new();
    private readonly TextBox customKey = Secret();
    private readonly TextBox customBase = new();
    private readonly TextBox customModel = new();
    private readonly CheckBox roblox = new() { Text = "Roblox" };
    private readonly CheckBox discord = new() { Text = "Discord" };
    private readonly CheckBox generic = new() { Text = "Outros aplicativos Windows (Alt+Enter)" };
    private readonly CheckBox overlayOn = new() { Text = "Overlay de mensagens recebidas no Roblox" };
    private readonly Label regionStatus = new() { AutoSize = true };
    private readonly CheckBox microphone = new() { Text = "Ativar microfone / voz" };
    private readonly TextBox speechModel = new();
    private readonly ComboBox localModel = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 430 };
    private readonly Label ollamaStatus = new() { AutoSize = true, MaximumSize = new Size(680, 0) };

    public MainForm(AppConfig config, bool hidden)
    {
        cfg = config; translator = new(cfg); local = new(cfg); ocr = new(cfg, translator, overlay);
        Text = "Refrigerator"; Icon = Brand.Icon(); Width = 900; Height = 700; MinimumSize = new Size(780, 580); StartPosition = FormStartPosition.CenterScreen; Font = new Font("Segoe UI", 9.5f); BackColor = Color.FromArgb(247, 249, 250);
        ocr.StatusChanged += SetStatus; mic.StatusChanged += SetStatus;
        BuildUi(); LoadCfg();

        tray = new NotifyIcon { Visible = true, Icon = Icon, Text = "Refrigerator - tradutor" };
        tray.DoubleClick += (_, _) => OpenSettings();
        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir Refrigerator", null, (_, _) => OpenSettings());
        menu.Items.Add("Ativar/Pausar", null, (_, _) => { cfg.TranslationEnabled = !cfg.TranslationEnabled; ConfigStore.Save(cfg); SetStatus(cfg.TranslationEnabled ? "Traducao ativada" : "Traducao pausada"); });
        menu.Items.Add("Mostrar/Ocultar overlay", null, (_, _) => { cfg.IncomingOverlayEnabled = !cfg.IncomingOverlayEnabled; ConfigStore.Save(cfg); if (!cfg.IncomingOverlayEnabled) overlay.Hide(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => ExitApp()); tray.ContextMenuStrip = menu;

        Shown += (_, _) => { RegisterHotkeys(); ConfigStore.ApplyStartup(cfg); ocr.Start(); if (hidden || cfg.StartMinimized) BeginInvoke(new Action(Hide)); };
        FormClosing += (_, e) => { if (!exitNow) { e.Cancel = true; Hide(); tray.ShowBalloonTip(900, "Refrigerator", "Continua rodando em segundo plano.", ToolTipIcon.Info); } };
    }

    private void BuildUi()
    {
        var head = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Color.White };
        head.Controls.Add(new PictureBox { Image = Brand.Image(), SizeMode = PictureBoxSizeMode.Zoom, Bounds = new Rectangle(18, 12, 64, 64) });
        head.Controls.Add(new Label { Text = "Refrigerator", Font = new Font("Segoe UI Semibold", 22), AutoSize = true, Location = new Point(96, 16) });
        head.Controls.Add(new Label { Text = "Real-time translation layer - Roblox - Discord - Windows", ForeColor = Color.DimGray, AutoSize = true, Location = new Point(99, 55) });
        Controls.Add(head);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(GeneralTab()); tabs.TabPages.Add(TranslationTab()); tabs.TabPages.Add(AppsTab()); tabs.TabPages.Add(MicTab()); tabs.TabPages.Add(LocalTab()); tabs.TabPages.Add(AboutTab());
        Controls.Add(tabs); tabs.BringToFront();

        var foot = new Panel { Dock = DockStyle.Bottom, Height = 58, BackColor = Color.White, Padding = new Padding(12) };
        foot.Controls.Add(status); var save = new Button { Text = "Salvar", Dock = DockStyle.Right, Width = 130 }; save.Click += (_, _) => SaveCfg(); foot.Controls.Add(save); Controls.Add(foot);
    }

    private TabPage GeneralTab() { var p = Page("Geral"); p.Controls.Add(Stack(Group("Execucao", enabled, startWin, startMin), Group("Atalhos", Info("Alt + Enter - traduz o campo ativo e substitui o texto sem enviar.\nCtrl + Shift + Espaco - inicia/para o microfone.\n\nDepois da traducao, voce ainda aperta Enter para enviar.")))); return p; }
    private TabPage TranslationTab()
    {
        var p = Page("Traducao & APIs"); var test = new Button { Text = "Testar traducao", Width = 170 }; test.Click += async (_, _) => await TestTranslation();
        p.Controls.Add(Stack(Form("Idiomas e estilo", Row("Eu escrevo em", source), Row("Enviar como", target), Row("Recebidas ->", incoming), Row("Estilo", style), Row("Engine", provider)), Form("Gemini", Row("API key", geminiKey), Row("Modelo", geminiModel)), Form("Groq", Row("API key", groqKey), Row("Modelo", groqModel)), Form("OpenAI-compatible", Row("API key", customKey), Row("Base URL", customBase), Row("Modelo", customModel)), Group("Teste", test))); return p;
    }
    private TabPage AppsTab()
    {
        var p = Page("Aplicativos"); var calibrate = new Button { Text = "Calibrar regiao do chat do Roblox", Width = 270 }; calibrate.Click += (_, _) => Calibrate();
        p.Controls.Add(Stack(Group("Conexoes", roblox, discord, generic, overlayOn), Group("Roblox - traducao recebida", calibrate, regionStatus, Info("Abra o Roblox com o chat visivel, clique em Calibrar e selecione somente a area das mensagens. Refrigerator usa OCR local e exibe a traducao em um overlay proximo ao chat.")), Group("Saida", Info("Em Roblox, Discord ou outro campo de texto: escreva -> Alt+Enter -> Refrigerator troca pelo idioma escolhido -> revise -> Enter para enviar.")))); return p;
    }
    private TabPage MicTab() { var p = Page("Microfone"); p.Controls.Add(Stack(Group("Voice translation", microphone, Info("Ctrl+Shift+Espaco comeca a ouvir; pressione de novo para concluir. O audio e transcrito via Groq, traduzido e colocado no campo ativo. O Refrigerator nao salva o audio.")), Form("Speech-to-text", Row("Modelo Groq STT", speechModel)))); return p; }
    private TabPage LocalTab()
    {
        foreach (var m in Registrator.Models) localModel.Items.Add(m.DisplayName);
        var detect = new Button { Text = "Detectar Ollama", Width = 150 }; detect.Click += async (_, _) => await DetectOllama();
        var install = new Button { Text = "Instalar Ollama", Width = 150 }; install.Click += async (_, _) => await InstallOllama();
        var pull = new Button { Text = "Baixar modelo", Width = 150 }; pull.Click += async (_, _) => await PullModel();
        var p = Page("Local AI / Registrator"); p.Controls.Add(Stack(Form("Registrator", Row("Modelo", localModel), Info("Os modelos e caches ficam em D:\\Refrigerator\\Models. Para 8 GB de RAM, Qwen 2.5 1.5B e a opcao recomendada.")), Group("Ollama", detect, install, pull, ollamaStatus))); return p;
    }
    private TabPage AboutTab() { var p = Page("Sobre"); p.Controls.Add(Stack(Group("Refrigerator v0.1 MVP", Info("Assistente de traducao/acessibilidade. Nao injeta DLL, nao le memoria do Roblox e nao automatiza gameplay.\n\nDados grandes: D:\\Refrigerator\nAPI keys: protegidas com DPAPI do Windows.")))); return p; }

    protected override void WndProc(ref Message m) { if (m.Msg == Win32.WM_HOTKEY) { int id = m.WParam.ToInt32(); if (id == HK_TRANSLATE) _ = TranslateActive(); if (id == HK_MIC) _ = ToggleMic(); } base.WndProc(ref m); }
    private void RegisterHotkeys() { if (!IsHandleCreated) return; Win32.UnregisterHotKey(Handle, HK_TRANSLATE); Win32.UnregisterHotKey(Handle, HK_MIC); Win32.RegisterHotKey(Handle, HK_TRANSLATE, cfg.TranslateHotkeyModifiers | Win32.MOD_NOREPEAT, cfg.TranslateHotkeyKey); Win32.RegisterHotKey(Handle, HK_MIC, cfg.MicHotkeyModifiers | Win32.MOD_NOREPEAT, cfg.MicHotkeyKey); }

    private async Task TranslateActive()
    {
        if (busy || !cfg.TranslationEnabled || !Win32.IsAllowedForeground(cfg)) return; busy = true;
        try
        {
            string? old = null; try { if (Clipboard.ContainsText()) old = Clipboard.GetText(); Clipboard.Clear(); } catch { }
            SendKeys.SendWait("^a"); SendKeys.SendWait("^c"); await Task.Delay(150); string text = ""; try { if (Clipboard.ContainsText()) text = Clipboard.GetText().Trim(); } catch { }
            if (string.IsNullOrWhiteSpace(text)) { SetStatus("Nenhum texto encontrado no campo ativo."); return; }
            SetStatus("Traduzindo..."); var translated = await translator.TranslateAsync(text, cfg.SourceLanguage, cfg.TargetLanguage); Clipboard.SetText(translated); SendKeys.SendWait("^a"); SendKeys.SendWait("^v");
            if (old is not null) { await Task.Delay(300); try { Clipboard.SetText(old); } catch { } } SetStatus("Traducao pronta. Pressione Enter para enviar.");
        }
        catch (Exception ex) { SetStatus("Erro: " + ex.Message); tray.ShowBalloonTip(1800, "Refrigerator", ex.Message, ToolTipIcon.Warning); }
        finally { busy = false; }
    }

    private async Task ToggleMic()
    {
        if (!cfg.MicrophoneEnabled || !cfg.TranslationEnabled || !Win32.IsAllowedForeground(cfg)) return;
        try
        {
            if (!mic.IsRecording) { mic.Start(); return; }
            SetStatus("Processando voz..."); var wav = mic.Stop(); if (wav.Length < 1000) { SetStatus("Audio muito curto."); return; }
            var txt = await translator.TranscribeGroqAsync(wav); var translated = await translator.TranslateAsync(txt, cfg.SourceLanguage, cfg.TargetLanguage); Clipboard.SetText(translated); SendKeys.SendWait("^a"); SendKeys.SendWait("^v"); SetStatus("Voz traduzida. Revise e envie.");
        }
        catch (Exception ex) { SetStatus("Microfone: " + ex.Message); }
    }

    private void Calibrate()
    {
        var hwnd = Win32.FindMainWindow("RobloxPlayerBeta", "RobloxPlayer"); if (hwnd == IntPtr.Zero || !Win32.GetWindowRect(hwnd, out var wr)) { MessageBox.Show(this, "Abra o Roblox Player e deixe a janela visivel.", "Refrigerator"); return; }
        var window = wr.ToRectangle(); Hide(); Thread.Sleep(180); using var sel = new RegionSelectorForm(window); var result = sel.ShowDialog(); OpenSettings(); if (result != DialogResult.OK) return; ocr.SetRegion(sel.SelectedRegion, window); ConfigStore.Save(cfg); UpdateRegion(); SetStatus("Regiao do chat salva.");
    }

    private async Task TestTranslation() { SaveCfg(); try { var r = await translator.TranslateAsync("espera ai que eu ja estou entrando", "Portugues (Brasil)", "English"); MessageBox.Show(this, r, "Resultado Refrigerator"); SetStatus("Provider funcionando."); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Falha", MessageBoxButtons.OK, MessageBoxIcon.Warning); } }
    private async Task DetectOllama() { var exe = local.FindOllama(); bool alive = await local.IsServerAliveAsync(); ollamaStatus.Text = exe is null ? "Ollama nao encontrado." : $"Ollama: {exe}\nAPI local: {(alive ? "online" : "offline")}"; }
    private async Task InstallOllama() { SaveCfg(); if (MessageBox.Show(this, "Baixar o instalador oficial do Ollama? Os modelos serao configurados para D:\\Refrigerator\\Models\\Ollama.", "Local AI", MessageBoxButtons.YesNo) != DialogResult.Yes) return; try { var progress = new Progress<string>(SetStatus); await local.InstallOllamaAsync(progress); await DetectOllama(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Ollama"); } }
    private async Task PullModel() { SaveCfg(); try { int i = localModel.SelectedIndex < 0 ? 1 : localModel.SelectedIndex; var m = Registrator.Models[i]; if (MessageBox.Show(this, $"Baixar {m.DisplayName}?\nDisco: {m.Disk}\nRAM: {m.Ram}\nDestino: D:\\Refrigerator\\Models\\Ollama", "Registrator", MessageBoxButtons.YesNo) != DialogResult.Yes) return; var progress = new Progress<string>(SetStatus); await local.PullModelAsync(m.OllamaName, progress); cfg.LocalModel = m.OllamaName; ConfigStore.Save(cfg); MessageBox.Show(this, "Modelo instalado.", "Registrator"); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Registrator"); } }

    private void LoadCfg()
    {
        enabled.Checked = cfg.TranslationEnabled; startWin.Checked = cfg.StartWithWindows; startMin.Checked = cfg.StartMinimized; Select(source, cfg.SourceLanguage); Select(target, cfg.TargetLanguage); Select(incoming, cfg.IncomingTargetLanguage); Select(style, cfg.Style); Select(provider, cfg.Provider);
        geminiKey.Text = cfg.GeminiApiKey; geminiModel.Text = cfg.GeminiModel; groqKey.Text = cfg.GroqApiKey; groqModel.Text = cfg.GroqModel; customKey.Text = cfg.CustomApiKey; customBase.Text = cfg.CustomBaseUrl; customModel.Text = cfg.CustomModel;
        roblox.Checked = cfg.RobloxEnabled; discord.Checked = cfg.DiscordEnabled; generic.Checked = cfg.GenericAppsEnabled; overlayOn.Checked = cfg.IncomingOverlayEnabled; microphone.Checked = cfg.MicrophoneEnabled; speechModel.Text = cfg.SpeechModel;
        int idx = Array.FindIndex(Registrator.Models, m => m.OllamaName == cfg.LocalModel); localModel.SelectedIndex = idx >= 0 ? idx : 1; UpdateRegion(); _ = DetectOllama();
    }
    private void SaveCfg()
    {
        cfg.TranslationEnabled = enabled.Checked; cfg.StartWithWindows = startWin.Checked; cfg.StartMinimized = startMin.Checked; cfg.SourceLanguage = source.Text; cfg.TargetLanguage = target.Text; cfg.IncomingTargetLanguage = incoming.Text; cfg.Style = style.Text; cfg.Provider = provider.Text;
        cfg.GeminiApiKey = geminiKey.Text.Trim(); cfg.GeminiModel = geminiModel.Text.Trim(); cfg.GroqApiKey = groqKey.Text.Trim(); cfg.GroqModel = groqModel.Text.Trim(); cfg.CustomApiKey = customKey.Text.Trim(); cfg.CustomBaseUrl = customBase.Text.Trim(); cfg.CustomModel = customModel.Text.Trim();
        cfg.RobloxEnabled = roblox.Checked; cfg.DiscordEnabled = discord.Checked; cfg.GenericAppsEnabled = generic.Checked; cfg.IncomingOverlayEnabled = overlayOn.Checked; cfg.MicrophoneEnabled = microphone.Checked; cfg.SpeechModel = speechModel.Text.Trim(); if (localModel.SelectedIndex >= 0) cfg.LocalModel = Registrator.Models[localModel.SelectedIndex].OllamaName;
        ConfigStore.Save(cfg); ConfigStore.ApplyStartup(cfg); RegisterHotkeys(); SetStatus("Configuracoes salvas em " + AppPaths.ConfigFile);
    }
    private void UpdateRegion() { regionStatus.Text = cfg.HasRobloxRegion ? "Regiao calibrada." : "Regiao ainda nao calibrada."; regionStatus.ForeColor = cfg.HasRobloxRegion ? Color.DarkGreen : Color.DarkOrange; }
    private void OpenSettings() { Show(); WindowState = FormWindowState.Normal; Activate(); BringToFront(); }
    private void ExitApp() { exitNow = true; try { Win32.UnregisterHotKey(Handle, HK_TRANSLATE); Win32.UnregisterHotKey(Handle, HK_MIC); } catch { } ocr.Dispose(); mic.Dispose(); tray.Visible = false; tray.Dispose(); Close(); Application.Exit(); }
    private void SetStatus(string s) { if (InvokeRequired) { BeginInvoke(new Action(() => SetStatus(s))); return; } status.Text = s; try { string t = "Refrigerator - " + s; tray.Text = t[..Math.Min(60, t.Length)]; } catch { } }

    private static TabPage Page(string t) => new(t) { BackColor = Color.FromArgb(247, 249, 250), Padding = new Padding(12) };
    private static FlowLayoutPanel Stack(params Control[] cs) { var f = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(8) }; foreach (var x in cs) f.Controls.Add(x); return f; }
    private static GroupBox Group(string title, params Control[] cs) { var g = new GroupBox { Text = title, Width = 760, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(12), Margin = new Padding(4, 5, 4, 12) }; var f = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false }; foreach (var c in cs) { c.Margin = new Padding(4, 5, 4, 5); f.Controls.Add(c); } g.Controls.Add(f); return g; }
    private static GroupBox Form(string title, params Control[] c) => Group(title, c);
    private static Control Row(string label, Control input) { var p = new Panel { Width = 690, Height = 38 }; p.Controls.Add(new Label { Text = label, Width = 145, Height = 30, TextAlign = ContentAlignment.MiddleLeft, Location = new Point(0, 2) }); input.Location = new Point(150, 3); input.Width = 470; p.Controls.Add(input); return p; }
    private static Label Info(string t) => new() { Text = t, AutoSize = true, MaximumSize = new Size(690, 0), ForeColor = Color.DimGray };
    private static ComboBox Combo(params string[] values) { var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 470 }; c.Items.AddRange(values); return c; }
    private static TextBox Secret() => new() { UseSystemPasswordChar = true, PlaceholderText = "cole sua API key" };
    private static void Select(ComboBox c, string v) { int i = c.Items.IndexOf(v); c.SelectedIndex = i >= 0 ? i : 0; }
}
