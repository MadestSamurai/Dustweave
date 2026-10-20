using System;
using System.Collections.Generic;
namespace BD2Territory
{
 public enum TerritoryWorkPhase { Idle, Harvesting, Planting, Gathering, Processing }
 // Harvest one finite crop batch, replant its fields, then gather minerals/wood
 // while crops grow. Respawns never extend the current workset indefinitely.
 public sealed class TerritoryWorkCycle
 {
  readonly HashSet<string> harvest=new HashSet<string>(StringComparer.Ordinal),gather=new HashSet<string>(StringComparer.Ordinal),fields=new HashSet<string>(StringComparer.Ordinal);
  public TerritoryWorkPhase Phase{get;private set;}=TerritoryWorkPhase.Idle;
  public int Remaining=>Phase==TerritoryWorkPhase.Harvesting?harvest.Count:Phase==TerritoryWorkPhase.Gathering?gather.Count:Phase==TerritoryWorkPhase.Planting?fields.Count:0;
  public bool AcceptsResource(string key)=>Phase==TerritoryWorkPhase.Harvesting?harvest.Contains(key):Phase==TerritoryWorkPhase.Gathering&&gather.Contains(key);
  public bool AcceptsField(string key)=>Phase==TerritoryWorkPhase.Planting&&fields.Contains(key);
  public TerritoryWorkPhase Advance(IEnumerable<string> crops,IEnumerable<string> resources,IEnumerable<string> emptyFields,bool actionPending=false)
  {
   if(actionPending)return Phase;
   var availableCrops=new HashSet<string>(crops,StringComparer.Ordinal);var availableResources=new HashSet<string>(resources,StringComparer.Ordinal);var availableFields=new HashSet<string>(emptyFields,StringComparer.Ordinal);
   if(Phase==TerritoryWorkPhase.Idle){harvest.UnionWith(availableCrops);gather.UnionWith(availableResources);Phase=TerritoryWorkPhase.Harvesting;}
   if(Phase==TerritoryWorkPhase.Harvesting){harvest.IntersectWith(availableCrops);if(harvest.Count==0){fields.UnionWith(availableFields);Phase=TerritoryWorkPhase.Planting;}}
   if(Phase==TerritoryWorkPhase.Planting){fields.IntersectWith(availableFields);if(fields.Count==0)Phase=TerritoryWorkPhase.Gathering;}
   if(Phase==TerritoryWorkPhase.Gathering){gather.IntersectWith(availableResources);if(gather.Count==0)Phase=TerritoryWorkPhase.Processing;}
   return Phase;
  }
  public void Gathered(string key){harvest.Remove(key);gather.Remove(key);}
  public void Planted(IEnumerable<string> keys){foreach(var key in keys)fields.Remove(key);}
  public void FinishProcessing(){if(Phase!=TerritoryWorkPhase.Processing)throw new InvalidOperationException("Work pass has not reached processing");Reset();}
  public void Reset(){harvest.Clear();gather.Clear();fields.Clear();Phase=TerritoryWorkPhase.Idle;}
 }
}
