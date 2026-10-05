namespace BD2Territory
{
 public sealed class RecipeOption
 {
  public int Id{get;set;}public string Name{get;set;}="";public string Reason{get;set;}="";public string Ratio{get;set;}="";
  public bool Available{get;set;}public string Label=>Name+(Available?"":" · "+Reason);
 }
}
