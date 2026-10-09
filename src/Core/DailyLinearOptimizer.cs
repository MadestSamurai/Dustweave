using System.Runtime.InteropServices;
using System.Security.Cryptography;
namespace Dustweave;

/// <summary>.NET owns the model and audits. HiGHS only solves LP/MIP matrices.</summary>
public static class DailyLinearOptimizer
{
    public const double Infinity = 1e30;
    private static readonly object gate = new();
    private static readonly Lazy<IntPtr> library = new(Load);
    static DailyLinearOptimizer() => NativeLibrary.SetDllImportResolver(typeof(DailyLinearOptimizer).Assembly, (name, _, _) => name == "daily-highs" ? library.Value : IntPtr.Zero);
    private static IntPtr Load()
    {
        using var stream = typeof(DailyLinearOptimizer).Assembly.GetManifestResourceStream("Dustweave.highs.dll") ?? throw new InvalidDataException("缺少内置跑商规划组件");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        byte[] bytes = memory.ToArray();
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        string folder = Path.Combine(DailyIdentity.DataRoot, "native");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "highs-" + hash + ".dll");
        bool Valid()
        {
            try
            {
                return DailyTradeCatalog.Hash(path) == hash;
            }
            catch (IOException) { return false; }
        }
        if (!Valid())
        {
            string temporary = Path.Combine(folder, "highs-" + hash + "-" + Guid.NewGuid().ToString("N") + ".dll");
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(bytes);
                file.Flush(true);
            }
            if (!File.Exists(path))
            {
                try
                {
                    File.Move(temporary, path);
                }
                catch (IOException) { if (Valid()) File.Delete(temporary); else path = temporary; }
            }
            else
                path = temporary;
        }
        return NativeLibrary.Load(path);
    }
    public sealed record Result(double[] Values, double[] RowDual, double Objective, double? Bound, bool Optimal, int Status);
    public static Result? Solve(double[] objective, double[] upper, IReadOnlyList<double[]> rows, IReadOnlyList<double> lower, IReadOnlyList<double> caps, bool integer, double seconds = 15, Action? check = null, int? integerColumns = null, int[]? extraIntegerColumns = null, double[]? initial = null)
    {
        if (objective.Length != upper.Length || rows.Count != lower.Count || rows.Count != caps.Count || rows.Any(r => r.Length != objective.Length) || !double.IsFinite(seconds) || seconds <= 0 || seconds > 300)
            throw new ArgumentException("Invalid optimization model");
        if (integerColumns is < 0 || integerColumns > objective.Length || extraIntegerColumns?.Any(j => j < 0 || j >= objective.Length) == true) throw new ArgumentException("Invalid integer column count");
        check?.Invoke();
        if (initial != null && (initial.Length != objective.Length || initial.Any(v => !double.IsFinite(v)))) throw new ArgumentException("Invalid initial solution");
        lock (gate)
        {
            IntPtr solver = Create();
            if (solver == IntPtr.Zero)
                throw new InvalidOperationException("无法初始化规划器");
            Callback? callback = null;
            System.Runtime.ExceptionServices.ExceptionDispatchInfo? interruption = null;
            try
            {
                static void Check(int s)
                {
                    if (s < 0)
                        throw new InvalidOperationException("跑商规划器配置失败");
                }
                Check(BoolOption(solver, "output_flag", 0));
                Check(IntOption(solver, "threads", 1));
                Check(DoubleOption(solver, "time_limit", seconds));
                Check(DoubleOption(solver, "mip_rel_gap", 0));
                Check(DoubleOption(solver, "mip_abs_gap", 0));
                int n = objective.Length, m = rows.Count;
                var starts = new int[m];
                var indices = new List<int>();
                var coefficients = new List<double>();
                for (int i = 0; i < m; i++)
                {
                    starts[i] = indices.Count;
                    for (int j = 0; j < n; j++)
                        if (rows[i][j] != 0)
                        {
                            indices.Add(j);
                            coefficients.Add(rows[i][j]);
                        }
                }
                var hi = upper.Select(x => double.IsPositiveInfinity(x) ? Infinity : x).ToArray();
                var lo = lower.Select(x => double.IsNegativeInfinity(x) ? -Infinity : x).ToArray();
                var cap = caps.Select(x => double.IsPositiveInfinity(x) ? Infinity : x).ToArray();
                Check(integer ? PassMip(solver, n, m, coefficients.Count, 2, 1, 0, objective, new double[n], hi, lo, cap, starts, indices.ToArray(), coefficients.ToArray(), Enumerable.Range(0, n).Select(j => j < (integerColumns ?? n) || extraIntegerColumns?.Contains(j) == true ? 1 : 0).ToArray()) : PassLp(solver, n, m, coefficients.Count, 2, 1, 0, objective, new double[n], hi, lo, cap, starts, indices.ToArray(), coefficients.ToArray()));
                if (initial != null) Check(SetSolution(solver, initial, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero));
                if (check != null)
                {
                    callback = (_, _, _, input, _) =>
                    {
                        // C ABI: HighsCallbackDataIn.user_interrupt is its first int.
                        // Never unwind a managed exception through native solver frames.
                        try
                        {
                            check();
                        }
                        catch (Exception error)
                        {
                            error.Data["daily_optimizer_native_callback"] = true;
                            interruption ??= System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error);
                            if (input != IntPtr.Zero)
                                Marshal.WriteInt32(input, 1);
                        }
                    };
                    Check(SetCallback(solver, callback, IntPtr.Zero));
                    foreach (int kind in new[] { 1, 2, 6 })
                        Check(StartCallback(solver, kind));
                }
                check?.Invoke();
                int execution = Run(solver);
                interruption?.Throw();
                check?.Invoke();
                Check(execution);
                int status = Status(solver);
                if (status == 8)
                    return null;
                Check(IntInfo(solver, "primal_solution_status", out int primal));
                if (primal != 2 && status == 13) return null;
                if (primal != 2)
                    throw new InvalidOperationException("跑商规划未取得可行解：" + status);
                var x = new double[n];
                var dual = new double[m];
                Check(Solution(solver, x, IntPtr.Zero, IntPtr.Zero, dual));
                if (integer)
                    foreach (int i in Enumerable.Range(0, n).Where(j => j < (integerColumns ?? n) || extraIntegerColumns?.Contains(j) == true))
                    {
                        if (Math.Abs(x[i] - Math.Round(x[i])) > 1e-5)
                            throw new InvalidDataException("规划整数校验失败");
                        x[i] = Math.Round(x[i]);
                    }
                if (x.Any(v => !double.IsFinite(v)) || x.Where((v, i) => v < -1e-6 || v > hi[i] + 1e-6).Any())
                    throw new InvalidDataException("规划变量超限");
                for (int i = 0; i < m; i++)
                {
                    double v = rows[i].Select((a, j) => a * x[j]).Sum();
                    // Mixed forecasts can combine large gold coefficients with fractional expected supply.
                    double tolerance = integerColumns < n ? Math.Max(1e-5, Math.Abs(v) * 1e-10) : 1e-5;
                    if (v < lo[i] - tolerance || v > cap[i] + tolerance)
                        throw new InvalidDataException($"独立资源约束核对失败：row={i}, value={v:R}, lower={lo[i]:R}, upper={cap[i]:R}");
                }
                double value = objective.Zip(x, (a, b) => a * b).Sum();
                double? bound = status == 7 ? value : null;
                if (integer && DoubleInfo(solver, "mip_dual_bound", out double b) >= 0 && double.IsFinite(b) && Math.Abs(b) < Infinity)
                    bound = b;
                return new(x, dual, value, bound, status == 7, status);
            }
            finally { Destroy(solver); GC.KeepAlive(callback); }
        }
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Callback(int kind, IntPtr message, IntPtr output, IntPtr input, IntPtr userData);
    [DllImport("daily-highs", EntryPoint = "Highs_setCallback", CallingConvention = CallingConvention.Cdecl)] private static extern int SetCallback(IntPtr h, Callback callback, IntPtr userData);
    [DllImport("daily-highs", EntryPoint = "Highs_startCallback", CallingConvention = CallingConvention.Cdecl)] private static extern int StartCallback(IntPtr h, int kind);
    [DllImport("daily-highs", EntryPoint = "Highs_create", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr Create();
    [DllImport("daily-highs", EntryPoint = "Highs_destroy", CallingConvention = CallingConvention.Cdecl)] private static extern void Destroy(IntPtr h);
    [DllImport("daily-highs", EntryPoint = "Highs_setBoolOptionValue", CallingConvention = CallingConvention.Cdecl)] private static extern int BoolOption(IntPtr h, string name, int value);
    [DllImport("daily-highs", EntryPoint = "Highs_setIntOptionValue", CallingConvention = CallingConvention.Cdecl)] private static extern int IntOption(IntPtr h, string name, int value);
    [DllImport("daily-highs", EntryPoint = "Highs_setDoubleOptionValue", CallingConvention = CallingConvention.Cdecl)] private static extern int DoubleOption(IntPtr h, string name, double value);
    [DllImport("daily-highs", EntryPoint = "Highs_passMip", CallingConvention = CallingConvention.Cdecl)] private static extern int PassMip(IntPtr h, int cols, int rows, int nnz, int format, int sense, double offset, double[] cost, double[] lower, double[] upper, double[] rowLower, double[] rowUpper, int[] starts, int[] indices, double[] values, int[] integer);
    [DllImport("daily-highs", EntryPoint = "Highs_passLp", CallingConvention = CallingConvention.Cdecl)] private static extern int PassLp(IntPtr h, int cols, int rows, int nnz, int format, int sense, double offset, double[] cost, double[] lower, double[] upper, double[] rowLower, double[] rowUpper, int[] starts, int[] indices, double[] values);
    [DllImport("daily-highs", EntryPoint = "Highs_run", CallingConvention = CallingConvention.Cdecl)] private static extern int Run(IntPtr h);
    [DllImport("daily-highs", EntryPoint = "Highs_getModelStatus", CallingConvention = CallingConvention.Cdecl)] private static extern int Status(IntPtr h);
    [DllImport("daily-highs", EntryPoint = "Highs_getIntInfoValue", CallingConvention = CallingConvention.Cdecl)] private static extern int IntInfo(IntPtr h, string name, out int value);
    [DllImport("daily-highs", EntryPoint = "Highs_getDoubleInfoValue", CallingConvention = CallingConvention.Cdecl)] private static extern int DoubleInfo(IntPtr h, string name, out double value);
    [DllImport("daily-highs", EntryPoint = "Highs_setSolution", CallingConvention = CallingConvention.Cdecl)] private static extern int SetSolution(IntPtr h, double[] values, IntPtr row, IntPtr dual, IntPtr rowDual);
    [DllImport("daily-highs", EntryPoint = "Highs_getSolution", CallingConvention = CallingConvention.Cdecl)] private static extern int Solution(IntPtr h, [Out] double[] values, IntPtr dual, IntPtr row, [Out] double[] rowDual);
}
