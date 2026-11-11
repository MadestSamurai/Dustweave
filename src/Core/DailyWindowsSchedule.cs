using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Dustweave;

// Per-user Task Scheduler registration. No stored password, elevation or machine settings.
public static class DailyWindowsSchedule
{
    public static string TaskName => "Dustweave-" + WindowsIdentity.GetCurrent().User!.Value;
    public static DailySchedulePathResult RepairPath(DailySchedulePlan plan, string executable, Func<bool> cancelled)
    {
        DailySandbox.RequireHost();
        if (!plan.Enabled || !plan.IsDaily || cancelled()) return new("skipped");
        dynamic? service = null, folder = null;
        try
        {
            service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!);
            service!.Connect(); folder = service.GetFolder("\\");
            string? Read()
            {
                dynamic? registered = null;
                try { registered = folder.GetTask(TaskName); return (string)registered.Xml; }
                catch (COMException e) when ((e.HResult & 0xffff) is 2 or 3) { return null; }
                finally { if (registered != null && Marshal.IsComObject(registered)) Marshal.FinalReleaseComObject(registered); }
            }
            void Update(string expected, string replacement)
            {
                if (cancelled()) throw new OperationCanceledException();
                if (!File.Exists(executable)) throw new FileNotFoundException("Scheduled launch executable is missing.", executable);
                if (Read() != expected) throw new InvalidOperationException("Scheduled task changed during path repair; retry from the schedule page.");
                dynamic? registered = null;
                try { registered = folder.RegisterTask(TaskName, replacement, 4, WindowsIdentity.GetCurrent().User!.Value, null, 3, null); }
                finally { if (registered != null && Marshal.IsComObject(registered)) Marshal.FinalReleaseComObject(registered); }
            }
            return DailySchedulePathRepair.Repair(plan, executable, WindowsIdentity.GetCurrent().User!.Value, Read, Update);
        }
        catch (Exception error) { return new("failed", Current: executable, Error: error.ToString()); }
        finally
        {
            foreach (object? obj in new object?[] { folder, service })
                if (obj != null && Marshal.IsComObject(obj)) Marshal.FinalReleaseComObject(obj);
        }
    }
    public static string Register(DailySchedulePlan plan, string executable, bool validateOnly = false)
    {
        DailySandbox.RequireHost();
        dynamic? service = null, folder = null, task = null, trigger = null, action = null, registered = null;
        try
        {
            service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!);
            service!.Connect(); folder = service.GetFolder("\\");
            if (!plan.Enabled)
            {
                try { if (!validateOnly) folder.DeleteTask(TaskName, 0); }
                catch (COMException e) when ((e.HResult & 0xffff) == 2) { }
                return "";
            }
            plan.Validate();
            if (!plan.IsDaily) throw new InvalidDataException("schedule.unreadable");
            if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable)) throw new InvalidDataException("schedule.executable_missing");
            task = service.NewTask(0);
            task.RegistrationInfo.Description = "Dustweave scheduled account queue";
            task.Principal.UserId = WindowsIdentity.GetCurrent().User!.Value;
            task.Principal.LogonType = 3; // InteractiveToken: the user must be signed in.
            task.Principal.RunLevel = 0;
            task.Settings.Enabled = true;
            task.Settings.StartWhenAvailable = true;
            task.Settings.DisallowStartIfOnBatteries = false;
            task.Settings.StopIfGoingOnBatteries = false;
            task.Settings.MultipleInstances = 2; // IgnoreNew
            task.Settings.ExecutionTimeLimit = "PT0S";
            trigger = task.Triggers.Create(2); // TASK_TRIGGER_DAILY
            trigger.DaysInterval = 1;
            trigger.StartBoundary = DateTime.Today.AddHours(plan.Hour).AddMinutes(plan.Minute).ToString("yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            trigger.Enabled = true;
            action = task.Actions.Create(0);
            action.Path = executable;
            action.Arguments = "--scheduled";
            action.WorkingDirectory = Path.GetDirectoryName(executable);
            string xml = task.XmlText;
            if (!validateOnly) registered = folder.RegisterTaskDefinition(TaskName, task, 6, task.Principal.UserId, null, 3, null);
            return xml;
        }
        finally
        {
            foreach (object? obj in new object?[] { registered, action, trigger, task, folder, service })
                if (obj != null && Marshal.IsComObject(obj)) Marshal.FinalReleaseComObject(obj);
        }
    }
}
