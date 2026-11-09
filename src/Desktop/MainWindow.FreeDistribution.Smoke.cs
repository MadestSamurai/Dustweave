using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Dustweave.Desktop;

public partial class MainWindow
{
    private void CheckFreeDistributionVisibleForSmoke()
    {
        if (!FreeDistributionNotice.IsVisible || FreeDistributionNotice.Opacity != 1
            || FreeDistributionTitle.Text != L.Get("distribution.free_title")
            || FreeDistributionRefund.Text != L.Get("distribution.refund"))
            throw new Exception("Free-software/refund notice is missing, hidden or not localized");
        foreach (var label in new[] { FreeDistributionTitle, FreeDistributionRefund })
        {
            var at = label.TranslatePoint(new Point(), WindowRoot);
            if (label.ActualWidth < 100 || label.ActualHeight < 10 || label.TextWrapping != TextWrapping.Wrap
                || at.X < 0 || at.Y < 0 || at.X + label.ActualWidth > WindowRoot.ActualWidth + 1
                || at.Y + label.ActualHeight > WindowRoot.ActualHeight + 1)
                throw new Exception("Free-software/refund notice is clipped");
        }
    }

    private async Task CheckFreeDistributionForSmoke()
    {
        if (sessions is not DemoEnvironment) throw new Exception("Free notice smoke requires isolated sessions");
        string locale = L.Code;
        int theme = ThemeSelector.SelectedIndex;
        double width = Width, height = Height;
        object selected = WorkspaceTabs.SelectedItem;
        var originalStore = freeDistributionAcknowledgement;
        int checks = 0;
        void Check(bool ok, string error) { if (!ok) throw new Exception(error); checks++; }
        void ExerciseDialog(Action<FreeDistributionWindow> action)
        {
            Exception? failure = null;
            Dispatcher.BeginInvoke(new Action(() => {
                FreeDistributionWindow? dialog = null;
                try
                {
                    dialog = Application.Current.Windows.OfType<FreeDistributionWindow>().Single(w => w.Owner == this);
                    action(dialog);
                }
                catch (Exception error) { failure = error; }
                finally { if (dialog?.IsVisible == true) dialog.Close(); }
            }), DispatcherPriority.ApplicationIdle);
            CloseFreeDistributionNoticeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (failure != null) throw failure;
        }
        try
        {
            foreach (string code in DailyLanguage.Codes)
            foreach (int mode in new[] { 1, 2 })
            {
                L.Select(code); ThemeSelector.SelectedIndex = mode;
                byte[]? receipt = null;
                freeDistributionAcknowledgement = new(() => receipt, value => receipt = value.ToArray());
                RefreshFreeDistributionNotice();
                Width = 920; Height = 650;
                foreach (var tab in new[] { RunTab, AccountsTab, SettingsTab, ToolsTab, SoftwareSettingsTab })
                {
                    WorkspaceTabs.SelectedItem = tab;
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    CheckFreeDistributionVisibleForSmoke(); checks++;
                }
                WorkspaceTabs.SelectedItem = RunTab;
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Capture($"free-notice-{code}-{mode}-compact");
                // Operational notices can appear/disappear and tasks can run without
                // replacing this notice or requiring an acknowledgement.
                ActionNoticeText.Text = L.Get("connection.none");
                ActionNotice.Visibility = Visibility.Visible;
                dailyPanel.Busy(true);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                CheckFreeDistributionVisibleForSmoke(); checks++;
                DismissActionNotice_Click(this, new RoutedEventArgs());
                dailyPanel.Busy(false);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                CheckFreeDistributionVisibleForSmoke(); checks++;
                Width = 1180; Height = 900;
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                CheckFreeDistributionVisibleForSmoke(); checks++;
                Capture($"free-notice-{code}-{mode}");
                ExerciseDialog(dialog => {
                    Check(!dialog.ConfirmButton.IsEnabled, "Empty input enabled dismissal");
                    dialog.ConfirmationInput.Text = "yes";
                    Check(!dialog.ConfirmButton.IsEnabled, "Incorrect input enabled dismissal");
                    dialog.ConfirmationInput.Text = DailyFreeDistributionAcknowledgement.Phrase(code);
                    Check(dialog.ConfirmButton.IsEnabled, "Localized confirmation did not enable dismissal");
                    Check(receipt == null, "Typing without confirming persisted acknowledgement");
                    Capture($"free-dismiss-dialog-{code}-{mode}", (FrameworkElement)dialog.Content);
                    // Closing/Escape/cancel does not save merely because text is correct.
                    dialog.Close();
                });
                Check(receipt == null && FreeDistributionNotice.IsVisible, "Cancelled dialog hid the notice");
                freeDistributionAcknowledgement = new(() => null, _ => throw new IOException("synthetic denied write"));
                ExerciseDialog(dialog => {
                    dialog.ConfirmationInput.Text = DailyFreeDistributionAcknowledgement.Phrase(code);
                    dialog.ConfirmButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(!dialog.Confirmed && dialog.IsVisible && dialog.Feedback.Visibility == Visibility.Visible,
                        "Failed persistence falsely closed the dialog");
                });
                Check(FreeDistributionNotice.IsVisible, "Failed persistence hid the notice");
                freeDistributionAcknowledgement = new(() => receipt, value => receipt = value.ToArray());
                ExerciseDialog(dialog => {
                    dialog.ConfirmationInput.Text = DailyFreeDistributionAcknowledgement.Phrase(code);
                    dialog.ConfirmButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(dialog.Confirmed && !dialog.IsVisible, "Valid confirmation did not close the dialog");
                });
                Check(FreeDistributionNotice.Visibility == Visibility.Collapsed, "Verified acknowledgement did not hide notice");
                freeDistributionAcknowledgement = new(() => receipt, value => receipt = value.ToArray());
                RefreshFreeDistributionNotice();
                Check(FreeDistributionNotice.Visibility == Visibility.Collapsed, "Reopened store lost acknowledgement");
                // Missing/nontransferable records show the notice again, without a gate.
                receipt = null; RefreshFreeDistributionNotice();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                CheckFreeDistributionVisibleForSmoke(); checks++;
            }
            L.Audit();
            DailyJson.Write(Path.Combine(smoke!, "free-distribution.json"), new {
                status="passed", checks, embedded=true, acknowledgementRequiredForUse=false,
                typedConfirmationToDismiss=true, verifiedPersistence=true, realGameTouched=false
            });
        }
        finally
        {
            dailyPanel.Busy(false); ActionNotice.Visibility = Visibility.Collapsed;
            freeDistributionAcknowledgement = originalStore; RefreshFreeDistributionNotice();
            L.Select(locale); ThemeSelector.SelectedIndex = theme;
            Width = width; Height = height; WorkspaceTabs.SelectedItem = selected;
        }
    }
}
