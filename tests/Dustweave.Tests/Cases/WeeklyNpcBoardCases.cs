using System.Text.Json.Nodes;
using Dustweave;
using static Dustweave.DailyData;

static class WeeklyNpcBoardCases
{
    public static async Task Run(string root, List<string> checks)
    {
        await FieldRouteArrivalCases.Run(Path.Combine(root, "field-route-arrival"), checks);
        string assets=Path.Combine(root,"assets");Directory.CreateDirectory(Path.Combine(assets,"flows"));
        foreach(string name in new[]{"collection-catalog.json","steal-catalog.json"})File.Copy(Path.Combine(TestPaths.SourceRoot,"assets","flows",name),Path.Combine(assets,"flows",name),true);
        void Check(bool yes,string name){if(!yes)throw new Exception(name);checks.Add(name);}
        foreach(string mode in new[]{"normal","scroll","already-active","quota-zero","board-open","wrong-confirmation","unreachable","lost-confirmation","resume-confirmation","unknown-popup","stale-astar","native-stopped","native-delayed"})
        {
            using var h=new WorkflowHarness(Path.Combine(root,mode),[]);
            h.Workflow.Settings.Weekly.Npc=true;
            string page=mode=="board-open"?"QuestBoardUI":"GameFieldDefaultUI";
            h.Page(page);
            bool active=mode=="already-active", near=false, visible=mode!="scroll";
            bool boardOpen=mode=="board-open", navigating=false;long selected=0;string query="initial";double navAt=0;
            var steps=new List<string>();
            void SetPage(string type){page=type;h.Page(type);}
            JsonObject Native()=>O(("State","ready"),("Error",""),("Query",query),("Pack",1),("Map",11),("Week",500),("ActionsSupported",true),("CanAccept",true),("Navigating",navigating),
                ("Limit",3),("Completed",mode=="quota-zero"?3:0),("Remaining",mode=="quota-zero"?0:active?2:3),
                ("Rows",new JsonArray(O(("id",100),("packId",1),("mapId",11),("conditionType",19))).ToJsonString()),
                ("Active",new JsonArray(active?O(("id",100)):null).Where(x=>x!=null).ToJsonStringForTest()),("Posted",new[]{100}),("Cleared",new int[0]),("Wild",new JsonArray()),
                ("Boards",new[]{new { Map=11,Npc=7,Instance=71,Near=near }}),
                ("Board",O(("Ui",boardOpen?2:0),("VisibleQuests",visible?new[]{100}:[]),("PopupQuest",selected),("PopupCanAccept",selected==100))));
            JsonObject Read(string id,string path,JsonNode value)=>O(("Id",id),("Error",""),("Values",new JsonArray(O(("Path",path),("Error",""),("Json",value.ToJsonString())))));
            h.Readings=()=>[Read("weekly_npc.native","$self",Native()),Read("mainline.reset","GetWeeklyResetTime().Ticks",JsonValue.Create(500)!),Read("mainline.map",DailyFieldRoute.MapPath,O(("id",11),("packId",1)))];
            h.OnCommand=c=>{
                string k=S(c["Kind"]);steps.Add(k);
                switch(k){
                    case "weekly_npc_query":query=S(c["Id"]);break;
                    case "weekly_npc_board_nav":
                                                navAt=h.Time;
                        if(mode is "unreachable" or "native-stopped")navigating=false;
                        else if(mode is "stale-astar" or "native-delayed"){
                            navigating=true;
                            if(mode=="stale-astar")h.Frame["SquareNavigation"]=O(("Kind",k),("State","failed"),("Reason","old A* failure"));
                        }else near=true;
                        break;
                    case "weekly_npc_board_stop":navigating=false;break;
                    case "weekly_npc_board_open":Check(near,"walked to native interaction range "+mode);boardOpen=true;SetPage("QuestBoardUI");break;
                    case "weekly_npc_board_scroll":Check(boardOpen,"scroll only on visible board");visible=true;break;
                    case "weekly_npc_board_select":Check(boardOpen&&visible,"select visible board item "+mode);selected=mode=="wrong-confirmation"?101:100;SetPage("QuestPopupUI");break;
                    case "weekly_npc_accept":Check(page=="QuestPopupUI"&&boardOpen&&selected==100,"accept via matching native confirmation "+mode);if(mode=="lost-confirmation")throw new DailyStepException("uncertain","lost confirmation");active=true;selected=0;SetPage("QuestBoardUI");break;
                    case "back":if(page=="QuestPopupUI"){selected=0;SetPage("QuestBoardUI");}else{boardOpen=false;SetPage("GameFieldDefaultUI");}break;
                    default:throw new Exception("Unexpected board command "+k);
                }
            };
            h.OnDelay=()=>{if(navigating&&h.Time-navAt>=1){near=true;navigating=false;}};
            if(mode is "resume-confirmation" or "unknown-popup")
            {
                boardOpen=true;SetPage("QuestPopupUI");
                var surface=h.Frame["Surfaces"]![0]!.AsObject();surface["NativeContext"]=mode=="unknown-popup"?"":"weekly_npc_accept:100";
                h.Frame["Surfaces"]!.AsArray().Add(O(("Id",2),("Type","QuestBoardUI"),("Popup",false),("Order",-1),("InputReady",true),("Targets",new JsonArray())));
                try{await DailyWeeklyNpcBoard.Close(h.Workflow);Check(mode!="unknown-popup","unknown popup must remain");}catch(DailyNpcUnavailable){Check(mode=="unknown-popup"&&steps.Count==0,"foreign task popup never clicked");}
                if(mode=="resume-confirmation")Check(steps.SequenceEqual(new[]{"back","back"}),"resume cancels old confirmation and closes board without accepting");
                continue;
            }
            var workflow=new DailyWorkflow(h.Root,assets,h.Context,h.Driver,h.Business,h.Workflow.Navigation,[],()=>h.Stopped,h.Workflow.Relay);var route=new DailyFieldRoute(workflow);var npc=new DailyWeeklyNpc(route,false);
            bool failed=false;
            try{await npc.Prepare(1);}catch(Exception ex) when(ex is DailyNpcUnavailable or DailyStepException){failed=true;}
            Check(failed==(mode is "wrong-confirmation" or "unreachable" or "native-stopped" or "lost-confirmation"),"board result "+mode);
            Check(steps.Count(s=>s=="weekly_npc_accept")<2,"no duplicate confirmation "+mode);
            if(mode is "already-active" or "quota-zero")Check(steps.SequenceEqual(new[]{"weekly_npc_query"}),"no board travel for accepted or exhausted tasks "+mode);
            else if(!failed){Check(active&&!boardOpen&&page=="GameFieldDefaultUI","accepted and closed "+mode);Check(steps.IndexOf("weekly_npc_board_select")<steps.IndexOf("weekly_npc_accept"),"visible selection precedes accept "+mode);}
            if(mode=="board-open")Check(!steps.Contains("weekly_npc_board_nav")&&!steps.Contains("weekly_npc_board_open"),"reuse open board");
            if(mode=="wrong-confirmation")Check(!steps.Contains("weekly_npc_accept"),"mismatched quest never accepted");
            if(mode=="native-stopped")Check(h.Time-navAt<10&&steps.Contains("weekly_npc_board_stop"),"stopped native navigation exits promptly and clears movement");
            if(mode=="stale-astar")Check(!failed&&active,"stale plaza A* result cannot block cartridge native navigation");
            if(mode is "unreachable" or "native-stopped")Check(!steps.Contains("weekly_npc_board_open")&&!steps.Contains("weekly_npc_accept"),"unreachable board cannot bypass interaction");
        }
    }
    static string ToJsonStringForTest(this IEnumerable<JsonNode?> rows)=>new JsonArray(rows.Select(Copy).ToArray()).ToJsonString();
}
