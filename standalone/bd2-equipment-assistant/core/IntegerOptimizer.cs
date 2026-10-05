using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
namespace BD2Equipment.Core;
// C# owns planning and all resource checks. Only the small native MILP solver is
// embedded; no Python, NumPy, SciPy, BLAS or second managed runtime is shipped.
internal static class IntegerOptimizer
{
 static readonly object Gate=new();
 static readonly Lazy<IntPtr> Library=new(Load);
 static IntegerOptimizer()=>NativeLibrary.SetDllImportResolver(typeof(IntegerOptimizer).Assembly,(name,assembly,search)=>name=="equipment-highs"?Library.Value:IntPtr.Zero);
 static IntPtr Load(){
  using var resource=typeof(IntegerOptimizer).Assembly.GetManifestResourceStream("Equipment.highs.dll")??throw new InvalidOperationException("缺少内置规划组件");using var buffer=new MemoryStream();resource.CopyTo(buffer);byte[] bytes=buffer.ToArray();string hash=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();string dir=Path.Combine(J.Data,"native");Directory.CreateDirectory(dir);string path=Path.Combine(dir,"highs-"+hash+".dll");
  bool Valid(){try{using var file=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(file)).Equals(hash,StringComparison.OrdinalIgnoreCase);}catch(IOException){return false;}}
  if(!Valid()){
   string temp=Path.Combine(dir,"highs-"+hash+"-"+Guid.NewGuid().ToString("N")+".dll");using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(bytes);file.Flush(true);}
   // Never replace a DLL held by a running older instance.
   if(!File.Exists(path)){try{File.Move(temp,path);}catch(IOException){if(Valid())File.Delete(temp);else path=temp;}}else path=temp;
  }
  return NativeLibrary.Load(path);
 }
 public static long[] Solve(List<double[]> rows,List<double> caps,double[] objective,bool integralObjective){
  lock(Gate){IntPtr solver=Create();if(solver==IntPtr.Zero)throw new InvalidOperationException("无法初始化规划器");try{
   void Check(int status){if(status<0)throw new InvalidOperationException("规划器配置失败");}
   Check(BoolOption(solver,"output_flag",0));Check(IntOption(solver,"threads",1));Check(DoubleOption(solver,"time_limit",10));Check(DoubleOption(solver,"mip_rel_gap",0));Check(DoubleOption(solver,"mip_abs_gap",integralObjective?0:1e-7));
   int n=objective.Length,m=rows.Count;var starts=new int[m];var indices=new List<int>();var values=new List<double>();for(int i=0;i<m;i++){starts[i]=indices.Count;for(int j=0;j<n;j++)if(rows[i][j]!=0){indices.Add(j);values.Add(rows[i][j]);}}
   Check(PassMip(solver,n,m,values.Count,2,-1,0,objective,new double[n],Enumerable.Repeat(1e30,n).ToArray(),Enumerable.Repeat(-1e30,m).ToArray(),caps.ToArray(),starts,indices.ToArray(),values.ToArray(),Enumerable.Repeat(1,n).ToArray()));
   Check(Run(solver));
   // Some historical presolvers rejected feasible integral packing models.
   // Retry the same model without that optional pass; no heuristic result is accepted.
   if(Status(solver)!=7){Check(StringOption(solver,"presolve","off"));Check(Run(solver));}
   if(Status(solver)!=7)throw new InvalidOperationException("材料规划未得到最优结果，请缩小预算后重试");
   var x=new double[n];Check(Solution(solver,x,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero));var integers=x.Select(v=>(long)Math.Round(v)).ToArray();
   if(integers.Any(v=>v<0)||rows.Where((row,i)=>row.Select((a,j)=>a*integers[j]).Sum()>caps[i]+1e-6).Any())throw new InvalidOperationException("规划整数校验失败");return integers;
  }finally{Destroy(solver);}}
 }
 [DllImport("equipment-highs",EntryPoint="Highs_create",CallingConvention=CallingConvention.Cdecl)]static extern IntPtr Create();
 [DllImport("equipment-highs",EntryPoint="Highs_destroy",CallingConvention=CallingConvention.Cdecl)]static extern void Destroy(IntPtr h);
 [DllImport("equipment-highs",EntryPoint="Highs_setBoolOptionValue",CallingConvention=CallingConvention.Cdecl)]static extern int BoolOption(IntPtr h,string name,int value);
 [DllImport("equipment-highs",EntryPoint="Highs_setIntOptionValue",CallingConvention=CallingConvention.Cdecl)]static extern int IntOption(IntPtr h,string name,int value);
 [DllImport("equipment-highs",EntryPoint="Highs_setDoubleOptionValue",CallingConvention=CallingConvention.Cdecl)]static extern int DoubleOption(IntPtr h,string name,double value);
 [DllImport("equipment-highs",EntryPoint="Highs_setStringOptionValue",CallingConvention=CallingConvention.Cdecl)]static extern int StringOption(IntPtr h,string name,string value);
 [DllImport("equipment-highs",EntryPoint="Highs_passMip",CallingConvention=CallingConvention.Cdecl)]static extern int PassMip(IntPtr h,int cols,int rows,int nnz,int format,int sense,double offset,double[] cost,double[] lower,double[] upper,double[] rowLower,double[] rowUpper,int[] starts,int[] indices,double[] values,int[] integer);
 [DllImport("equipment-highs",EntryPoint="Highs_run",CallingConvention=CallingConvention.Cdecl)]static extern int Run(IntPtr h);
 [DllImport("equipment-highs",EntryPoint="Highs_getModelStatus",CallingConvention=CallingConvention.Cdecl)]static extern int Status(IntPtr h);
 [DllImport("equipment-highs",EntryPoint="Highs_getSolution",CallingConvention=CallingConvention.Cdecl)]static extern int Solution(IntPtr h,[Out]double[] values,IntPtr dual,IntPtr row,IntPtr rowDual);
}
