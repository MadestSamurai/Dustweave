using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BD2Daily.Live;
using Dustweave;

static class ObservationPerformanceCases
{
    const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    class Base
    {
        private int hidden = 7;
        public int Item = 4;
        public int Shadow => 5;
        public void ChangeHidden(int value) => hidden = value;
    }
    sealed class State : Base
    {
        public int Value = 11, Reads;
        public int Current => ++Reads + Value;
        public new int Shadow = 9;
        public new int this[int index] => index;
        public static int StaticValue = 13;
        public bool Fail;
        public int Recoverable => Fail ? throw new InvalidOperationException("temporarily unavailable") : Value;
        public bool Ready() => Value > 0;
        public bool Ready(int ignored) => false;
    }

    static object OriginalRead(object? instance, Type type, string name, bool isStatic = false)
    {
        var flags = BindingFlags.Public | BindingFlags.NonPublic | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
        for (var t = type; t != null; t = t.BaseType)
        {
            var p = t.GetProperty(name, flags | BindingFlags.DeclaredOnly);
            if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(instance)!;
            var f = t.GetField(name, flags | BindingFlags.DeclaredOnly);
            if (f != null) return f.GetValue(instance)!;
        }
        throw new MissingMemberException(type.FullName, name);
    }
    static FieldInfo[] OriginalFields(Type type, Type stop)
    {
        var fields = new List<FieldInfo>();
        for (var t = type; t != null && t != stop; t = t.BaseType)
            fields.AddRange(t.GetFields(Instance | BindingFlags.DeclaredOnly));
        return fields.ToArray();
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference ReadTemporary(ObservationMetadata cache)
    {
        var value = new State(); cache.Read(value, value.GetType(), "Value");
        return new WeakReference(value);
    }
    static object DifferentAssembly(string assembly, int value)
    {
        var module = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(assembly), AssemblyBuilderAccess.Run).DefineDynamicModule(assembly);
        var type = module.DefineType("Same.ClientType", TypeAttributes.Public);
        type.DefineField("Value", typeof(int), FieldAttributes.Public);
        var runtime = type.CreateType()!; var instance = Activator.CreateInstance(runtime)!;
        runtime.GetField("Value")!.SetValue(instance, value); return instance;
    }

    public static void Run(string root, List<string> cases)
    {
        void Check(bool value, string name) { if (!value) throw new Exception(name); cases.Add(name); }
        var cache = new ObservationMetadata(typeof(object)); var state = new State(); var type = state.GetType();
        foreach (var name in new[] { "Value", "hidden", "Shadow", "Item" })
            Check(Equals(cache.Read(state, type, name), OriginalRead(state, type, name)), "cached member preserves derived/base/indexer resolution: " + name);
        state.Value = 29; state.ChangeHidden(31);
        Check((int)cache.Read(state, type, "Value") == 29 && (int)cache.Read(state, type, "hidden") == 31, "metadata cache reads changed field values without stale state");
        int before = (int)cache.Read(state, type, "Current");
        Check((int)cache.Read(state, type, "Current") == before + 1 && state.Reads == 2, "cached getter is invoked for every observation");
        var replacement = new State { Value = 43 };
        Check((int)cache.Read(replacement, type, "Value") == 43, "metadata reuse observes a replacement object");
        state.Fail = true;
        try { cache.Read(state, type, "Recoverable"); throw new Exception("Getter error was hidden"); }
        catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { }
        state.Fail = false;
        Check((int)cache.Read(state, type, "Recoverable") == 29, "getter failure does not poison later successful observations");
        Check((int)cache.Read(null, type, "StaticValue", true) == State.StaticValue, "static metadata has its own lookup scope");
        State.StaticValue = 47;
        Check((int)cache.Read(null, type, "StaticValue", true) == 47, "static values are never cached");
        try { cache.Read(state, type, "StaticValue"); throw new Exception("Static member leaked into instance scope"); }
        catch (MissingMemberException) { cases.Add("cached static member cannot be used as an instance member"); }
        var method = cache.Method(type, "Ready");
        Check(method.GetParameters().Length == 0 && (bool)method.Invoke(state, null)!, "cached methods retain exact zero-argument overload");
        state.Value = -1;
        Check(!(bool)cache.Method(type, "Ready").Invoke(state, null)!, "cached method uses current instance state");
        Check(cache.Method(type, "Absent") == null, "missing method remains missing");
        var aliases = new EvidenceAliases([new() { Original = "α", Current = "Value" }]);
        Check((int)cache.Read(state, type, aliases.Current("α")) == -1, "aliases resolve before metadata cache lookup");
        aliases = new EvidenceAliases([new() { Original = "α", Current = "Shadow" }]);
        Check((int)cache.Read(state, type, aliases.Current("α")) == 9, "changed alias does not reuse previous member");
        var a = DifferentAssembly("ObservationA", 51); var b = DifferentAssembly("ObservationB", 53);
        Check((int)cache.Read(a, a.GetType(), "Value") == 51 && (int)cache.Read(b, b.GetType(), "Value") == 53, "same type name in different client assemblies cannot share metadata");
        Check(cache.Fields(type).SequenceEqual(OriginalFields(type, typeof(object))), "field hierarchy preserves original ordering and private base fields");
        Check(new ObservationMetadata(typeof(Base)).Fields(type).All(f => f.DeclaringType == type), "UI field traversal preserves excluded base boundary");
        Check(cache.InstanceFields(type).SequenceEqual(type.GetFields(Instance)), "nested wrapper fields keep the original flat reflection rules");
        var temporary = ReadTemporary(cache);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Check(!temporary.IsAlive, "metadata cache does not retain observed objects");
        Parallel.For(0, 1000, n => {
            var value = new State { Value = n };
            if ((int)cache.Read(value, type, "Value") != n || cache.Fields(type).Length == 0) throw new Exception("Concurrent metadata read changed state");
        });
        cases.Add("concurrent observation callbacks retain independent values");

        // A synthetic CLR microbenchmark only, not a Unity/FPS measurement.
        const int iterations = 100000;
        long Work(bool cached, int count)
        {
            long sum = 0;
            for (int i = 0; i < count; i++)
            {
                state.Value = i;
                sum += (int)(cached ? cache.Read(state, type, "hidden") : OriginalRead(state, type, "hidden"));
                sum += (int)(cached ? cache.Read(state, type, "Value") : OriginalRead(state, type, "Value"));
                sum += (cached ? cache.Fields(type) : OriginalFields(type, typeof(object))).Length;
            }
            return sum;
        }
        Work(false, 2000); Work(true, 2000);
        (long Checksum, double Milliseconds, long Bytes) Measure(bool cached)
        {
            long allocated = GC.GetAllocatedBytesForCurrentThread(); var watch = Stopwatch.StartNew();
            long checksum = Work(cached, iterations); watch.Stop();
            return (checksum, watch.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - allocated);
        }
        var baseline = Measure(false); var optimized = Measure(true);
        Check(baseline.Checksum == optimized.Checksum, "cached reflection benchmark matches changing-state baseline exactly");
        Check(optimized.Bytes < baseline.Bytes, "cached reflection reduces temporary allocation in repeated observations");
        DailyJson.Write(Path.Combine(root, "observation-performance.json"), new {
            iterations, runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            baseline = new { baseline.Milliseconds, baseline.Bytes, baseline.Checksum },
            optimized = new { optimized.Milliseconds, optimized.Bytes, optimized.Checksum },
            realGameTouched = false, caveat = "Synthetic metadata benchmark; not a game frame-rate comparison."
        });
    }
}
