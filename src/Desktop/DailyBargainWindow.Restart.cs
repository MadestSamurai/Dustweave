using System.Windows;
using System.Windows.Controls;
namespace Dustweave.Desktop;

public sealed partial class DailyBargainWindow
{
    readonly ComboBox restartInterval=new(){MinHeight=36,MaxWidth=260,HorizontalAlignment=HorizontalAlignment.Left};
    UIElement BuildRestart()
    {
        var panel=new StackPanel();var label=Text();L.Text(label,"practice.restart_interval");panel.Children.Add(label);
        L.Bind(restartInterval,System.Windows.Automation.AutomationProperties.NameProperty,"practice.restart_interval");
        foreach(int minutes in DailyPracticeRestartClock.Choices)
        {
            var item=new ComboBoxItem{Tag=minutes};
            if(minutes==0)L.Bind(item,ContentControl.ContentProperty,"practice.restart_off");
            else L.Bind(item,ContentControl.ContentProperty,()=>L.Get("practice.restart_minutes",minutes));
            restartInterval.Items.Add(item);
        }
        if(allowGame)session.LoadSettings();
        restartInterval.SelectedItem=restartInterval.Items.Cast<ComboBoxItem>().Single(i=>(int)i.Tag==session.RestartMinutes);
        restartInterval.SelectionChanged+=(_,_)=>EditSafely(()=>{if(allowGame&&restartInterval.SelectedItem is ComboBoxItem{Tag:int minutes})session.RestartMinutes=minutes;});
        panel.Children.Add(restartInterval);
        var explanation=Text();L.Text(explanation,"practice.restart_hint");explanation.SetResourceReference(TextBlock.ForegroundProperty,"MutedInk");panel.Children.Add(explanation);
        return Section(panel);
    }
    internal void CheckRestartForSmoke()
    {
        if(allowGame||restartInterval.Items.Count!=5||(restartInterval.SelectedItem as ComboBoxItem)?.Tag is not 0)
            throw new InvalidOperationException("Restart must be opt-in with the supported intervals");
        restartInterval.SelectedIndex=2;
        if((restartInterval.SelectedItem as ComboBoxItem)?.Content as string!=L.Get("practice.restart_minutes",30))
            throw new InvalidOperationException("Restart interval did not localize");
        busy=true;Render();
        if(!restartInterval.IsEnabled)throw new InvalidOperationException("Restart interval cannot be disabled while running");
        busy=false;Render();restartInterval.SelectedIndex=0;restartInterval.BringIntoView();
    }
}
