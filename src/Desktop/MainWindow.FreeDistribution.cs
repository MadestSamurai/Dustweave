using System.Windows;

namespace Dustweave.Desktop;

public partial class MainWindow
{
    private DailyFreeDistributionAcknowledgement freeDistributionAcknowledgement = null!;
    private void InitializeFreeDistributionNotice()
    {
        byte[]? previewReceipt = null;
        freeDistributionAcknowledgement = smoke == null ? new()
            : new(() => previewReceipt, value => previewReceipt = value.ToArray());
        RefreshFreeDistributionNotice();
    }
    private void RefreshFreeDistributionNotice() => FreeDistributionNotice.Visibility =
        freeDistributionAcknowledgement.IsAcknowledged ? Visibility.Collapsed : Visibility.Visible;

    private void CloseFreeDistributionNotice_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new FreeDistributionWindow(this, freeDistributionAcknowledgement);
        DailyDialogs.ShowModal(dialog);
        if (dialog.Confirmed) RefreshFreeDistributionNotice();
    }
}
