#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
namespace Dustweave.Pandora
{
    public sealed class CombatSuggestion
    {
        public double Time, Milliseconds;
        public Route Route;
        public CombatForecast Forecast;
        public int Evaluations;
        public string Error;
    }
    // One bounded pure-math job. Reset invalidates old rounds without queuing
    // additional workers. Never supply Unity objects or live native delegates.
    public sealed class CombatWorker
    {
        readonly object sync = new object();
        int epoch; bool busy; CombatSuggestion ready;
        CombatRoutePlanner planner = new CombatRoutePlanner();
        public void Reset() { lock(sync) { epoch++; ready=null; planner=new CombatRoutePlanner(); } }
        public bool Busy { get { lock(sync) return busy; } }
        public bool Submit(CombatFrame frame, IList<Route> candidates, Point previous, Func<Point,double> speed, Func<Point,bool> passable)
        {
            int version; CombatRoutePlanner current;
            lock(sync) { if(busy) return false; busy=true; version=epoch; current=planner; }
            var detached = new List<Route>(candidates);
            Task.Run(()=>
            {
                var watch=Stopwatch.StartNew(); var result=new CombatSuggestion { Time=frame.Time };
                try { result.Route=current.Choose(frame,detached,previous,speed,passable); result.Forecast=current.Selected; result.Evaluations=current.Evaluations; }
                catch(Exception e) { result.Error=e.GetType().Name+": "+e.Message; }
                result.Milliseconds=watch.Elapsed.TotalMilliseconds;
                lock(sync) { busy=false; if(version==epoch) ready=result; }
            });
            return true;
        }
        public CombatSuggestion Take(double time, double maximumAge=.4)
        {
            lock(sync)
            {
                var result=ready;ready=null;
                return result!=null && time>=result.Time && time-result.Time<=maximumAge ? result : null;
            }
        }
    }
}
