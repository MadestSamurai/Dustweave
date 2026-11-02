using System.Text.Json.Nodes;
namespace Dustweave.Soul;

internal static class NativeSearchRisk
{
    internal static JsonObject Assess(int wins,int trials,int lives,int remaining)
    {
        if(trials<=0||wins<0||wins>trials||lives<=0||remaining<=0)throw new ArgumentException("Invalid search risk evidence");
        double p=(double)wins/trials,z=1.645,z2=z*z;
        double lower=(p+z2/(2*trials)-z*Math.Sqrt(p*(1-p)/trials+z2/(4.0*trials*trials)))/(1+z2/trials);
        // Probability of all remaining wins before exhausting the allowed
        // failures, conditional on an explicitly stated common lower p.
        double term=Math.Pow(lower,remaining),completion=term;
        for(int lost=1;lost<lives;lost++){term*=((double)remaining+lost-1)/lost*(1-lower);completion+=term;}
        // Future boards improve as purchases and income arrive. Treating the
        // pessimistic constant-board estimate as a hard gate discarded good
        // current offers and spent coins repeatedly refreshing. Gate the
        // current decision by actual remaining retries; report the horizon
        // estimate separately instead of claiming the future is fixed.
        double required=lives>=4?.80:lives==3?.85:lives==2?.93:.985;
        return new(){["ready"]=p>=required,["requiredCurrentWinRate"]=required,["lives"]=lives,["remainingWaves"]=remaining,["winRateLowerBound"]=lower,["completionEstimateAtThisBound"]=completion,
            ["assumption"]="Independent retries; every later wave must be replanned to achieve at least this probability. This is a risk budget, not a known future-win guarantee."};
    }
    internal static void Test()
    {
        double Value(int l,int r)=>Assess(468,512,l,r)["completionEstimateAtThisBound"]!.GetValue<double>();
        if(Value(5,15)<=Value(4,15)||Value(5,15)>=Value(5,10)||Assess(10,512,5,15)["ready"]!.GetValue<bool>())throw new Exception("Risk-budget ordering failed");
        if(!Assess(512,512,1,15)["ready"]!.GetValue<bool>())throw new Exception("Fully audited reliable board rejected");
        if(NativeFastPlanner.Remaining(-40,100)!=0||NativeFastPlanner.Remaining(140,100)!=1||NativeFastPlanner.Remaining(50,100)!=.5)throw new Exception("Overkill must not improve survival ranking");
        if(NativeFastPlanner.DecisionRank(0,-1,30).CompareTo(NativeFastPlanner.DecisionRank(0,-1,10))<=0)throw new Exception("Equal losing candidates preferred faster defeat");
        if(NativeFastPlanner.DecisionRank(1,.8,10).CompareTo(NativeFastPlanner.DecisionRank(1,.8,30))<=0)throw new Exception("Winning candidates must still prefer faster clears");
        Console.WriteLine("Native search risk checks passed: lives, remaining waves, weak and reliable boards");
    }
}
