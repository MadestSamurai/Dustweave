using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

/// <summary>
/// The legacy joint 29-day allocation, with dated funding only while prices
/// change. Splits purchases at receipt dates, not recipes or every inventory.
/// Only ORIGINAL inventory allocated to sale can fund later purchases. Production
/// and expected drops never create credit. Today's orders still need real cash.
/// </summary>
internal sealed class DailyTradeTransitionFunding
{
    internal sealed record Receipt(long Item,int Source,int Day,long Price,long Stock);
    private readonly Receipt[] receipts;
    private readonly JsonObject[] offers;
    private readonly int[] boundaries;
    private readonly int start, purchaseStart;
    private int aggregateStart;
    private double[] cash = [];
    private long budget;
    public int Count => purchaseStart + (boundaries.Length-1)*offers.Length;
    public DailyTradeTransitionFunding(int first,JsonObject[] offers,IEnumerable<Receipt> receipts)
    {
        start=first; this.offers=offers;
        this.receipts=receipts.Where(r=>r.Stock>0 && r.Price>0 && r.Day>=0 && r.Day<29).ToArray();
        boundaries=new[]{1,30}.Concat(this.receipts.Select(r=>r.Day+1)).Distinct().Order().ToArray();
        purchaseStart=start+this.receipts.Length;
    }
    private int Buy(int phase,int offer)=>purchaseStart+phase*offers.Length+offer;
    public void Add(double[] upper,List<double[]> rows,List<double> lower,List<double> caps,
        double[] currentCash,double[] totalFunding,long budget,int aggregateStart,Func<JsonObject,long> price)
    {
        this.cash=currentCash; this.budget=budget; this.aggregateStart=aggregateStart;
        int n=upper.Length;
        void Row(double[] row,double lo,double hi){rows.Add(row);lower.Add(lo);caps.Add(hi);}
        for(int r=0;r<receipts.Length;r++)
        {
            var receipt=receipts[r]; upper[start+r]=receipt.Stock;
            var row=new double[n];row[start+r]=1;row[receipt.Source]=-1;Row(row,double.NegativeInfinity,0);
        }
        foreach(var group in receipts.Select((r,i)=>(r,i)).GroupBy(p=>p.r.Item))
        {
            var row=new double[n];foreach(var p in group)row[start+p.i]=1;
            Row(row,double.NegativeInfinity,group.First().r.Stock);
        }
        for(int j=0;j<offers.Length;j++)
        {
            var sum=new double[n];sum[aggregateStart+j]=-1;
            for(int t=0;t<boundaries.Length-1;t++)
            {
                int column=Buy(t,j);sum[column]=1;
                upper[column]=(boundaries[t+1]-boundaries[t])*N(offers[j]["limit"]);
            }
            Row(sum,0,0);
        }
        // A receipt at the end of day d first funds day d+1. Within a phase all
        // cash is already available; quantities fit that phase's daily supply.
        for(int t=0;t<boundaries.Length-1;t++)
        {
            var row=(double[])cash.Clone();
            for(int q=0;q<=t;q++)for(int j=0;j<offers.Length;j++)row[Buy(q,j)]=price(offers[j]);
            for(int r=0;r<receipts.Length;r++)if(receipts[r].Day<boundaries[t])row[start+r]=-receipts[r].Price;
            Row(row,double.NegativeInfinity,budget);
        }
        // Future cooking can be scheduled after its ingredients arrive; reserve
        // its entire potion cost by the horizon end, without pledging its output.
        var final=(double[])totalFunding.Clone();
        for(int r=0;r<receipts.Length;r++)final[start+r]=-receipts[r].Price;
        Row(final,double.NegativeInfinity,budget);
    }
    public void Reconcile(double[] x)
    {
        // Legacy zero-margin cleanup only REDUCES purchases. Remove the latest
        // allocations first; earlier funding inequalities can only improve.
        for(int j=0;j<offers.Length;j++)
        {
            double excess=Enumerable.Range(0,boundaries.Length-1).Sum(t=>x[Buy(t,j)])-x[aggregateStart+j];
            for(int t=boundaries.Length-2;t>=0 && excess>1e-7;t--)
            {double q=Math.Min(excess,x[Buy(t,j)]);x[Buy(t,j)]-=q;excess-=q;}
            Require(Math.Abs(excess)<1e-4,"Transition purchase allocation mismatch");
        }
        for(int r=0;r<receipts.Length;r++)x[start+r]=Math.Min(x[start+r],x[receipts[r].Source]);
    }
    public JsonObject Report(double[] x)=>O(("method","dated_existing_stock_receipts"),("today_spendable",false),
        ("initial_budget",budget),("current_spend",cash.Zip(x,(a,b)=>a*b).Sum()),
        ("receipts",Array(receipts.Select((r,i)=>O(("item",r.Item),("day",r.Day),("count",x[start+i]),("price",r.Price),
            ("amount",x[start+i]*r.Price))).Where(r=>r["count"]!.GetValue<double>()>1e-7))),
        ("phases",Array(Enumerable.Range(0,boundaries.Length-1).Select(t=>O(("first_day",boundaries[t]),("last_day",boundaries[t+1]-1),
            ("purchases",Array(offers.Select((o,j)=>O(("item",o["item"]),("count",x[Buy(t,j)]))).Where(r=>r["count"]!.GetValue<double>()>1e-7))))))));
}
