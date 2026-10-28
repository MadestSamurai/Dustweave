using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Dustweave.Desktop;

public partial class MainWindow
{
    private async Task CheckGamePreferencesForSmoke()
    {
        var account=new DailyAccount(1,"测试账号 · Test account",new string('a',64),"***123",true,false,"");int calls=0,checks=0;
        void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);checks++;}
        foreach(string language in DailyLanguage.Codes)foreach(int theme in new[]{1,2}) {
            L.Select(language);ThemeSelector.SelectedIndex=theme;
            var pending=new TaskCompletionSource<GamePreferenceTransferResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var window=new GamePreferenceSyncWindow(this,account,true,true,(direction,parts)=>{
                Check(direction==GamePreferenceDirection.ToComputer,"Wrong copy direction");
                Check(parts==(GamePreferenceParts.Personal|GamePreferenceParts.Presentation),"Unselected gameplay settings were copied");calls++;return pending.Task;
            });
            window.Show();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            var controls=Descendants(window).OfType<Control>().Where(c=>c.Name.StartsWith("GamePreferences")).ToDictionary(c=>c.Name);
            Check(calls==0,"Opening the dialog must not copy any settings");
            var host=(RadioButton)controls["GamePreferencesToComputer"];host.IsChecked=true;
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            var button=(Button)controls["GamePreferencesCopy"];
            Check(button.Content.ToString()==L.Get("sandbox.preferences.copy_to_host"),"Copy button must describe the destination");
            Check(host.Foreground==Application.Current.FindResource("Ink"),"Direction text must use the current theme's ink color");
            Check(Descendants(window).OfType<TextBlock>().All(t=>!t.Text.Contains("missing UI text",StringComparison.OrdinalIgnoreCase)),"Missing translated text");
            CaptureSandboxDialog(window,"game-preferences-"+language+"-"+theme);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(calls==1&&!button.IsEnabled,"Copy must run once and disable duplicate submission");
            window.Close();Check(window.IsVisible,"Closing during commit must preserve the receipt");
            pending.SetResult(new(21,12,"fixture"));await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            Check(window.Completed&&button.IsEnabled,"Completed copy must restore controls");window.Close();calls=0;
            var unknown=new GamePreferenceSyncWindow(this,account,true,false,(_,_)=>throw new Exception("Unexpected copy")){Width=480};
            unknown.Show();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            Check(!Descendants(unknown).OfType<CheckBox>().Single(x=>x.Name=="GamePreferencesPersonal").IsEnabled,"Unknown player must not copy account data");
            Check(unknown.ActualHeight<=unknown.MaxHeight,"Compact dialog must remain on screen");
            CaptureSandboxDialog(unknown,"game-preferences-compact-"+language+"-"+theme);unknown.Close();
            var other=account with{Name="Second account",AccountKey=new string('b',64)};
            var batch=new GamePreferenceSyncWindow(this,account,true,true,(_,_)=>throw new Exception("Unexpected copy"),[account,other]);
            batch.Show();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            var chooser=Descendants(batch).OfType<ComboBox>().Single(x=>x.Name=="GamePreferencesAccount");chooser.SelectedItem=other;
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            Check(chooser.Items.Count==2&&calls==0,"A first parallel launch uses one selectable-account dialog with no automatic copying");
            CaptureSandboxDialog(batch,"game-preferences-batch-"+language+"-"+theme);batch.Close();
        }
        DailyJson.Write(System.IO.Path.Combine(smoke!,"game-preferences.json"),new{status="passed",checks,realGameTouched=false});
    }
}
