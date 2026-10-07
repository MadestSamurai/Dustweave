using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Dustweave;

// Per-user Task Scheduler registration. No stored password, elevation or machine settings.
public static class DailyWindowsSchedule
{
    public static string TaskName => "Dustweave-" + WindowsIdentity.GetCurrent().User!.Value;
    public static string Register(DailySchedulePlan plan, string executable, bool validateOnly = false)
    {
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
            trigger = task.Triggers.Create(3); // weekly, including all seven days for daily schedules
            trigger.DaysOfWeek = (short)plan.Days.Aggregate(0, (mask, d) => mask | (1 << d));
            trigger.WeeksInterval = 1;
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
