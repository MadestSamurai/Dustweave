using System.Windows;
using System.Windows.Controls;
namespace Dustweave.Desktop;

public sealed partial class DailyBargainWindow
{
    readonly ComboBox mode=new(){MinHeight=36,Margin=new(0,6,0,10)};
    readonly Button add=Button("practice.queue_add");
    readonly ListBox queue=new(){MinHeight=72,MaxHeight=170,Margin=new(0,8,0,8)};
    readonly List<Button> queueActions=[];
    readonly TextBlock queueEmpty=Text();
    List<DailyPracticeJob> jobs=[];
    DailyPracticeAchievement[] achievements=[];
    bool AchievementMode=>mode.SelectedIndex==1;
    DailyPracticeAchievement? Goal=>DailyPracticeAchievement.Highest(achievements,Kind);
    bool CanAdd=>characters.SelectedItem is DailyBargainCharacter && (!AchievementMode||Goal is{Known:true,Remaining:>0});
    UIElement BuildMode()
    {
        foreach(string key in new[]{"practice.mode_xp","practice.mode_achievement"}){
            var item=new ComboBoxItem();L.Bind(item,ContentControl.ContentProperty,key);mode.Items.Add(item);
        }
        L.Bind(mode,System.Windows.Automation.AutomationProperties.NameProperty,"practice.mode");
        mode.SelectedIndex=0;
        mode.SelectionChanged+=(_,_)=>{FilterCharacters();Render();};
        return mode;
    }
    UIElement BuildQueue()
    {
        if(allowGame)jobs=session.LoadQueue();
        var panel=new StackPanel();var heading=new DockPanel();
        DockPanel.SetDock(add,Dock.Right);heading.Children.Add(add);
        var title=Text(16);L.Text(title,"practice.queue_title");heading.Children.Add(title);panel.Children.Add(heading);
        L.Bind(queue,System.Windows.Automation.AutomationProperties.NameProperty,"practice.queue_title");L.Text(queueEmpty,"practice.queue_empty");queueEmpty.SetResourceReference(TextBlock.ForegroundProperty,"MutedInk");panel.Children.Add(queueEmpty);panel.Children.Add(queue);
        var actions=new WrapPanel();
        foreach(var (key,delta) in new[]{("practice.queue_up",-1),("practice.queue_down",1),("practice.queue_remove",0)}){
            var button=Button(key);button.Margin=new(0,0,8,0);queueActions.Add(button);actions.Children.Add(button);
            button.Click+=(_,_)=>EditSafely(()=>EditQueue(delta));
        }
        panel.Children.Add(actions);add.Click+=(_,_)=>EditSafely(AddSelected);queue.SelectionChanged+=(_,_)=>{foreach(var b in queueActions)b.IsEnabled=!busy&&queue.SelectedIndex>=0;};
        L.Bind(start,ContentControl.ContentProperty,"practice.queue_start");RefreshQueue();return Section(panel);
    }
    void PersistQueue(){if(allowGame)session.SaveQueue(jobs);}
    void EditSafely(Action edit){try{edit();}catch(Exception e){error=e;session.RecordFailure(e);Render();}}
    void AddSelected()
    {
        if(busy||!CanAdd||characters.SelectedItem is not DailyBargainCharacter c)return;
        int existing=jobs.FindIndex(j=>j.State!="completed"&&j.Kind==Kind&&j.Achievement==AchievementMode&&(AchievementMode||j.Character==c.Character));
        if(existing>=0){queue.SelectedIndex=existing;return;}
        jobs.Add(DailyPracticeJob.Create(Kind,c.Character,AchievementMode,c.Level));PersistQueue();RefreshQueue();queue.SelectedIndex=jobs.Count-1;Render();
    }
    DailyPracticeJob[] RefreshQueueLevels(DailyBargainCharacter[] current)
    {
        var next=jobs.ToList();
        var removed=DailyPracticeQueue.RefreshLevels(next,current,allCharacters);
        if(!next.SequenceEqual(jobs)){
            if(allowGame)session.SaveQueue(next);
            jobs=next;
        }
        return removed;
    }
    void ShowRefreshResult(DailyPracticeJob[] removed)
    {
        if(removed.Length==0){SetMessage(allCharacters.Length==0?"practice.none":"bargain.ready");return;}
        string names=string.Join(" / ",removed.Select(j=>allCharacters.FirstOrDefault(c=>c.Character==j.Character&&c.Kind==j.Kind)?.NameIn(L.Code)
            ??L.Get("practice.saved_character",j.Character)).Distinct());
        SetMessage("practice.queue_levels_changed",removed.Length,names);
    }
    void EditQueue(int delta)
    {
        int i=queue.SelectedIndex;if(busy||i<0||i>=jobs.Count)return;
        if(delta==0)jobs.RemoveAt(i);
        else{int next=i+delta;if(next<0||next>=jobs.Count)return;(jobs[i],jobs[next])=(jobs[next],jobs[i]);i=next;}
        PersistQueue();RefreshQueue();queue.SelectedIndex=Math.Min(i,jobs.Count-1);Render();
    }
    void RefreshQueue()
    {
        int selected=queue.SelectedIndex;queue.Items.Clear();
        foreach(var job in jobs){
            string talent=talentNames.FirstOrDefault(t=>t.Kind==job.Kind)?.Names.In(L.Code)??L.Get("practice.skill_number",job.Kind);
            string name=job.Achievement?L.Get("practice.mode_achievement"):allCharacters.FirstOrDefault(c=>c.Character==job.Character)?.NameIn(L.Code)??L.Get("practice.saved_character",job.Character);
            string state=job.State is "running" or "completed" or "blocked"?job.State:"pending";
            var row=Text();row.Margin=new(4,5,4,5);L.Text(row,"practice.queue_row",queue.Items.Count+1,talent,name,L.Get("practice.queue_"+state));
            if(job.Message.Length>0){row.ToolTip=L.Get(job.Message);if(job.State=="blocked")row.Text+="\n"+L.Get(job.Message);}queue.Items.Add(row);
        }
        queue.ToolTip=jobs.Count==0?L.Get("practice.queue_empty"):null;
        queueEmpty.Visibility=jobs.Count==0?Visibility.Visible:Visibility.Collapsed;
        queue.SelectedIndex=Math.Min(selected,jobs.Count-1);
    }
    void RenderAchievement(bool available)
    {
        L.Text(hint,"practice.achievement_hint");
        var goal=Goal;var c=characters.SelectedItem as DailyBargainCharacter;
        if(goal is{Known:true}){
            L.Text(experience,"practice.achievement_progress",goal.Current,goal.Target);
            progress.Value=Math.Min(100,100d*goal.Current/goal.Target);
            if(c!=null){L.Text(estimate,"practice.achievement_estimate",goal.Remaining,goal.Remaining*c.Cost);L.Text(stock,"bargain.stock",potions,c.Cost);}
            else{estimate.Text="—";stock.Text="";}
        }else{experience.Text="";estimate.Text="—";stock.Text="";progress.Value=0;}
        start.IsEnabled=available&&(jobs.Any(j=>j.State!="completed")||(CanAdd&&c!.Cost<=potions));
        if(error is DailyBargainFault fault)L.Text(status,fault.Code,fault.Values);
        else if(error!=null)DailyUiText.Error(status,error);
        else if(busy||jobs.Count>0||message=="practice.queue_levels_changed")L.Text(status,message,messageValues);
        else L.Text(status,goal==null?"practice.achievement_missing":!goal.Known?"practice.achievement_unknown":goal.Remaining==0?"practice.achievement_complete":c==null?"practice.none":potions<c.Cost?"practice.queue_insufficient":"practice.achievement_hint");
    }
    void RenderQueueBudget()
    {
        stock.SetResourceReference(TextBlock.ForegroundProperty,"Ink");
        if(jobs.Count==0)return;
        var budget=DailyPracticeQueue.Estimate(jobs,new(allCharacters,potions,achievements));
        L.Text(costLabel,"practice.queue_cost_label");
        L.Text(estimate,budget.UnknownItems>0?"practice.queue_cost_partial":"practice.queue_cost",budget.Potions,budget.Uses,budget.UnknownItems);
        long shortage=budget.Shortage(potions);
        L.Text(stock,shortage>0?"practice.queue_stock_short":"practice.queue_stock",potions,shortage);
        if(shortage>0)stock.SetResourceReference(TextBlock.ForegroundProperty,"Warning");
    }
    internal void PreviewQueueForSmoke()
    {
        if(allowGame)throw new InvalidOperationException("Preview cannot access the game");
        PreviewFieldForSmoke();AddSelected();characters.SelectedIndex=1;AddSelected();AddSelected();
        if(jobs.Count!=2)throw new Exception("Queue lost character identities or duplicate prevention");
        if(estimate.Text!=L.Get("practice.queue_cost",135,27,0)||costLabel.Text!=L.Get("practice.queue_cost_label"))
            throw new Exception("Queue total still shows only the selected costume");
        queue.SelectedIndex=1;EditQueue(-1);if(jobs[0].Character!=11)throw new Exception("Queue reorder failed");
        EditQueue(0);if(jobs.Count!=1||jobs[0].Character!=10)throw new Exception("Queue remove failed");
        achievements=[new(12,88,0,500,499,true),new(12,88,0,4500,499,true)];
        allCharacters=allCharacters.Select(c=>c.Character==10?c with{Cost=8,Experience=c.Target}:c).ToArray();
        mode.SelectedIndex=1;AddSelected();
        if(Goal?.Target!=4500||(characters.SelectedItem as DailyBargainCharacter)?.Character!=11||characters.IsEnabled||jobs.Count!=2||jobs[1].Character!=0)
            throw new Exception("Achievement mode did not use final tier and automatic cheapest selection");
        skills.SelectedIndex=3;if(CanAdd||status.Text.Contains("["))throw new Exception("Unsupported achievement has no explanation");
        skills.SelectedIndex=4;Render();
        if(estimate.Text!=L.Get("practice.queue_cost",20005,4001,0)||stock.Text!=L.Get("practice.queue_stock_short",1000,19005))
            throw new Exception("Mixed queue total or inventory shortfall is incorrect");
        session.SaveQueue(jobs.Select((j,i)=>j with{State=i==0?"completed":"running"}));
        var loaded=session.LoadQueue();
        if(loaded.Count!=2||loaded[0].State!="completed"||loaded[1].State!="pending"||!loaded[1].Achievement||loaded[0].Level!=allCharacters[0].Level||loaded[1].Level!=null)
            throw new Exception("Per-account queue restart lost completed work or did not reset in-flight status");
    }
    internal void PreviewLevelRefreshForSmoke()
    {
        if(allowGame)throw new InvalidOperationException("Preview cannot access the game");
        jobs=session.LoadQueue();
        var current=allCharacters.Select(c=>c.Character==10?c with{Level=c.Level+1,Experience=0,Target=100}:c).ToArray();
        var removed=RefreshQueueLevels(current);allCharacters=current;FilterCharacters();RefreshQueue();ShowRefreshResult(removed);
        if(removed.Length!=1||jobs.Count!=1||!jobs[0].Achievement||estimate.Text!=L.Get("practice.queue_cost",20005,4001,0))
            throw new Exception("Refresh did not remove upgraded XP entry while preserving achievement and remaining budget");
        session.SaveQueue(jobs);
        if(session.LoadQueue().Any(j=>!j.Achievement))throw new Exception("Removed costume returned after reopening");
        mode.SelectedIndex=0;var selected=allCharacters.First(c=>c.Character==11);
        jobs=[DailyPracticeJob.Create(selected.Kind,selected.Character,false,selected.Level)];
        current=allCharacters.Select(c=>c.Character==11?c with{Level=c.Level+1,Experience=c.Target}:c).ToArray();
        removed=RefreshQueueLevels(current);allCharacters=current;FilterCharacters(11);RefreshQueue();ShowRefreshResult(removed);
        if(jobs.Count!=0||queue.Items.Count!=0||queueEmpty.Visibility!=Visibility.Visible||status.Text!=L.Get("practice.queue_levels_changed",1,selected.NameIn(L.Code)))
            throw new Exception("Empty queue hides level-change removal explanation");
    }
}
