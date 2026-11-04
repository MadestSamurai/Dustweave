using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Dustweave.Desktop;

// Isolated UI fixtures exercise live window synchronization without constructing a game client.
internal static class HostedToolLocaleUiProbe
{
    internal static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var checks=new List<string>();
        void Check(bool ok,string message){if(!ok)throw new Exception(message);checks.Add(message);}
        string root=Path.Combine(output,"isolated");
        DailyLanguage.Current.Initialize(root);
        DailyLanguage.Current.Select("zh-CN");
        using var scope=HostedToolLocale.Begin("zh-CN");
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        var first=new ToolWindow();app.MainWindow=first;first.Show();
        var busy=new ToolWindow();busy.Show();busy.Selector.IsEnabled=false;
        var popup=new PopupWindow();popup.Show();
        app.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        using var bridge=new HostedToolLocaleBridge(app);
        try
        {
            Check(first.Selector.Visibility==Visibility.Collapsed && busy.Selector.Visibility==Visibility.Collapsed,"Child selectors are hidden, including busy windows");
            foreach(string code in new[]{"en-US","zh-TW","zh-CN","en-US"})
            {
                // Same file update used by the main window, observed in a separate tool process.
                DailyJson.Write(Path.Combine(root,"language.json"),new DailyLanguage.LanguageSetting(code));
                bridge.Refresh();
                Check(first.CurrentCode==code&&popup.CurrentCode==code,"Existing tool and popup follow "+code);
                Check(first.Draft.Text=="Account-甲 / 5035"&&first.Draft.SelectionLength==3&&first.Choice.IsChecked==true,"Draft and selection survive "+code);
                Check((string)app.Resources["SuiteRefresh"]==DailyLanguage.Current.Get("tools.refresh_inventory"),"Shared refresh caption follows "+code);
                Check(first.Title=="Tool "+code,"Native window title follows "+code);
            }
            Check(busy.CurrentCode=="zh-CN","Busy equipment view defers refresh");
            busy.Selector.IsEnabled=true;bridge.Refresh();
            Check(busy.CurrentCode=="en-US","Deferred view catches up after becoming ready");
            var later=new PopupWindow();later.Show();app.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);bridge.Refresh();
            Check(later.CurrentCode=="en-US","A newly opened popup inherits the current language");later.Close();bridge.Refresh();
            File.WriteAllText(Path.Combine(root,"language.json"),"{incomplete");bridge.Refresh();
            Check(first.CurrentCode=="en-US","Unreadable preference does not reset the interface");
            DailyJson.Write(Path.Combine(root,"language.json"),new DailyLanguage.LanguageSetting("zh-TW"));bridge.Refresh();
            Check(first.CurrentCode=="zh-TW"&&busy.CurrentCode=="zh-TW","Language refresh recovers on the next valid preference");
            Check(first.Selector.Items.Count==3,"Repeated switches do not duplicate language choices");
            Check(first.LanguageLabel.Visibility==Visibility.Collapsed,"Redundant language caption is removed");
            foreach(var tool in DailyToolCatalog.All)
            {
                var notice=Assembly.Load(tool.AssemblyName).GetType("BD2.Distribution.DistributionNotice");
                if(notice==null)continue;
                Exception? failure=null;
                app.Dispatcher.BeginInvoke(new Action(()=>
                {
                    Window? dialog=null;
                    try
                    {
                        dialog=app.Windows.Cast<Window>().Single(w=>w.GetType().Name=="NoticeWindow");
                        bridge.Refresh();
                        Check(dialog.Title=="來源與說明",tool.Id+": actual source dialog inherits Traditional");
                        DailyLanguage.Current.Select("en-US");bridge.Refresh();
                        Check(dialog.Title=="About & source",tool.Id+": open source dialog switches to English");
                        DailyLanguage.Current.Select("zh-CN");bridge.Refresh();
                        Check(dialog.Title=="来源与说明",tool.Id+": open source dialog switches back to Chinese");
                    }
                    catch(Exception error){failure=error;}
                    finally{dialog?.Close();}
                }),DispatcherPriority.ApplicationIdle);
                notice.GetMethod("Show",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[first,false]);
                if(failure!=null)throw failure;
                DailyLanguage.Current.Select("zh-TW");bridge.Refresh();
            }
            CheckReattachedText(app,Check);
            DailyJson.Write(Path.Combine(output,"result.json"),new{status="passed",count=checks.Count,checks,realGameTouched=false,gameCommands=0});
        }
        finally{foreach(Window window in app.Windows.Cast<Window>().ToArray())window.Close();app.Shutdown();}
    }
    private static void CheckReattachedText(Application app,Action<bool,string> check)
    {
        foreach(string name in new[]{"BD2Fishing","BD2Sichuan","BD2InfiniteGacha","BD2ApostleDefense"})
        {
            var label=new TextBlock{Text="连接游戏"};var body=new StackPanel();body.Children.Add(label);
            var window=new Window{Content=body,Width=300,Height=180,Left=-12000,ShowInTaskbar=false,ShowActivated=false};
            var type=Assembly.Load(name).GetType(name+".Desktop.WindowLanguage",true)!;
            using var adapter=(IDisposable)Activator.CreateInstance(type,window,"en-US")!;
            void Select(string code)=>type.GetMethod("Select")!.Invoke(adapter,[code]);
            void Drain()=>app.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
            window.Show();Drain();
            try
            {
                check(label.Text=="Connect game",name+": real text adapter translates before reparenting");
                // Preparation wraps the original content; unloaded controls must keep their source text.
                window.Content=null;Drain();window.Content=body;Drain();
                Select("zh-TW");check(label.Text=="連接遊戲",name+": reattached English text switches to Traditional");
                Select("zh-CN");check(label.Text=="连接游戏",name+": original source survives Traditional round trip");
                Select("en-US");check(label.Text=="Connect game",name+": repeated switch stays reversible");
                window.Content=null;Drain();label.Text="Account-甲 / 5035";window.Content=body;Drain();
                Select("zh-CN");check(label.Text=="Account-甲 / 5035",name+": new text assigned while detached is preserved");
            }
            finally{window.Close();}
        }
    }
    private sealed class ToolWindow:Window
    {
        internal readonly ComboBox Selector=new(){Name="LanguageBox"};
        internal readonly TextBlock LanguageLabel=new(){Text="语言 / Language"};
        internal readonly TextBox Draft=new(){Text="Account-甲 / 5035"};
        internal readonly CheckBox Choice=new(){IsChecked=true};
        internal string CurrentCode="zh-CN";
        internal ToolWindow()
        {
            Width=300;Height=220;Opacity=0;ShowInTaskbar=false;
            NameScope.SetNameScope(this,new NameScope());RegisterName("LanguageBox",Selector);
            Selector.Items.Add(new ComboBoxItem{Content="简体中文",Tag="zh-CN"});
            Selector.Items.Add(new ComboBoxItem{Content="English",Tag="en-US"});Selector.SelectedIndex=0;
            Selector.SelectionChanged+=(_,_)=>{CurrentCode=(string)((ComboBoxItem)Selector.SelectedItem).Tag;Title="Tool "+CurrentCode;};
            var panel=new StackPanel();panel.Children.Add(LanguageLabel);panel.Children.Add(Selector);panel.Children.Add(Draft);panel.Children.Add(Choice);Content=panel;
            Draft.Select(0,3);
        }
    }
    private sealed class PopupWindow:Window
    {
        internal string CurrentCode="zh-CN";
        internal PopupWindow(){Width=100;Height=100;Opacity=0;ShowInTaskbar=false;}
        public void ApplyHostedLanguage(string code)=>CurrentCode=code;
    }
}



