using System.Runtime.InteropServices;
using System.Text.Json;

// Runs the actual client Mono in a separate CLI process. Never opens/injects the game.
internal static class MonoBootstrapHost
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void SetDirs([MarshalAs(UnmanagedType.LPStr)] string lib, [MarshalAs(UnmanagedType.LPStr)] string config);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void Config(IntPtr path);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void SetPath([MarshalAs(UnmanagedType.LPStr)] string path);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr Init([MarshalAs(UnmanagedType.LPStr)] string name, [MarshalAs(UnmanagedType.LPStr)] string version);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr Open(IntPtr domain, [MarshalAs(UnmanagedType.LPStr)] string path);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr One(IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr OpenBytes(byte[] bytes, uint size, int copy, out int status);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr LoadImage(IntPtr image, [MarshalAs(UnmanagedType.LPStr)] string name, out int status, int reflectionOnly);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr FindClass(IntPtr image, [MarshalAs(UnmanagedType.LPStr)] string ns, [MarshalAs(UnmanagedType.LPStr)] string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr FindMethod(IntPtr cls, [MarshalAs(UnmanagedType.LPStr)] string name, int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr Invoke(IntPtr method, IntPtr target, IntPtr arguments, out IntPtr error);
    public static void Run(string managed, string output)
    {
        output = Path.GetFullPath(output);
        Environment.SetEnvironmentVariable("DAILY_PROBE_OUTPUT", output);
        string game = Directory.GetParent(Directory.GetParent(managed)!.FullName)!.FullName;
        var library = NativeLibrary.Load(Path.Combine(game, "MonoBleedingEdge", "EmbedRuntime", "mono-2.0-bdwgc.dll"));
        T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
        Export<SetDirs>("mono_set_dirs")(managed, Path.Combine(game, "MonoBleedingEdge", "etc"));
        Export<SetPath>("mono_set_assemblies_path")(managed);
        Export<Config>("mono_config_parse")(IntPtr.Zero);
        var domain = Export<Init>("mono_jit_init_version")("daily-cold-start", "v4.0.30319");
        if (domain == IntPtr.Zero) throw new Exception("Mono initialization failed");
        var payload=File.ReadAllBytes(Path.Combine(output,"bootstrap.dll"));
        var payloadImage=Export<OpenBytes>("mono_image_open_from_data")(payload,(uint)payload.Length,1,out var status);
        var loaded=Export<LoadImage>("mono_assembly_load_from_full")(payloadImage,"",out status,0);
        if(loaded==IntPtr.Zero||status!=0)throw new Exception("Native payload load failed");
        var assembly = Export<Open>("mono_domain_assembly_open")(domain, Path.Combine(output, "Probe.exe"));
        if (assembly == IntPtr.Zero) throw new Exception("Probe assembly failed to load");
        var image = Export<One>("mono_assembly_get_image")(assembly);
        var cls = Export<FindClass>("mono_class_from_name")(image, "", "Runner");
        var method = Export<FindMethod>("mono_class_get_method_from_name")(cls, "Run", 0);
        Export<Invoke>("mono_runtime_invoke")(method, IntPtr.Zero, IntPtr.Zero, out var error);
        if (error != IntPtr.Zero || !File.Exists(Path.Combine(output, "passed.txt")))
            throw new Exception("Cold Mono bootstrap failed: " + (File.Exists(Path.Combine(output,"outer-error.txt")) ? File.ReadAllText(Path.Combine(output,"outer-error.txt")) : "native managed exception"));
        File.WriteAllText(Path.Combine(output,"validation.json"),JsonSerializer.Serialize(new{status="passed",freshMono=true,gameTouched=false,pythonInvoked=false,checks=new[]{"cold dependency load","unload and reload","second component handoff","SDK readiness avoids cached Lazy exception"}},new JsonSerializerOptions{WriteIndented=true}));
    }
}



