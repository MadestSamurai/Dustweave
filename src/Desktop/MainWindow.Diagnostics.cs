using System.IO;
using System.Windows;
using System.Windows.Media;

namespace Dustweave.Desktop;

public partial class MainWindow
{
    // Presentation uses the existing connection observation. No new polling or game commands.
    private string diagnosticState = "closed", diagnosticScene = "";
    private DateTimeOffset diagnosticObserved;

    private void PresentDiagnostics(GameInstance? game, DailySnapshot? snapshot, bool fresh, bool readFailed = false)
    {
        diagnosticState = designPreview ? "preview" : readFailed ? "unreadable" : game == null ? "closed"
            : ToolRunning ? "tool" : !fresh ? snapshot == null ? "unconnected" : "stale"
            : snapshot!.State != "identified" ? "login" : "ready";
        diagnosticScene = fresh ? snapshot!.Scene : "";
        diagnosticObserved = DateTimeOffset.Now;
        L.Text(DiagnosticStatus, "diagnostics.status." + diagnosticState);
        L.Text(DiagnosticAdvice, "diagnostics.advice." + diagnosticState);
        L.Bind(DiagnosticScene, System.Windows.Controls.TextBlock.TextProperty, () =>
            string.IsNullOrEmpty(diagnosticScene) ? L.Get("diagnostics.version", DailyProductVersion.Current)
                : L.Get("diagnostics.scene_version", L.Translate(DailyUserText.Scene(diagnosticScene)), DailyProductVersion.Current));
        DiagnosticIcon.Data = (Geometry)FindResource("Icon." + (diagnosticState == "ready" ? "Check"
            : diagnosticState is "stale" or "unreadable" ? "Warning" : diagnosticState == "closed" ? "System" : "Link"));
        DiagnosticIcon.Stroke = (Brush)FindResource(diagnosticState == "ready" ? "Success"
            : diagnosticState is "stale" or "unreadable" ? "Warning" : "MutedInk");
        DiagnosticConnect.Visibility = diagnosticState is "unconnected" or "stale" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        UpdateConnection();
        L.Text(DiagnosticCopyFeedback, "diagnostics.refreshed");
        DiagnosticCopyFeedback.Foreground = (Brush)FindResource("Primary");
        DiagnosticCopyFeedback.Visibility = Visibility.Visible;
    }

    private void DiagnosticTimeline_Click(object sender, RoutedEventArgs e) => WorkspaceTabs.SelectedItem = RunTab;

    private string DiagnosticSummary() => string.Join(Environment.NewLine,
        "Dustweave " + DailyProductVersion.Current, diagnosticObserved.ToString("yyyy-MM-dd HH:mm:ss zzz"),
        L.Get("diagnostics.status." + diagnosticState), L.Get("diagnostics.advice." + diagnosticState),
        string.IsNullOrEmpty(diagnosticScene) ? "" : L.Translate(DailyUserText.Scene(diagnosticScene)));

    private void CopyDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(DiagnosticSummary());
            L.Text(DiagnosticCopyFeedback, "diagnostics.copied");
            DiagnosticCopyFeedback.Foreground = (Brush)FindResource("Primary");
        }
        catch
        {
            L.Text(DiagnosticCopyFeedback, "diagnostics.copy_failed");
            DiagnosticCopyFeedback.Foreground = (Brush)FindResource("Error");
        }
        DiagnosticCopyFeedback.Visibility = Visibility.Visible;
    }

    private void OpenDiagnosticsFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Only expose the observation journal folder, never the account/session vault.
            var path = Path.Combine(root, "live", "diagnostics");
            Directory.CreateDirectory(path);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { ShowError(ex); }
    }
}
