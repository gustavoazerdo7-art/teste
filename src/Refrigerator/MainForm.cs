namespace Refrigerator;

public sealed class MainForm : Form
{
    private const int HK_TRANSLATE=101, HK_MIC=102;
    private readonly AppConfig _cfg;
    private readonly TranslationService _translator;
    private readonly LocalAiManager _local;
    private readonly MicrophoneService _mic=new();
    private readonly TranslationOverlayForm _overlay=new();
    private readonly RobloxOcrMonitor _ocr;
    private readonly NotifyIcon _tray;
    private bool _exit,_busy;

    private readonly Label status=new(){AutoSize=false,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft};
    private readonly CheckBox enabled=new(){Text="Ativar tradução"}, startWin=new(){Text="Iniciar com Windows"}, startMin=new(){Text="Iniciar minimizado"};
    private readonly ComboBox source=Cmb("Português (Brasil)","English","Spanish","French","German","Japanese","Korean","Auto");
    private readonly ComboBox target=Cmb("English","Português (Brasil)","Spanish","French","German","Japanese","Korean");
    private readonly ComboBox incoming=Cmb("Português (Brasil)","English","Spanish");
    private readonly ComboBox style=Cmb("Gaming","Natural","Casual","Literal","Formal");
    private readonly ComboBox provider=Cmb("Gemini","Groq","OpenAI-compatible","Local (Ollama)");
    private readonly TextBox geminiKey=Secret(), geminiModel=new(), groqKey=Secret(), groqModel=new(), customKey=Secret(), customBase=new(), customModel=new();
    private readonly CheckBox roblox=new(){Text="Roblox"}, discord=new(){Text="Discord"}, generic=new(){Text="Outros aplicativos Windows (Alt+Enter)"}, overlayOn=new(){Text="Overlay de tradução recebida no Roblox"};
    private readonly Label regionStatus=new(){AutoSize=true};
    private readonly CheckBox microphone=new(){Text="Ativar microfone / voz"};
    private readonly TextBox speechModel=new();
    private readonly ComboBox localModel=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=430};
    private readonly Label ollamaStatus=new(){AutoSize=true,MaximumSize=new Size(680,0)};

    public MainForm(AppConfig cfg,bool hidden)
    {
        _cfg=cfg; _translator=new(cfg); _local=new(cfg); _ocr=new(cfg,_translator,_overlay);
        Text="Refrigerator"; Icon=Brand.Icon(); Width=900; Height=700; MinimumSize=new Size(780,580); StartPosition=FormStartPosition.CenterScreen; Font=new Font("Segoe UI",9.5f); BackColor=Color.FromArgb(247,249,250);
        _ocr.StatusChanged+=SetStatus; _mic.StatusChanged+=SetStatus;
        BuildUi(); LoadCfg();
        _tray=new NotifyIcon{Visible=true,Icon=Icon,Text="Refrigerator — tradutor"};
        _tray.DoubleClick+=(_,_)=>OpenSettings();
        var menu=new ContextMenuStrip();
        menu.Items.Add("Abrir Refrigerator",null,(_,_)=>OpenSettings());
        menu.Items.Add("Ativar/Pausar",null,(_,_)=>{_cfg.TranslationEnabled=!_cfg.TranslationEnabled;ConfigStore.Save(_cfg);SetStatus(_cfg.TranslationEnabled?"Tradução ativada":"Tradução pausada");});
        menu.Items.Add("Mostrar/Ocultar overlay",null,(_,_)=>{_cfg.IncomingOverlayEnabled=!_cfg.IncomingOverlayEnabled;ConfigStore.Save(_cfg);if(!_cfg.IncomingOverlayEnabled)_overlay.Hide();});
        menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Sair",null,(_,_)=>ExitApp()); _tray.ContextMenuStrip=menu;
        Shown+=(_,_)=>{RegisterHotkeys();ConfigStore.ApplyStartup(_cfg);_ocr.Start();if(hidden||_cfg.StartMinimized)BeginInvoke(new Action(Hide));};
        FormClosing+=(_,e)=>{if(!_exit){e.Cancel=true;Hide();_tray.ShowBalloonTip(900,"Refrigerator","Continua rodando em segundo plano.",ToolTipIcon.Info);}};
    }

    private void BuildUi()
    {
        var head=new Panel{Dock=DockStyle.Top,Height=92,BackColor=Color.White};
        head.Controls.Add(new PictureBox{Image=Brand.Image(),SizeMode=PictureBoxSizeMode.Zoom,Bounds=new Rectangle(18,12,64,64)});
        head.Controls.Add(new Label{Text="Refrigerator",Font=new Font("Segoe UI Semibold",22),AutoSize=true,Location=new Point(96,16)});
        head.Controls.Add(new Label{Text="Real-time translation layer • Roblox • Discord • Windows",ForeColor=Color.DimGray,AutoSize=true,Location=new Point(99,55)});
        Controls.Add(head);
        var tabs=new TabControl{Dock=DockStyle.Fill}; tabs.TabPages.Add(GeneralTab());tabs.TabPages.Add(TranslationTab());tabs.TabPages.Add(AppsTab());tabs.TabPages.Add(MicTab());tabs.TabPages.Add(LocalTab());tabs.TabPages.Add(AboutTab()); Controls.Add(tabs);tabs.BringToFront();
        var foot=new Panel{Dock=DockStyle.Bottom,Height=58,BackColor=Color.White,Padding=new Padding(12)};foot.Controls.Add(status);var save=new Button{Text="Salvar",Dock=DockStyle.Right,Width=130};save.Click+=(_,_)=>SaveCfg();foot.Controls.Add(save);Controls.Add(foot);
    }

    private TabPage GeneralTab()
    {
        var p=Page("Geral");p.Controls.Add(Stack(Group("Execução",enabled,startWin,startMin),Group("Atalhos",Info("Alt + Enter — traduz o campo ativo e substitui o texto sem enviar.\nCtrl + Shift + Espaço — inicia/para o microfone.\n\nDepois da tradução, você continua apertando Enter para enviar."))));return p;
    }
    private TabPage TranslationTab()
    {
        var p=Page("Tradução & APIs");var test=new Button{Text="Testar tradução",Width=170};test.Click+=async(_,_)=>await TestTranslation();
        p.Controls.Add(Stack(Form("Idiomas e estilo",Row("Eu escrevo em",source),Row("Enviar como",target),Row("Recebidas →",incoming),Row("Estilo",style),Row("Engine",provider)),Form("Gemini",Row("API key",geminiKey),Row("Modelo",geminiModel)),Form("Groq",Row("API key",groqKey),Row("Modelo",groqModel)),Form("OpenAI-compatible",Row("API key",customKey),Row("Base URL",customBase),Row("Modelo",customModel)),Group("Teste",test)));return p;
    }
    private TabPage AppsTab()
    {
        var p=Page("Aplicativos");var calibrate=new Button{Text="Calibrar região do chat do Roblox",Width=270};calibrate.Click+=(_,_)=>Calibrate();
        p.Controls.Add(Stack(Group("Conexões",roblox,discord,generic,overlayOn),Group("Roblox — tradução recebida",calibrate,regionStatus,Info("Abra o Roblox com o chat visível, clique em Calibrar e selecione somente a área das mensagens. Refrigerator usa OCR local e mostra a tradução em um overlay próximo ao chat.")),Group("Saída",Info("Em Roblox, Discord ou outro campo de texto: escreva em português → Alt+Enter → Refrigerator troca pelo idioma escolhido → revise → Enter para enviar."))));return p;
    }
    private TabPage MicTab(){var p=Page("Microfone");p.Controls.Add(Stack(Group("Voice translation",microphone,Info("Ctrl+Shift+Espaço começa a ouvir; pressione de novo para concluir. O áudio é transcrito via Groq, traduzido e colocado no campo ativo. O áudio não é salvo.")),Form("Speech-to-text",Row("Modelo Groq STT",speechModel))));return p;}
    private TabPage LocalTab()
    {
        foreach(var m in Registrator.Models)localModel.Items.Add(m.DisplayName);
        var detect=new Button{Text="Detectar Ollama",Width=150},install=new Button{Text="Instalar Ollama",Width=150},pull=new Button{Text="Baixar modelo",Width=150};
        detect.Click+=async(_,_)=>await DetectOllama();install.Click+=async(_,_)=>await InstallOllama();pull.Click+=async(_,_)=>await PullModel();
        var p=Page("Local AI / Registrator");p.Controls.Add(Stack(Form("Registrator",Row("Modelo",localModel),Info("O modelo e os caches ficam em D:\\Refrigerator\\Models quando possível. Para seu PC de 8 GB, o Qwen 2.5 1.5B é a opção recomendada.")),Group("Ollama",detect,install,pull,ollamaStatus)));return p;
    }
    private TabPage AboutTab(){var p=Page("Sobre");p.Controls.Add(Stack(Group("Refrigerator v0.1 MVP",Info("Assistente de tradução/acessibilidade. Não injeta DLL, não lê memória do Roblox e não automatiza gameplay.\n\nDados grandes: D:\\Refrigerator\nAPI keys: protegidas com DPAPI do usuário do Windows."))));return p;}

    protected override void WndProc(ref Message m){if(m.Msg==Win32.WM_HOTKEY){int id=m.WParam.ToInt32();if(id==HK_TRANSLATE)_=TranslateActive();if(id==HK_MIC)_=ToggleMic();}base.WndProc(ref m);}
    private void RegisterHotkeys(){if(!IsHandleCreated)return;Win32.UnregisterHotKey(Handle,HK_TRANSLATE);Win32.UnregisterHotKey(Handle,HK_MIC);Win32.RegisterHotKey(Handle,HK_TRANSLATE,_cfg.TranslateHotkeyModifiers|Win32.MOD_NOREPEAT,_cfg.TranslateHotkeyKey);Win32.RegisterHotKey(Handle,HK_MIC,_cfg.MicHotkeyModifiers|Win32.MOD_NOREPEAT,_cfg.MicHotkeyKey);}

    private async Task TranslateActive()
    {
        if(_busy||!_cfg.TranslationEnabled||!Win32.IsAllowedForeground(_cfg))return;_busy=true;
        try
        {
            string? old=null;try{if(Clipboard.ContainsText())old=Clipboard.GetText();Clipboard.Clear();}catch{}
            SendKeys.SendWait("^a");SendKeys.SendWait("^c");await Task.Delay(150);string text="";try{if(Clipboard.ContainsText())text=Clipboard.GetText().Trim();}catch{}
            if(string.IsNullOrWhiteSpace(text)){SetStatus("Nenhum texto encontrado no campo ativo.");return;}
            SetStatus("Traduzindo...");var translated=await _translator.TranslateAsync(text,_cfg.SourceLanguage,_cfg.TargetLanguage);Clipboard.SetText(translated);SendKeys.SendWait("^a");SendKeys.SendWait("^v");
            if(old is not null){await Task.Delay(300);try{Clipboard.SetText(old);}catch{}}SetStatus("✓ Tradução pronta. Pressione Enter para enviar.");
        }
        catch(Exception ex){SetStatus("Erro: "+ex.Message);_tray.ShowBalloonTip(1800,"Refrigerator",ex.Message,ToolTipIcon.Warning);}finally{_busy=false;}
    }

    private async Task ToggleMic()
    {
        if(!_cfg.MicrophoneEnabled||!_cfg.TranslationEnabled||!Win32.IsAllowedForeground(_cfg))return;
        try
        {
            if(!_mic.IsRecording){_mic.Start();return;}SetStatus("Processando voz...");var wav=_mic.Stop();if(wav.Length<1000){SetStatus("Áudio muito curto.");return;}var txt=await _translator.TranscribeGroqAsync(wav);var translated=await _translator.TranslateAsync(txt,_cfg.SourceLanguage,_cfg.TargetLanguage);Clipboard.SetText(translated);SendKeys.SendWait("^a");SendKeys.SendWait("^v");SetStatus("✓ Voz traduzida. Revise e envie.");
        }
        catch(Exception ex){SetStatus("Microfone: "+ex.Message);}
    }

    private void Calibrate()
    {
        var hwnd=Win32.FindMainWindow("RobloxPlayerBeta","RobloxPlayer");if(hwnd==IntPtr.Zero||!Win32.GetWindowRect(hwnd,out var wr)){MessageBox.Show(this,"Abra o Roblox Player e deixe a janela visível.","Refrigerator");return;}
        var window=wr.ToRectangle();Hide();Thread.Sleep(180);using var sel=new RegionSelectorForm(window);var result=sel.ShowDialog();OpenSettings();if(result!=DialogResult.OK)return;_ocr.SetRegion(sel.SelectedRegion,window);ConfigStore.Save(_cfg);UpdateRegion();SetStatus("Região do chat salva.");
    }

    private async Task TestTranslation(){SaveCfg();try{var r=await _translator.TranslateAsync("espera aí que eu já estou entrando","Português (Brasil)","English");MessageBox.Show(this,r,"Resultado Refrigerator");SetStatus("Provider funcionando.");}catch(Exception ex){MessageBox.Show(this,ex.Message,"Falha",MessageBoxButtons.OK,MessageBoxIcon.Warning);}}
    private async Task DetectOllama(){var exe=_local.FindOllama();bool alive=await _local.IsServerAliveAsync();ollamaStatus.Text=exe is null?"Ollama não encontrado.":$"Ollama: {exe}\nAPI local: {(alive?"online":"offline")}";SetStatus(exe is null?"Ollama não encontrado.":"Ollama detectado.");}
    private async Task InstallOllama(){SaveCfg();if(MessageBox.Show(this,"Baixar o instalador oficial do Ollama? Os modelos serão configurados para D:\\Refrigerator\\Models\\Ollama.","Local AI",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;try{var progress=new Progress<string>(SetStatus);await _local.InstallOllamaToDAsync(progress);await DetectOllama();}catch(Exception ex){MessageBox.Show(this,ex.Message,"Ollama");}}
    private async Task PullModel(){SaveCfg();try{int i=localModel.SelectedIndex<0?1:localModel.SelectedIndex;var m=Registrator.Models[i];if(MessageBox.Show(this,$"Baixar {m.DisplayName}?\nDisco: {m.Disk}\nRAM: {m.Ram}\nDestino: D:\\Refrigerator\\Models\\Ollama","Registrator",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;var progress=new Progress<string>(SetStatus);await _local.PullModelAsync(m.OllamaName,progress);_cfg.LocalModel=m.OllamaName;ConfigStore.Save(_cfg);MessageBox.Show(this,"Modelo instalado.","Registrator");}catch(Exception ex){MessageBox.Show(this,ex.Message,"Registrator");}}

    private void LoadCfg()
    {
        enabled.Checked=_cfg.TranslationEnabled;startWin.Checked=_cfg.StartWithWindows;startMin.Checked=_cfg.StartMinimized;Sel(source,_cfg.SourceLanguage);Sel(target,_cfg.TargetLanguage);Sel(incoming,_cfg.IncomingTargetLanguage);Sel(style,_cfg.Style);Sel(provider,_cfg.Provider);
        geminiKey.Text=_cfg.GeminiApiKey;geminiModel.Text=_cfg.GeminiModel;groqKey.Text=_cfg.GroqApiKey;groqModel.Text=_cfg.GroqModel;customKey.Text=_cfg.CustomApiKey;customBase.Text=_cfg.CustomBaseUrl;customModel.Text=_cfg.CustomModel;
        roblox.Checked=_cfg.RobloxEnabled;discord.Checked=_cfg.DiscordEnabled;generic.Checked=_cfg.GenericAppsEnabled;overlayOn.Checked=_cfg.IncomingOverlayEnabled;microphone.Checked=_cfg.MicrophoneEnabled;speechModel.Text=_cfg.SpeechModel;
        int idx=Array.FindIndex(Registrator.Models,m=>m.OllamaName==_cfg.LocalModel);localModel.SelectedIndex=idx>=0?idx:1;UpdateRegion();_=DetectOllama();
    }
    private void SaveCfg()
    {
        _cfg.TranslationEnabled=enabled.Checked;_cfg.StartWithWindows=startWin.Checked;_cfg.StartMinimized=startMin.Checked;_cfg.SourceLanguage=source.Text;_cfg.TargetLanguage=target.Text;_cfg.IncomingTargetLanguage=incoming.Text;_cfg.Style=style.Text;_cfg.Provider=provider.Text;
        _cfg.GeminiApiKey=geminiKey.Text.Trim();_cfg.GeminiModel=geminiModel.Text.Trim();_cfg.GroqApiKey=groqKey.Text.Trim();_cfg.GroqModel=groqModel.Text.Trim();_cfg.CustomApiKey=customKey.Text.Trim();_cfg.CustomBaseUrl=customBase.Text.Trim();_cfg.CustomModel=customModel.Text.Trim();
        _cfg.RobloxEnabled=roblox.Checked;_cfg.DiscordEnabled=discord.Checked;_cfg.GenericAppsEnabled=generic.Checked;_cfg.IncomingOverlayEnabled=overlayOn.Checked;_cfg.MicrophoneEnabled=microphone.Checked;_cfg.SpeechModel=speechModel.Text.Trim();if(localModel.SelectedIndex>=0)_cfg.LocalModel=Registrator.Models[localModel.SelectedIndex].OllamaName;
        ConfigStore.Save(_cfg);ConfigStore.ApplyStartup(_cfg);RegisterHotkeys();SetStatus("Configurações salvas em "+AppPaths.ConfigFile);
    }
    private void UpdateRegion(){regionStatus.Text=_cfg.HasRobloxRegion?"✓ Região calibrada.":"⚠ Região ainda não calibrada.";regionStatus.ForeColor=_cfg.HasRobloxRegion?Color.DarkGreen:Color.DarkOrange;}
    private void OpenSettings(){Show();WindowState=FormWindowState.Normal;Activate();BringToFront();}
    private void ExitApp(){_exit=true;try{Win32.UnregisterHotKey(Handle,HK_TRANSLATE);Win32.UnregisterHotKey(Handle,HK_MIC);}catch{} _ocr.Dispose();_mic.Dispose();_tray.Visible=false;_tray.Dispose();Close();Application.Exit();}
    private void SetStatus(string s){if(InvokeRequired){BeginInvoke(new Action(()=>SetStatus(s)));return;}status.Text=s;try{_tray.Text=("Refrigerator — "+s)[..Math.Min(60,("Refrigerator — "+s).Length)];}catch{}}

    private static TabPage Page(string t)=>new(t){BackColor=Color.FromArgb(247,249,250),Padding=new Padding(12)};
    private static FlowLayoutPanel Stack(params Control[] c){var f=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(8)};foreach(var x in c)f.Controls.Add(x);return f;}
    private static GroupBox Group(string title,params Control[] cs){var g=new GroupBox{Text=title,Width=760,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Padding=new Padding(12),Margin=new Padding(4,5,4,12)};var f=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false};foreach(var c in cs){c.Margin=new Padding(4,5,4,5);f.Controls.Add(c);}g.Controls.Add(f);return g;}
    private static GroupBox Form(string title,params Control[] c)=>Group(title,c);
    private static Control Row(string label,Control input){var p=new Panel{Width=690,Height=38};p.Controls.Add(new Label{Text=label,Width=145,Height=30,TextAlign=ContentAlignment.MiddleLeft,Location=new Point(0,2)});input.Location=new Point(150,3);input.Width=470;p.Controls.Add(input);return p;}
    private static Label Info(string t)=>new(){Text=t,AutoSize=true,MaximumSize=new Size(690,0),ForeColor=Color.DimGray};
    private static ComboBox Cmb(params string[] values){var c=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=470};c.Items.AddRange(values);return c;}
    private static TextBox Secret()=>new(){UseSystemPasswordChar=true,PlaceholderText="cole sua API key"};
    private static void Sel(ComboBox c,string v){int i=c.Items.IndexOf(v);c.SelectedIndex=i>=0?i:0;}
}
