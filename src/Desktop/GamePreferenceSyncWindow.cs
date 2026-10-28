using System.Windows;
using System.Windows.Controls;

namespace Dustweave.Desktop;

internal sealed class GamePreferenceSyncWindow : Window
{
    private static DailyLanguage L=>DailyLanguage.Current;
    private readonly RadioButton toBox=new(){Name="GamePreferencesToSandbox",GroupName="direction",IsChecked=true};
    private readonly RadioButton toHost=new(){Name="GamePreferencesToComputer",GroupName="direction"};
    private readonly CheckBox personal=new(){Name="GamePreferencesPersonal"},presentation=new(){Name="GamePreferencesPresentation",IsChecked=true},gameplay=new(){Name="GamePreferencesGameplay"};
    private readonly Button copy=new(){Name="GamePreferencesCopy"},close=new(){Name="GamePreferencesClose",IsCancel=true};
    private readonly TextBlock consequence=new(){TextWrapping=TextWrapping.Wrap,Margin=new(0,12,0,20)};
    private readonly TextBlock feedback=new(){Name="GamePreferencesFeedback",TextWrapping=TextWrapping.Wrap,Margin=new(0,16,0,0)};
    private readonly ComboBox accounts=new(){Name="GamePreferencesAccount",DisplayMemberPath="Name",Margin=new(0,0,0,16)};
    private readonly TextBlock identityHelp=Label("sandbox.preferences.identity_help");
    private DailyAccount selectedAccount;
    private readonly Func<GamePreferenceDirection,GamePreferenceParts,Task<GamePreferenceTransferResult>> transfer;
    private bool working;
    internal bool Completed { get; private set; }
    internal GamePreferenceSyncWindow(Window owner,DailyAccount account,bool firstUse=false,bool? knownPlayer=null,
        Func<GamePreferenceDirection,GamePreferenceParts,Task<GamePreferenceTransferResult>>? transfer=null,DailyAccount[]? choices=null)
    {
        Owner=owner;Width=640;MinWidth=480;SizeToContent=SizeToContent.Height;MaxHeight=Math.Max(440,SystemParameters.WorkArea.Height-40);
        WindowStartupLocation=WindowStartupLocation.CenterOwner;L.Bind(this,TitleProperty,"sandbox.preferences.title");
        selectedAccount=account;
        this.transfer=transfer??((direction,parts)=>Task.Run(()=>DailySandboxPreferenceTransfer.CopyAsync(selectedAccount,direction,parts,Environment.ProcessPath!,CancellationToken.None)));
        bool known=knownPlayer??DailySandboxPreferenceTransfer.HasKnownPlayer(account.AccountKey);
        personal.IsChecked=personal.IsEnabled=known;
        var body=new StackPanel{Margin=new(24,8,24,24)};
        if(choices is{Length:>1}) {
            accounts.ItemsSource=choices;accounts.SelectedItem=account;body.Children.Add(accounts);
            accounts.SelectionChanged+=(_,_)=>{
                if(accounts.SelectedItem is not DailyAccount value||working)return;
                selectedAccount=value;bool available=knownPlayer??DailySandboxPreferenceTransfer.HasKnownPlayer(value.AccountKey);
                personal.IsChecked=personal.IsEnabled=available;identityHelp.Visibility=available?Visibility.Collapsed:Visibility.Visible;
                feedback.Text="";Update();
            };
        } else body.Children.Add(new TextBlock{Text=account.Name,FontSize=22,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,10)});
        AddText(body,firstUse?"sandbox.preferences.first":"sandbox.preferences.manual",0,18);
        AddText(body,"sandbox.preferences.direction",0,10,FontWeights.SemiBold);
        foreach(var pair in new[]{(toBox,"sandbox.preferences.to_box"),(toHost,"sandbox.preferences.to_host")}) {
            pair.Item1.SetResourceReference(ForegroundProperty,"Ink");
            pair.Item1.Content=Label(pair.Item2);pair.Item1.Margin=new(0,0,0,10);body.Children.Add(pair.Item1);
            pair.Item1.Checked+=(_,_)=>Update();
        }
        consequence.SetResourceReference(TextBlock.ForegroundProperty,"MutedInk");body.Children.Add(consequence);
        AddText(body,"sandbox.preferences.content",0,12,FontWeights.SemiBold);
        foreach(var pair in new[]{(personal,"sandbox.preferences.personal"),(presentation,"sandbox.preferences.presentation"),(gameplay,"sandbox.preferences.gameplay")}) {
            pair.Item1.Content=Label(pair.Item2);pair.Item1.Margin=new(0,0,0,12);body.Children.Add(pair.Item1);
            pair.Item1.Checked+=(_,_)=>Update();pair.Item1.Unchecked+=(_,_)=>Update();
        }
        identityHelp.Margin=new(0,0,0,12);identityHelp.Visibility=known?Visibility.Collapsed:Visibility.Visible;body.Children.Add(identityHelp);
        AddText(body,"sandbox.preferences.scope",4,0);
        body.Children.Add(feedback);
        var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new(0,22,0,0)};
        L.Bind(close,ContentControl.ContentProperty,firstUse?"sandbox.preferences.skip":"window.close");close.Margin=new(0,0,10,0);close.Click+=(_,_)=>Close();actions.Children.Add(close);
        copy.Style=(Style)Application.Current.FindResource("PrimaryButton");copy.Click+=async(_,_)=>await CopyAsync();actions.Children.Add(copy);body.Children.Add(actions);
        Content=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        Closing+=(_,e)=>{if(working)e.Cancel=true;};
        Update();DailyDialogs.Prepare(this);
    }
    private static TextBlock Label(string key){var label=new TextBlock{TextWrapping=TextWrapping.Wrap};L.Text(label,key);return label;}
    private static void AddText(Panel panel,string key,double top,double bottom,FontWeight? weight=null)
    {
        var text=Label(key);text.Margin=new(0,top,0,bottom);text.FontWeight=weight??FontWeights.Normal;panel.Children.Add(text);
    }
    private GamePreferenceParts Parts=>(personal.IsChecked==true?GamePreferenceParts.Personal:0)|
        (presentation.IsChecked==true?GamePreferenceParts.Presentation:0)|(gameplay.IsChecked==true?GamePreferenceParts.Gameplay:0);
    private void Update()
    {
        L.Text(consequence,toHost.IsChecked==true?"sandbox.preferences.overwrite_host":"sandbox.preferences.overwrite_box");
        L.Bind(copy,ContentControl.ContentProperty,working?"sandbox.preferences.working":toHost.IsChecked==true?"sandbox.preferences.copy_to_host":"sandbox.preferences.copy_to_box");
        copy.IsEnabled=!working&&Parts!=0;close.IsEnabled=!working;accounts.IsEnabled=!working;
    }
    private async Task CopyAsync()
    {
        if(working||Parts==0)return;
        var parts=Parts;var direction=toHost.IsChecked==true?GamePreferenceDirection.ToComputer:GamePreferenceDirection.ToSandbox;
        working=true;bool available=personal.IsEnabled;personal.IsEnabled=presentation.IsEnabled=gameplay.IsEnabled=toHost.IsEnabled=toBox.IsEnabled=false;
        feedback.SetResourceReference(TextBlock.ForegroundProperty,"MutedInk");L.Text(feedback,"sandbox.preferences.working");Update();
        try {
            var result=await transfer(direction,parts);Completed=true;
            L.Text(feedback,"sandbox.preferences.completed",result.Tables,result.Settings);L.Bind(close,ContentControl.ContentProperty,"window.close");
        } catch(Exception error){feedback.SetResourceReference(TextBlock.ForegroundProperty,"Error");feedback.Text=DailyUserText.Error(error,L.Translate);}
        finally {working=false;personal.IsEnabled=available;presentation.IsEnabled=gameplay.IsEnabled=toHost.IsEnabled=toBox.IsEnabled=true;Update();}
    }
}
