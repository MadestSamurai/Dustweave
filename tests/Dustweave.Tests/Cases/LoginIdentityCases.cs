using Dustweave;
using Dustweave.Desktop;
using System.Text.Json;
internal static class LoginIdentityCases
{
    internal static async Task Run(string root, List<string> cases)
    {
        void Check(bool ok,string name){if(!ok)throw new Exception(name);cases.Add("login identity: "+name);}
        Check(DailyDesktopLaunch.NeedsRelay([]),"ordinary GUI launch relays before registry access");
        Check(DailyDesktopLaunch.NeedsRelay(["--inspect-account",new string('a',64)]),"explicit login check uses desktop context");
        Check(!DailyDesktopLaunch.NeedsRelay([DailyDesktopLaunch.ChildSwitch]),"desktop child does not loop");
        Check(DailyDesktopLaunch.Normalize(DailyDesktopLaunch.ChildArguments([])).Length==0,"normal window never acquires an automatic task");
        Check(DailyDesktopLaunch.Normalize(DailyDesktopLaunch.ChildArguments(["--inspect-account","key"])).SequenceEqual(new[]{"--inspect-account","key"}),"explicit target survives relay");
        foreach(var mode in new[]{"--smoke","--utility","--check-suite","--daily-connect-elevated"}) Check(!DailyDesktopLaunch.NeedsRelay([mode,"fixture"]),"background entry remains unchanged "+mode);
        Check(DailyDesktopLaunch.Quote("a b")=="\"a b\"","desktop argument quoting preserves spaces");
        foreach(bool title in new[]{true,false})
        {
            string path=Path.Combine(root,"login-store-"+title);var env=new DemoEnvironment(path){TitleVisible=title};
            var target=env.Accounts[0];var actual=env.Accounts[1];env.ForcedKey=actual.AccountKey;
            var coordinator=new DailyCoordinator(env,env,path,new DailyOptions{PollInterval=TimeSpan.FromMilliseconds(1),LoginTimeout=TimeSpan.FromSeconds(1)});
            string error="";try{await coordinator.ConnectCurrentAsync();}catch(InvalidOperationException ex){error=ex.Message;}
            Check(error.Contains(target.Name)&&error.Contains(actual.Name),"mismatch names both accounts at "+title);
            Check(!env.Calls.Any(c=>c is "startup.click" or "close"||c.StartsWith("launch:")||c.StartsWith("save:")),"mismatch performs no account switch or game action at "+title);
            Check(new DailyProfiles(path).Read().Count==0,"mismatch never binds wrong player at "+title);
            using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(path,"login-identity-error.json")));
            Check(doc.RootElement.GetProperty("classification").GetString()=="tool_game_session_disagreement","store disagreement diagnostic at "+title);
            Check(doc.RootElement.GetProperty("actual").GetProperty("AccountKey").GetString()==actual.AccountKey,"actual hash recorded at "+title);
            Check(!doc.RootElement.TryGetProperty("token",out _),"diagnostic stores no authentication material at "+title);
        }
        {
            string path=Path.Combine(root,"wrong-target-diagnostic");var env=new DemoEnvironment(path);var target=env.Accounts[1];var snapshot=env.ReadSnapshot()!;
            var ex=DailyLoginFailure.Mismatch(path,"fixture",target,snapshot,env.Read(),false);
            using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(path,"login-identity-error.json")));
            Check(doc.RootElement.GetProperty("classification").GetString()=="different_logged_in_account","wrong selection distinct from different stores");
            Check(!ex.Message.Contains("资源管理器"),"ordinary wrong selection not blamed on launch environment");
        }
    }
}
