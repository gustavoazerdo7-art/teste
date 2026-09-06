using System.Drawing.Imaging;
using System.Text;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Refrigerator;

public sealed class TranslationOverlayForm : Form
{
    private readonly Label _text;
    public TranslationOverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; BackColor = Color.FromArgb(20,22,26); Opacity = 0.88; Padding = new Padding(10); AutoScaleMode = AutoScaleMode.Dpi;
        _text = new Label { Dock = DockStyle.Fill, ForeColor = Color.White, BackColor = Color.Transparent, Font = new Font("Segoe UI",10.5f), AutoEllipsis = true, TextAlign = ContentAlignment.TopLeft };
        Controls.Add(_text);
    }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { const int WS_EX_TRANSPARENT=0x20, WS_EX_TOOLWINDOW=0x80, WS_EX_NOACTIVATE=0x08000000; var cp=base.CreateParams; cp.ExStyle |= WS_EX_TRANSPARENT|WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE; return cp; }
    }
    public void ShowTranslation(string text, Rectangle chatRegion, Rectangle windowRect)
    {
        if (string.IsNullOrWhiteSpace(text)) { Hide(); return; }
        _text.Text = text.Trim();
        int width=Math.Max(280,chatRegion.Width), height=Math.Min(190,Math.Max(70,chatRegion.Height/2)), x=chatRegion.Left, y=chatRegion.Bottom+4;
        if (y+height>windowRect.Bottom) { x=chatRegion.Right+4; y=chatRegion.Top; if (x+width>windowRect.Right) { x=chatRegion.Left; y=Math.Max(windowRect.Top,chatRegion.Top-height-4); } }
        Bounds = new Rectangle(x,y,width,height); if (!Visible) Show(); BringToFront();
    }
}

public sealed class RegionSelectorForm : Form
{
    private Point _start,_end; private bool _dragging; public Rectangle SelectedRegion { get; private set; }
    public RegionSelectorForm(Rectangle bounds)
    {
        Bounds=bounds; FormBorderStyle=FormBorderStyle.None; TopMost=true; ShowInTaskbar=false; BackColor=Color.Black; Opacity=.28; Cursor=Cursors.Cross; DoubleBuffered=true; KeyPreview=true;
        MouseDown += (_,e)=>{ if(e.Button!=MouseButtons.Left)return; _dragging=true; _start=_end=e.Location; Invalidate(); };
        MouseMove += (_,e)=>{ if(!_dragging)return; _end=e.Location; Invalidate(); };
        MouseUp += (_,e)=>{ if(!_dragging)return; _dragging=false; _end=e.Location; var r=Norm(_start,_end); if(r.Width>=30&&r.Height>=20){ SelectedRegion=new Rectangle(Left+r.Left,Top+r.Top,r.Width,r.Height); DialogResult=DialogResult.OK; Close(); } };
        KeyDown += (_,e)=>{ if(e.KeyCode==Keys.Escape){ DialogResult=DialogResult.Cancel; Close(); } };
    }
    protected override void OnPaint(PaintEventArgs e){ base.OnPaint(e); if(!_dragging)return; var r=Norm(_start,_end); using var p=new Pen(Color.DeepSkyBlue,3); using var b=new SolidBrush(Color.FromArgb(70,Color.DeepSkyBlue)); e.Graphics.FillRectangle(b,r); e.Graphics.DrawRectangle(p,r); }
    private static Rectangle Norm(Point a,Point b)=>Rectangle.FromLTRB(Math.Min(a.X,b.X),Math.Min(a.Y,b.Y),Math.Max(a.X,b.X),Math.Max(a.Y,b.Y));
}

public sealed class RobloxOcrMonitor : IDisposable
{
    private readonly AppConfig _config; private readonly TranslationService _translator; private readonly TranslationOverlayForm _overlay; private readonly System.Windows.Forms.Timer _timer;
    private bool _busy; private long _lastHash; private string _lastOcr="",_lastTranslation=""; private OcrEngine? _ocr;
    public event Action<string>? StatusChanged;
    public RobloxOcrMonitor(AppConfig config,TranslationService translator,TranslationOverlayForm overlay){ _config=config; _translator=translator; _overlay=overlay; _timer=new System.Windows.Forms.Timer{Interval=Math.Clamp(config.OcrIntervalMs,350,3000)}; _timer.Tick += async(_,_)=>await TickAsync(); }
    public void Start(){ _timer.Interval=Math.Clamp(_config.OcrIntervalMs,350,3000); _timer.Start(); }
    public void Stop(){ _timer.Stop(); _overlay.Hide(); }
    private async Task TickAsync()
    {
        if(_busy||!_config.TranslationEnabled||!_config.RobloxEnabled||!_config.IncomingOverlayEnabled||!_config.HasRobloxRegion)return;
        var hwnd=Win32.FindMainWindow("RobloxPlayerBeta","RobloxPlayer"); if(hwnd==IntPtr.Zero){ if(_overlay.Visible)_overlay.Hide(); return; }
        if(!Win32.GetWindowRect(hwnd,out var wr)||wr.Width<100||wr.Height<100)return;
        var windowRect=wr.ToRectangle(); var region=RegionFromConfig(windowRect); if(region.Width<30||region.Height<20)return;
        _busy=true;
        try
        {
            using var bmp=Capture(region); var hash=ComputeSampleHash(bmp); if(hash==_lastHash)return; _lastHash=hash;
            StatusChanged?.Invoke("OCR: lendo chat do Roblox..."); var text=NormalizeOcr(await ReadOcrAsync(bmp)); if(text.Length<2||text==_lastOcr)return; _lastOcr=text;
            var translated=await _translator.TranslateAsync(text,"Auto",_config.IncomingTargetLanguage); if(string.IsNullOrWhiteSpace(translated)||translated==_lastTranslation)return; _lastTranslation=translated;
            _overlay.ShowTranslation(translated,region,windowRect); StatusChanged?.Invoke("Roblox: tradução recebida atualizada.");
        }
        catch(Exception ex){ StatusChanged?.Invoke("OCR/overlay: "+ex.Message); }
        finally{ _busy=false; }
    }
    public Rectangle RegionFromConfig(Rectangle w){ int x=w.Left+(int)Math.Round(_config.RobloxRegionX*w.Width), y=w.Top+(int)Math.Round(_config.RobloxRegionY*w.Height), ww=(int)Math.Round(_config.RobloxRegionW*w.Width), hh=(int)Math.Round(_config.RobloxRegionH*w.Height); return Rectangle.Intersect(new Rectangle(x,y,Math.Max(1,ww),Math.Max(1,hh)),w); }
    public void SetRegion(Rectangle s,Rectangle w){ _config.RobloxRegionX=(s.Left-w.Left)/(double)w.Width; _config.RobloxRegionY=(s.Top-w.Top)/(double)w.Height; _config.RobloxRegionW=s.Width/(double)w.Width; _config.RobloxRegionH=s.Height/(double)w.Height; _lastHash=0; _lastOcr=""; }
    private static Bitmap Capture(Rectangle r){ var bmp=new Bitmap(r.Width,r.Height,PixelFormat.Format32bppArgb); using var g=Graphics.FromImage(bmp); g.CopyFromScreen(r.Location,Point.Empty,r.Size,CopyPixelOperation.SourceCopy); return bmp; }
    private static long ComputeSampleHash(Bitmap source){ using var tiny=new Bitmap(20,12); using(var g=Graphics.FromImage(tiny))g.DrawImage(source,new Rectangle(0,0,tiny.Width,tiny.Height)); unchecked{ long h=1469598103934665603L; for(int y=0;y<tiny.Height;y++)for(int x=0;x<tiny.Width;x++){ h^=tiny.GetPixel(x,y).ToArgb(); h*=1099511628211L; } return h; } }
    private async Task<string> ReadOcrAsync(Bitmap bitmap)
    {
        _ocr ??= OcrEngine.TryCreateFromUserProfileLanguages(); if(_ocr is null)throw new InvalidOperationException("Windows OCR indisponível neste idioma/Windows.");
        byte[] png; using(var ms=new MemoryStream()){ bitmap.Save(ms,ImageFormat.Png); png=ms.ToArray(); }
        using var stream=new InMemoryRandomAccessStream(); using(var writer=new DataWriter(stream)){ writer.WriteBytes(png); await writer.StoreAsync(); writer.DetachStream(); } stream.Seek(0);
        var decoder=await BitmapDecoder.CreateAsync(stream); using var software=await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied); var result=await _ocr.RecognizeAsync(software); return result.Text??"";
    }
    private static string NormalizeOcr(string text){ var sb=new StringBuilder(); foreach(var raw in text.Replace("\r","").Split('\n')){ var line=string.Join(' ',raw.Split(' ',StringSplitOptions.RemoveEmptyEntries)).Trim(); if(line.Length==0)continue; if(sb.Length>0)sb.AppendLine(); sb.Append(line); } var r=sb.ToString(); return r.Length>1800?r[^1800..]:r; }
    public void Dispose(){ _timer.Dispose(); _overlay.Dispose(); }
}
