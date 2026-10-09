#nullable disable
using System;
using System.Runtime.Serialization;
namespace BD2Daily {
 [DataContract] public sealed class SuiteCommand {
  [DataMember] public string Id="", Tool="", Owner="", Account="", Player="";
  [DataMember] public string ModulePayload="", ModuleHash="";
  [DataMember] public int ProcessId, OwnerProcessId;
  [DataMember] public long StartTicks, Expires, OwnerStartTicks;
 }
 [DataContract] public sealed class SuiteStatus {
  [DataMember] public string Request="", Tool="", State="idle", Error="", Owner="", Account="", Player="", RejectedRequest="", RejectedReason="";
  [DataMember] public string Pending="";
  [DataMember] public long At;
  [DataMember] public string[] Available=new string[0], Fingerprints=new string[0];
 }
 // Pure transition rules shared by runtime and offline regression.
 public static class SuiteRules {
  public static string ModuleEntry(string id) {
   switch(id) {
    case "daily": return "BD2Daily.Live.Bridge";
    case "mansion-runaway": return "Dustweave.Mansion.Runtime.Loader";
    case "equipment": return "BD2Equipment.Live.Bridge";
    case "fishing": case "fishing-daily": return "BD2Fishing.Runtime.Loader";
    case "sichuan": case "sichuan-daily": return "BD2Sichuan.Runtime.Loader";
    case "territory": return "BD2Territory.Runtime.Loader";
    case "rhythm": return "BD2Rhythm.Runtime.Loader";
    case "apostle-defense": return "BD2ApostleDefense.Runtime.Loader";
    case "infinite-gacha": return "BD2InfiniteGacha.Runtime.Loader";
    case "secret-vision": return "BD2SecretVision.Runtime.Loader";
    case "fiend-hunter": return "BD2FiendHunterRuntimePublic1.Loader";
    default: return "";
   }
  }
  public static string Validate(SuiteCommand c,DailySnapshot s,long now) {
   if(c==null||string.IsNullOrEmpty(c.Id)||string.IsNullOrEmpty(c.Owner))return "missing-request";
   if(c.OwnerProcessId<=0||c.OwnerStartTicks<=0)return "missing-owner";
   if(c.Expires<=now||c.Expires>now+TimeSpan.FromSeconds(45).Ticks)return "expired-request";
   if(c.ProcessId!=s.ProcessId||c.StartTicks!=s.ProcessStartTicks)return "game-changed";
   if(c.Account!=s.AccountKey||c.Player!=s.PlayerKey)return "account-changed";
   if(c.Tool!="daily"&&(string.IsNullOrEmpty(c.Account)||string.IsNullOrEmpty(c.Player)))return "login-required";
   return "";
  }
 }
}
