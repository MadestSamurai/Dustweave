using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Dustweave.Desktop;

public partial class MainWindow
{
    private async Task CheckSoftwareSettingsForSmoke()
    {
        if(smoke==null || host is not DemoEnvironment demo)throw new InvalidOperationException("Isolated fixture required");
        int calls=demo.Calls.Count; var original=DailyDesktopBehavior.Read(root);int language=LanguageSelector.SelectedIndex,appearance=ThemeSelector.SelectedIndex;
        try
        {
            WorkspaceTabs.SelectedItem=SoftwareSettingsTab;
            if(windowsStartup.Enabled || StartWithWindows.IsChecked==true)throw new Exception("Windows startup default is not off");
            StartWithWindows.IsChecked=true;StartWithWindows_Click(this,new RoutedEventArgs());
            if(!windowsStartup.Enabled || DailyDesktopBehavior.Read(root).RunOnOpen!=original.RunOnOpen)throw new Exception("Windows startup changes daily queue behavior");
            StartWithWindows.IsChecked=false;StartWithWindows_Click(this,new RoutedEventArgs());
            if(windowsStartup.Enabled)throw new Exception("Windows startup cannot be disabled");
            foreach(var spec in new[]{(1,"tray"),(2,"exit"),(0,"ask")})
            {CloseActionChoice.SelectedIndex=spec.Item1;if(DailyDesktopBehavior.Read(root).CloseAction!=spec.Item2)throw new Exception("Close behavior did not persist immediately");}
            SetBusy(true);
            if(StartWithWindows.IsEnabled || CloseActionChoice.IsEnabled || ConnectionGuideButton.IsEnabled || softwareGamePath.CanChoose)throw new Exception("Shared preferences editable while queue busy");
            SetBusy(false);
            // A corrupt preference must not prevent startup or make the Close button unusable.
            string behaviorPath = Path.Combine(root, "desktop-behavior.json");
            var behaviorBytes = File.ReadAllBytes(behaviorPath);
            try
            {
                File.WriteAllText(behaviorPath, "{broken");
                InitializeDesktopBehavior();
                RefreshCloseAction();
                UpdateSoftwareSettingsAvailability();
                if (RunOnOpen.IsChecked == true || RunOnOpen.IsEnabled || CloseActionChoice.IsEnabled || SoftwareSettingsStatus.Visibility != Visibility.Visible)
                    throw new Exception("Unreadable startup preferences still authorize execution or hide the error");
                if (File.ReadAllText(behaviorPath) != "{broken") throw new Exception("Unreadable preferences were overwritten");
                Capture("software-settings-error");
            }
            finally { File.WriteAllBytes(behaviorPath, behaviorBytes); RefreshCloseAction(); UpdateSoftwareSettingsAvailability(); SoftwareSettingsStatus.Visibility = Visibility.Collapsed; }
            WorkspaceTabs.SelectedItem = SettingsTab;
            preferencesPanel.CheckHiddenFieldValidationForSmoke();
            DailyRunPanel.CheckEmptyAccountForSmoke(root);
            foreach(int lang in new[]{0,1,2})foreach(int themeIndex in new[]{1,2})
            {
                LanguageSelector.SelectedIndex=lang;ThemeSelector.SelectedIndex=themeIndex;Width=920;Height=650;
                WorkspaceTabs.SelectedItem=SoftwareSettingsTab;SoftwareSettingsScroll.ScrollToTop();
                await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);Capture($"software-top-{lang}-{themeIndex}");
                SoftwareSettingsScroll.ScrollToBottom();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);Capture($"software-bottom-{lang}-{themeIndex}");
                WorkspaceTabs.SelectedItem=SettingsTab;preferencesPanel.CheckCategorySearchForSmoke();
                foreach(string categoryKey in new[]{"daily","battle","weekly","resources"})
                {preferencesPanel.ReviewCategoryForSmoke(categoryKey);await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);Capture($"settings-{categoryKey}-{lang}-{themeIndex}");}
                foreach (bool tower in new[] { false, true })
                { preferencesPanel.ShowNativeBattleSettingsForSmoke(tower); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); Capture($"native-battle-{tower}-{lang}-{themeIndex}"); }
                WorkspaceTabs.SelectedItem=PluginsTab;await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);Capture($"plugins-empty-{lang}-{themeIndex}");
                WorkspaceTabs.SelectedItem=UpdatesTab;await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);Capture($"updates-{lang}-{themeIndex}");
            }
            if(calls!=demo.Calls.Count)throw new Exception("Settings review sent a game command");
            DailyJson.Write(Path.Combine(smoke,"software-settings.json"),new{status="passed",fakeStartupRegistry=true,closeActions=3,languages=3,themes=2,realGameTouched=false});
        }
        finally {original.Save(root);RefreshCloseAction();preferencesPanel.ReviewCategoryForSmoke("daily");LanguageSelector.SelectedIndex=language;ThemeSelector.SelectedIndex=appearance;Width=1180;Height=900;ActionNotice.Visibility=Visibility.Collapsed;}
    }
}
