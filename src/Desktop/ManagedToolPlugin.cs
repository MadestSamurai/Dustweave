using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Dustweave.ToolPluginApi;

namespace Dustweave.Desktop;

/// <summary>Shared-runtime tool entry. Runs before normal host initialization;
/// no accounts, update recovery, host mutex or game connection are opened here.</summary>
internal static class ManagedToolPlugin
{
    public static int Run(string[] args)
    {
        string Value(string key,string fallback="") { int n=Array.IndexOf(args,key); return n>=0 && n+1<args.Length ? args[n+1] : fallback; }
        string probe=Value("--dustweave-probe"),session=Value("--dustweave-session");
        try
        {
            var info=DailyPlugin.Inspect(Path.GetFullPath(args[0]));
            if(!info.Available || info.ToolApiVersion!=2)throw new InvalidDataException("plugins."+info.State);
            if(info.Fingerprint!=args[1])throw new InvalidDataException("plugins.invalid_hash");
            var loader=new PluginLoadContext(info);
            var assembly=loader.LoadFromAssemblyPath(Path.Combine(info.Root,info.ToolAssembly));
            if(Activator.CreateInstance(assembly.GetType(info.ToolType,true)!) is not IToolPluginV2 plugin || plugin.UiApiVersion!=ToolPluginContext.ApiVersion)
                throw new InvalidDataException("plugins.incompatible");
            if(probe.Length>0)
            {
                plugin.Probe(info.Root);
                DailyJson.Write(probe,new {status="passed",toolApiVersion=2,uiApiVersion=ToolPluginContext.ApiVersion,realGameTouched=false});return 0;
            }
            if(session.Length==0 && Value("--smoke").Length==0)throw new InvalidDataException("plugins.tool_request_invalid");
            var app=new Application {ShutdownMode=ShutdownMode.OnMainWindowClose};
            foreach(string name in new[]{"Icons","SharedControls"})app.Resources.MergedDictionaries.Add(new ResourceDictionary {Source=new Uri("/Dustweave;component/"+name+".xaml",UriKind.Relative)});
            using var context=new Context(info.Root,session,Value("--language","zh-CN"),Value("--appearance",args.Contains("--dark")?"dark":"system"));
            var window=plugin.CreateWindow(context,args.Skip(2).ToArray());
            context.PrepareWindow(window);
            return app.Run(window);
        }
        catch(Exception error)
        {
            try
            {
                if(probe.Length>0)DailyJson.Write(probe,new {status="failed",error=error.ToString(),realGameTouched=false});
                else if(session.Length>0)DailyJson.Write(Path.Combine(session,"startup-error.json"),new {error=error.ToString()});
                else if(Value("--smoke").Length>0)File.WriteAllText(Value("--smoke")+".error.txt",error.ToString());
            }
            catch(Exception logging) when(logging is IOException or UnauthorizedAccessException) { }
            return 1;
        }
    }

    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly string tool;
        private readonly HashSet<string> declared;
        public PluginLoadContext(DailyPluginInfo info) : base("ToolPlugin",false)
        {
            tool=Path.GetDirectoryName(Path.Combine(info.Root,info.ToolAssembly))!;
            using var manifest=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(info.Root,"plugin.json")));
            declared=manifest.RootElement.GetProperty("files").EnumerateArray().Select(f=>Path.GetFullPath(Path.Combine(info.Root,f.GetProperty("path").GetString()!))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        protected override Assembly? Load(AssemblyName name)
        {
            // The UI contract and WPF/runtime types must have host identity.
            if(name.Name==typeof(IToolPluginV2).Assembly.GetName().Name)return typeof(IToolPluginV2).Assembly;
            string file=Path.Combine(tool,name.Name+".dll");
            if(!declared.Contains(file))return null;
            return LoadFromAssemblyPath(file);
        }
    }

    private sealed class Context : ToolPluginContext,IDisposable
    {
        private readonly DailyTheme theme;
        private readonly DispatcherTimer timer;
        private readonly string session;
        private DailyToolPresentation? current;
        public override string PackageDirectory {get;}
        public override string Language => DailyLanguage.Current.Code;
        public override string Appearance => theme.Preference.ToString().ToLowerInvariant();
        public override bool IsDark => theme.IsDark;
        public override event EventHandler? PresentationChanged;
        public Context(string package,string session,string language,string appearance)
        {
            PackageDirectory=package;this.session=session;
            // The theme reads at most this isolated session; it never writes to user settings.
            theme=new DailyTheme(session.Length>0?session:Path.Combine(package,".unused-ui-settings"));
            Apply(new(1,language,appearance));
            theme.PropertyChanged+=ThemeChanged;
            timer=new DispatcherTimer(TimeSpan.FromMilliseconds(500),DispatcherPriority.Background,(_,_)=>Refresh(),Application.Current.Dispatcher);
            Refresh();
        }
        private void ThemeChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs e)
        { if(e.PropertyName==nameof(DailyTheme.IsDark))PresentationChanged?.Invoke(this,EventArgs.Empty); }
        private void Apply(DailyToolPresentation value)
        {
            if(!value.Valid || value==current)return;
            current=value;DailyLanguage.Current.ApplyTransient(value.Language);
            theme.ApplyTransient(Enum.Parse<DailyAppearance>(value.Appearance,true));
            PresentationChanged?.Invoke(this,EventArgs.Empty);
        }
        private void Refresh()
        {
            if(session.Length==0)return;
            try
            {
                string file=Path.Combine(session,"appearance.json");
                if(File.Exists(file) && new FileInfo(file).Length<=4096 && DailyJson.TryRead<DailyToolPresentation>(file) is {} value)Apply(value);
            }
            catch(Exception error) when(error is IOException or UnauthorizedAccessException or JsonException) { }
        }
        public override void PrepareWindow(Window window)=>DailyDialogs.Prepare(window,toolWindow:true);
        public override bool Confirm(Window owner,Func<string> title,Func<string> message,Func<string> accept)
        {
            TextBlock body=null!;Button button=null!;
            var dialog=DailyDialogs.Message(owner,title(),message(),true,safeDefault:true,configure:(text,action)=>{body=text;button=action;});
            void RefreshDialog(object? sender,EventArgs args) {dialog.Title=title();body.Text=message();button.Content=accept();}
            RefreshDialog(null,EventArgs.Empty);PresentationChanged+=RefreshDialog;
            try {return DailyDialogs.ShowModal(dialog)==true;}
            finally {PresentationChanged-=RefreshDialog;}
        }
        public void Dispose(){timer.Stop();theme.PropertyChanged-=ThemeChanged;theme.Dispose();}
    }
}
