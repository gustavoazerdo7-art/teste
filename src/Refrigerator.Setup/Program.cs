using System.Diagnostics;
using System.Reflection;

namespace Refrigerator.Setup;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new SetupForm());
    }
}

public sealed class SetupForm : Form
{
    private readonly Label status=new(){AutoSize=false,Width=520,Height=42};
    private readonly ProgressBar bar=new(){Width=520,Height=20};
    private readonly Button install=new(){Text="Instalar no D:",Width=155,Height=38};
    private readonly CheckBox desktop=new(){Text="Criar atalho na Área de Trabalho",Checked=true,AutoSize=true};
    private readonly CheckBox run=new(){Text="Abrir Refrigerator após instalar",Checked=true,AutoSize=true};

    public SetupForm()
    {
        Text="Instalar Refrigerator";Width=610;Height=410;StartPosition=FormStartPosition.CenterScreen;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;Font=new Font("Segoe UI",10);BackColor=Color.White;
        Controls.Add(new Label{Text="Refrigerator",Font=new Font("Segoe UI Semibold",25),AutoSize=true,Location=new Point(30,24)});
        Controls.Add(new Label{Text="Tradutor em tempo real para Windows",ForeColor=Color.DimGray,AutoSize=true,Location=new Point(34,73)});
        Controls.Add(new Label{Text="Será instalado em:\nD:\\Refrigerator\\App\\Refrigerator.exe\n\nConfigurações, cache, modelos locais e logs também usam D:\\Refrigerator.",AutoSize=true,MaximumSize=new Size(520,0),Location=new Point(34,120)});
        desktop.Location=new Point(34,220);run.Location=new Point(34,250);Controls.Add(desktop);Controls.Add(run);
        bar.Location=new Point(34,290);Controls.Add(bar);status.Location=new Point(34,318);status.Text="Pronto para instalar.";Controls.Add(status);
        install.Location=new Point(410,350);install.Click+=async(_,_)=>await InstallAsync();Controls.Add(install);
        var cancel=new Button{Text="Cancelar",Width=110,Height=38,Location=new Point(288,350)};cancel.Click+=(_,_)=>Close();Controls.Add(cancel);
    }

    private async Task InstallAsync()
    {
        if(!Directory.Exists(@"D:\")){MessageBox.Show(this,"O disco D: não foi encontrado.","Refrigerator",MessageBoxButtons.OK,MessageBoxIcon.Warning);return;}
        install.Enabled=false;bar.Style=ProgressBarStyle.Marquee;
        try
        {
            string root=@"D:\Refrigerator",app=Path.Combine(root,"App"),target=Path.Combine(app,"Refrigerator.exe");
            Directory.CreateDirectory(app);Directory.CreateDirectory(Path.Combine(root,"Cache"));Directory.CreateDirectory(Path.Combine(root,"Logs"));Directory.CreateDirectory(Path.Combine(root,"Models"));Directory.CreateDirectory(Path.Combine(root,"Downloads"));
            status.Text="Extraindo Refrigerator para o D:...";await Task.Run(()=>Extract(target));
            if(desktop.Checked){status.Text="Criando atalhos...";CreateShortcut(target,Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));}
            bar.Style=ProgressBarStyle.Blocks;bar.Value=100;status.Text="✓ Instalado em D:\\Refrigerator";
            MessageBox.Show(this,"Instalação concluída.\n\nD:\\Refrigerator\\App\\Refrigerator.exe\n\nNa primeira abertura, configure Gemini/Groq ou Local AI e calibre o chat do Roblox.","Refrigerator instalado",MessageBoxButtons.OK,MessageBoxIcon.Information);
            if(run.Checked)Process.Start(new ProcessStartInfo(target){UseShellExecute=true});Close();
        }
        catch(Exception ex){bar.Style=ProgressBarStyle.Blocks;bar.Value=0;status.Text="Falha: "+ex.Message;install.Enabled=true;MessageBox.Show(this,ex.ToString(),"Falha",MessageBoxButtons.OK,MessageBoxIcon.Error);}
    }

    private static void Extract(string target)
    {
        using var input=Assembly.GetExecutingAssembly().GetManifestResourceStream("Refrigerator.Payload.exe")??throw new InvalidOperationException("Payload ausente.");
        string tmp=target+".new";using(var output=File.Create(tmp))input.CopyTo(output);
        if(File.Exists(target)){try{foreach(var p in Process.GetProcessesByName("Refrigerator"))p.Kill(true);Thread.Sleep(350);}catch{}File.Delete(target);}File.Move(tmp,target);
    }

    private static void CreateShortcut(string target,string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);string link=Path.Combine(folder,"Refrigerator.lnk");
            string ps="$w=New-Object -ComObject WScript.Shell;$s=$w.CreateShortcut('"+link.Replace("'","''")+"');$s.TargetPath='"+target.Replace("'","''")+"';$s.WorkingDirectory='"+Path.GetDirectoryName(target)!.Replace("'","''")+"';$s.Save()";
            using var p=Process.Start(new ProcessStartInfo("powershell.exe",$"-NoProfile -ExecutionPolicy Bypass -Command \"{ps.Replace("\"","\\\"")}\""){UseShellExecute=false,CreateNoWindow=true});p?.WaitForExit(5000);
        }
        catch{}
    }
}
