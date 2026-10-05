using System.ComponentModel;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BD2FiendHunter.Shared;
using BD2FiendHunter.Localization;
using System.Diagnostics;
using System.Windows.Controls;
using Control = BD2FiendHunter.Shared.Control;
namespace BD2FiendHunter.Desktop;
public partial class MainWindow:Window {
 public bool HostedAutomationEnabled => active || preparing;

 readonly Catalog words=new(Catalog.LoadLanguage());string statusKey="ready";JsonObject? lastState;bool ready,smokeMode;
 readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(500)};
 readonly SemaphoreSlim gate=new(1);Control control=new();bool active,closing,preparing,ownsControl;CancellationTokenSource? preparation;
 public MainWindow(bool smoke=false){smokeMode=smoke;InitializeComponent();ApplyLanguage();LanguageBox.SelectedIndex=words.Language=="zh-CN"?0:1;ready=true;timer.Tick+=async(_,_)=>await Pulse();if(!smoke)Loaded+=(_,_)=>timer.Start();Closing+=OnClosing;}
 void Status(string key){statusKey=key;StatusText.Text=words[key];}
 void ApplyLanguage(){foreach(var pair in words.Messages)Resources[pair.Key]=pair.Value;Title=words["title"];Status(statusKey);if(lastState!=null)Draw(lastState);else{PlayerText.Text=words["player"]+" —";BossText.Text=words["boss"]+" —";}}
 void LanguageChanged(object sender,SelectionChangedEventArgs e){if(!ready||LanguageBox.SelectedItem is not ComboBoxItem item)return;words.Select((string)item.Tag);ApplyLanguage();if(!smokeMode)try{words.SaveLanguage();}catch(Exception ex){RecordError(ex);}}
 static void RecordError(Exception ex){try{Directory.CreateDirectory(Client.Root);File.WriteAllText(Path.Combine(Client.Root,"desktop-error.txt"),DateTimeOffset.UtcNow+"\n"+ex);}catch{}}
 void Error(Exception ex){RecordError(ex);statusKey="error";StatusText.Text=words.Error(ex);}
 void Diagnostics(object sender,RoutedEventArgs e){try{Directory.CreateDirectory(Client.Root);Process.Start(new ProcessStartInfo("explorer.exe",Client.Root){UseShellExecute=true});}catch(Exception ex){Error(ex);}}
 void OpenSource(object sender,System.Windows.Navigation.RequestNavigateEventArgs e){Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri){UseShellExecute=true});e.Handled=true;}

 static bool Has(JsonObject? s,string type)=>s?["UIs"]?.AsArray().Any(x=>(string?)x?["Type"]==type)==true;
 async Task<JsonObject> WaitUi(string type,CancellationToken token){for(int i=0;i<120;i++){token.ThrowIfCancellationRequested();var s=Client.State();if(Client.Fresh(s)&&Has(s,type))return s!;await Task.Delay(250,token);}throw new Exception("timeout");}
 async void Start(object sender,RoutedEventArgs e){if(active||preparing||closing)return;preparing=true;StartButton.IsEnabled=false;Status("connecting");preparation=new();var token=preparation.Token;
  await gate.WaitAsync();try{
   await Task.Run(async()=>{await Client.Connect();ownsControl=true;token.ThrowIfCancellationRequested();await Client.Command("reset");var s=Client.State();
    if(Has(s,"ActionMainUI")){await Client.Command("click",new(){["Ui"]="ActionMainUI",["Field"]="_goBtnPlaySingle"});s=await WaitUi("ActionSelectUI",token);}
    if(Has(s,"ActionSelectUI")){await Client.Command("prepare",new(){["Character"]=1,["Boss"]=1,["Difficulty"]=3});token.ThrowIfCancellationRequested();control=new(){Enabled=true};Client.Heartbeat(control);await Client.Command("click",new(){["Ui"]="ActionSelectUI",["Field"]="_goPlaySingle"});}
    else if(Has(s,"ActionHUD")&&(string?)s?["ManagerState"]=="EcsPlaying"){if(s?["Player"]?["Id"]?.GetValue<int>()!=1||s?["Boss"]?["Id"]?.GetValue<int>()!=103)throw new Exception("unsupported");control=new(){Enabled=true};Client.Heartbeat(control);}
    else throw new Exception("entry");
   });token.ThrowIfCancellationRequested();active=true;Status("waiting");
  }catch(Exception ex){active=false;control.Enabled=false;try{await Task.Run(()=>Client.Heartbeat(control));}catch{}if(ex is OperationCanceledException)Status("paused");else Error(ex);}
  finally{preparing=false;StartButton.IsEnabled=!active;gate.Release();}
 }
 async Task Pulse(){if(closing||!await gate.WaitAsync(0))return;try{
  var s=await Task.Run(()=>Client.State());if(!Client.Fresh(s)){if(active){active=false;control.Enabled=false;Status("heartbeat");}return;}
  Draw(s!);if(!active)return;
  if(s!["LatchedStop"]?.GetValue<bool>()==true||(s["EndReplies"]?.GetValue<int>()??0)>0){active=false;control.Enabled=false;if((s["EndReplies"]?.GetValue<int>()??0)>0)Status((string?)s["Outcome"]=="victory"?"victory":"defeat");else{Status("error");RecordError(new Exception((string?)s["Error"]??(string?)s["Reason"]??"Stopped"));};}
  await Task.Run(()=>Client.Heartbeat(control));
 }catch(Exception ex){active=false;control.Enabled=false;Error(ex);}finally{StartButton.IsEnabled=!active&&!preparing;gate.Release();}}
 void Draw(JsonObject s){lastState=s;void Unit(string key,System.Windows.Controls.TextBlock label,System.Windows.Controls.ProgressBar bar,string title){var u=s[key];if(u==null){label.Text=title+" —";bar.Value=0;return;}double hp=u["Hp"]!.GetValue<double>(),max=u["MaxHp"]!.GetValue<double>();label.Text=$"{title} {hp:N0} / {max:N0}";bar.Value=max>0?hp/max:0;}Unit("Player",PlayerText,PlayerBar,words["player"]);Unit("Boss",BossText,BossBar,words["boss"]);if(active)StatusText.Text=words.Reason((string?)s["Reason"]??"");}
 async void Pause(object sender,RoutedEventArgs e){if(!ownsControl&&!preparing){Status("idle");return;}preparation?.Cancel();active=false;await gate.WaitAsync();try{control.Enabled=false;await Task.Run(()=>Client.Heartbeat(control));Status("paused");}catch(Exception ex){Error(ex);}finally{StartButton.IsEnabled=true;gate.Release();}}
 async void OnClosing(object? sender,CancelEventArgs e){if(closing)return;e.Cancel=true;closing=true;preparation?.Cancel();active=false;timer.Stop();IsEnabled=false;await gate.WaitAsync();try{if(ownsControl){control.Enabled=false;await Task.Run(()=>{try{Client.Heartbeat(control);}catch{}});}}finally{gate.Release();Close();}}
 public void Smoke(string folder){
  Directory.CreateDirectory(folder);var checks=new List<string>();
  void Check(bool v,string label){if(!v)throw new Exception(label);checks.Add(label);}
  foreach(var language in new[]{"zh-CN","en-US"})foreach(int width in new[]{490,540}){
   words.Select(language);ApplyLanguage();LanguageBox.SelectedIndex=language=="zh-CN"?0:1;Width=width;Height=550;UpdateLayout();
   Draw(JsonNode.Parse("{\"Player\":{\"Hp\":1435,\"MaxHp\":1800},\"Boss\":{\"Hp\":6000,\"MaxHp\":18000}}")!.AsObject());Status("running");UpdateLayout();
   Check(Title==words["title"],language+width+" title");Check(StartButton.Content?.ToString()==words["start"],language+width+" start");Check(PlayerText.Text.Contains(words["player"]),language+width+" HP");
   Check(!StatusText.Text.Contains("网络")&&!StatusText.Text.Contains("network"),language+width+" silent recovery");
   var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var f=File.Create(Path.Combine(folder,language+"-"+width+".png"));encoder.Save(f);
  }
  File.WriteAllText(Path.Combine(folder,"smoke.json"),System.Text.Json.JsonSerializer.Serialize(new{status="passed",gameCommands=0,checks}));
 }
}
