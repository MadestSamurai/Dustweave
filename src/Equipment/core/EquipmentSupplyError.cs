namespace BD2Equipment.Core;

// Stable presentation key; diagnostics retain the original technical detail.
public sealed class EquipmentSupplyError(string key,string detail=""):InvalidOperationException(key+(detail.Length>0?": "+detail:""))
{
 public string Key{get;}=key;
 public string Detail{get;}=detail;
}
