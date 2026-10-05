using System;
namespace BD2Territory
{
 public sealed class CookingProgress
 {
  public int Schema{get;set;}=1;public string Account{get;set;}="";public string Token{get;set;}="";
  public int Recipe{get;set;}public int ResultItem{get;set;}public int Count{get;set;}public long SubmittedTicks{get;set;}
  public long ConfirmedTotal{get;set;}public string State{get;set;}="idle";
  public bool Pending=>State=="pending";
 }
 public sealed class CookingReply {public string Token="";public bool Accepted;public bool Rejected;public int Recipe;public int Count;public string Error="";}
 public static class CookingPlanner
 {
  public static int Count(int limit,int capacity,long[] stocks,int[] required)
  {
   if(limit<1||limit>1000||capacity<0||stocks==null||required==null||stocks.Length==0||stocks.Length!=required.Length)throw new ArgumentException("Invalid cooking input");
   long result=Math.Min(limit,capacity);
   for(int i=0;i<stocks.Length;i++){if(stocks[i]<0||required[i]<=0)throw new ArgumentException("Invalid cooking ingredients");result=Math.Min(result,stocks[i]/required[i]);}
   return (int)result;
  }
  public static void Confirm(CookingProgress state,CookingReply reply)
  {
   if(!state.Pending||reply.Token!=state.Token||reply.Recipe!=state.Recipe||reply.Count!=state.Count)throw new InvalidOperationException("Cooking receipt does not match the pending batch");
   if(reply.Accepted&&!reply.Rejected){state.ConfirmedTotal=checked(state.ConfirmedTotal+state.Count);state.State="confirmed";}
   else if(reply.Rejected&&!reply.Accepted)state.State="rejected";
   else throw new InvalidOperationException("Cooking result is unknown; no repeat request will be sent");
  }
 }
}
