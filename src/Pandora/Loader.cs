using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using BD2.LocalIpc;

namespace Dustweave.Pandora.Runtime
{
    public static class Loader
    {
        static Handoff handoff;
        static MainThread frame;
        static object engine;
        static bool resolver;
        public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BD2DailyAssistant", "pandora");
        const string Entries = "state.json|control.json|command.json|runtime.json|error.json|receipt.json";
        public static void Load()
        {
            lock (typeof(Loader))
            {
                if (handoff == null)
                    handoff = new Handoff(typeof(Loader).Assembly.FullName, "pandora", "pandora", false, Start, Pause, Busy, Stop, Status);
                if (!handoff.IsActive && !handoff.Pending)
                    RuntimeFiles.Start(Root, Build.Fingerprint, Entries);
                handoff.Request(DateTime.UtcNow);
                if (frame == null)
                    frame = new MainThread(() =>
                    {
                        handoff.Tick(DateTime.UtcNow);
                        return handoff.Pending;
                    }, handoff.Fail);
                frame.Schedule();
            }
        }

        static void Start()
        {
            RuntimeFiles.Start(Root, Build.Fingerprint, Entries);
            if (!resolver)
            {
                AppDomain.CurrentDomain.AssemblyResolve += Resolve;
                resolver = true;
            }

            engine = typeof(Loader).Assembly.GetType("Dustweave.Pandora.Runtime.Engine", true).GetMethod("Create").Invoke(null, null);
        }

        static object Invoke(string method)
        {
            return engine.GetType().GetMethod(method).Invoke(engine, null);
        }

        static void Pause()
        {
            RuntimeFiles.Revoke();
            if (engine != null)
                Invoke("PrepareHandoff");
        }

        static string Busy()
        {
            return engine == null ? "" : (string)Invoke("HandoffBusy");
        }

        static void Stop()
        {
            if (engine != null)
                Invoke("Stop");
            engine = null;
            if (resolver)
            {
                AppDomain.CurrentDomain.AssemblyResolve -= Resolve;
                resolver = false;
            }
        }

        public static void Unload()
        {
            MainThread.Drain(() =>
            {
                if (handoff != null)
                    handoff.Unload();
            }, Status);
        }

        static Assembly Resolve(object sender, ResolveEventArgs e)
        {
            if (new AssemblyName(e.Name).Name != "0Harmony")
                return null;
            using (var s = typeof(Loader).Assembly.GetManifestResourceStream("Pandora.Harmony.dll"))
            using (var m = new MemoryStream())
            {
                s.CopyTo(m);
                return Assembly.Load(m.ToArray());
            }
        }

        static void Status(string state, string error)
        {
            if (state == "active")
                RuntimeFiles.Activate();
            var text = "{\"State\":\"" + state + "\",\"Error\":\"" + error.Replace("\\", "/").Replace("\"", "'").Replace("\n", " ").Replace("\r", " ") + "\"}";
            RuntimeFiles.Write(Path.Combine(Root, "runtime.json"), System.Text.Encoding.UTF8.GetBytes(text));
        }
    }


}
