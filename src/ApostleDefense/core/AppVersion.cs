namespace BD2ApostleDefense;

// Desktop/planner version is independent of the already loaded game component.
public static class AppVersion
{
 public static string Current=>typeof(AppVersion).Assembly.GetName().Version!.ToString(3);
}
