using System;
using System.Linq;
namespace BD2Territory
{
 public enum PlantingPreviewDecision { Ready, Refresh, Wait, Reject }
 // Availability and native preview size are different facts. Never turn cache reconciliation
 // into a layout error, and never shrink the transaction to bypass a failed check.
 public sealed class PlantingPreviewProgress
 {
  private bool refreshed;
  public string Reason{get;private set;}="";
  public void Reset(){refreshed=false;Reason="";}
  public PlantingPreviewDecision Evaluate(bool multi,string[] keys,FarmReadiness[] fields)
  {
   if(!multi)return Result(PlantingPreviewDecision.Reject,"游戏批量播种模式已关闭，已停止付款");
   if(keys==null||fields==null||keys.Length!=fields.Length)return Result(PlantingPreviewDecision.Reject,"批量预览数据不完整，已停止付款");
   if(keys.Length==0)
   {
    if(!refreshed){refreshed=true;return Result(PlantingPreviewDecision.Refresh,"重新生成游戏批量预览（当前 "+keys.Length+" 格）");}
    return Result(PlantingPreviewDecision.Reject,"重新生成后游戏仍未提供可播种农田，请检查目标农田是否可用");
   }
   if(!PlantingTransaction.ValidKeys(keys))return Result(PlantingPreviewDecision.Reject,"批量预览包含重复或缺失的农田身份，已停止付款");
   int occupied=fields.Count(f=>f==FarmReadiness.Occupied),live=fields.Count(f=>f==FarmReadiness.LiveCrop),unavailable=fields.Count(f=>f==FarmReadiness.NativeUnavailable);
   if(occupied+live+unavailable>0)return Result(PlantingPreviewDecision.Reject,"批量预览未通过空田核对：原生占用 "+occupied+"，存活作物 "+live+"，不可播种 "+unavailable+"；已停止付款");
   int unknown=fields.Count(f=>f==FarmReadiness.Unknown),pending=fields.Count(f=>f==FarmReadiness.PendingRequest),cache=fields.Count(f=>f==FarmReadiness.ReconcilingCache);
   if(unknown+pending+cache>0)return Result(PlantingPreviewDecision.Wait,"批量预览 "+keys.Length+" 格，等待空田核对：缓存 "+cache+"，数据未载入 "+unknown+"，请求未结算 "+pending);
   if(fields.Any(f=>f!=FarmReadiness.Empty))return Result(PlantingPreviewDecision.Reject,"批量预览出现未知空田状态，已停止付款");
   return Result(PlantingPreviewDecision.Ready,"");
  }
  private PlantingPreviewDecision Result(PlantingPreviewDecision value,string reason){Reason=reason;return value;}
 }
}
