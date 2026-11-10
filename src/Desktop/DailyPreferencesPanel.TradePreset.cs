using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
namespace Dustweave.Desktop;

public sealed partial class DailyPreferencesPanel
{
    private void AddTradePresetButtons(Panel parent)
    {
        var row = new WrapPanel { Margin = new(0, 0, 0, 12) };
        var import = LocalButton("tradepreset.import"); import.Name = "ImportTradePreset";
        var export = LocalButton("tradepreset.export"); export.Name = "ExportTradePreset";
        import.Margin = new(0, 0, 8, 4); export.Margin = new(0, 0, 0, 4);
        import.Click += (_, _) =>
        {
            if (account.Length == 0 || !SavePending()) return;
            var picker = new OpenFileDialog { Title = L.Get("tradepreset.import"), Filter = L.Get("tradepreset.filter") };
            if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
            try
            {
                var preset = DailyTradePreset.Read(picker.FileName);
                string Switch(bool value) => L.Get(value ? "tradepreset.on" : "tradepreset.off");
                string message = L.Get("tradepreset.confirm", accounts.Text,
                    preset.Trade.CookingRecipes.Length, preset.Trade.Sales.Count(s => s.Enabled),
                    DailyData.N(preset.Budget["reserve_gold"]).ToString("N0"),
                    preset.Budget["max_spend"] == null ? L.Get("tradepreset.unlimited") : DailyData.N(preset.Budget["max_spend"]).ToString("N0"),
                    Switch(preset.Trade.OpenBoxesBeforeTrade), Switch(preset.Trade.RefillCookingPotions));
                if (DailyDialogs.ShowModal(DailyDialogs.Message(Window.GetWindow(this), L.Get("tradepreset.import"), message, true)) != true) return;
                ImportTradePreset(preset);
                L.Text(status, "tradepreset.imported");
            }
            catch (Exception e) { DailyUiText.Error(status, e, "tradepreset.failed"); }
        };
        export.Click += (_, _) =>
        {
            if (account.Length == 0 || !SavePending()) return;
            var picker = new SaveFileDialog { Title = L.Get("tradepreset.export"), Filter = L.Get("tradepreset.filter"), FileName = "Dustweave-trade.json", DefaultExt = ".json", AddExtension = true };
            if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
            try { DailyTradePreset.Capture(dataRoot, account).Export(picker.FileName); L.Text(status, "tradepreset.exported"); }
            catch (Exception e) { DailyUiText.Error(status, e, "tradepreset.failed"); }
        };
        row.Children.Add(import); row.Children.Add(export); parent.Children.Add(row);
    }

    private void ImportTradePreset(DailyTradePreset preset)
    {
        if (!SavePending()) throw new InvalidOperationException(L.Get("prefs.autosave_failed"));
        preset.Apply(dataRoot, account);
        ReloadCurrent();
    }

    internal void TradePresetProbeForSmoke()
    {
        if (allowGame) throw new InvalidOperationException("Isolated preferences required");
        string first = new('c', 64), second = new('d', 64);
        var source = new DailyPreferences();
        source.Trade.FavoritesOnly = true; source.Trade.CookingRecipes = [123, 456];
        source.Trade.Sales = [new(789, true, 45)]; source.Trade.OpenBoxesBeforeTrade = true;
        store.Save(first, source);
        DailyJson.Write(DailyTradePlan.SettingsPath(dataRoot, first), new { reserve_gold = 50000, max_spend = 80000 });
        SetAccounts([new(1, "Source", first, "***001", true, true, ""), new(2, "Target", second, "***002", true, false, "")]);
        string path = Path.Combine(dataRoot, "trade-preset-smoke.json");
        DailyTradePreset.Capture(dataRoot, first).Export(path);
        accounts.SelectedValue = second;
        ImportTradePreset(DailyTradePreset.Read(path));
        if (account != second || tradeFavorites.IsChecked != true || !cookingButton.IsEnabled || tradeBoxes.IsChecked != true
            || tradeReserve.Text != "50000" || tradeMaximum.Text != "80000" || !loaded!.Trade.CookingRecipes.SequenceEqual(new long[] {123,456}))
            throw new Exception("Trade preset import did not refresh the target account UI");
        tradeFavorites.IsChecked = false;
        if (!Save() || DailyData.N(DailyTradePlan.ReadSettings(dataRoot, second)["max_spend"]) != 80000)
            throw new Exception("Imported budget was lost when leaving favorites mode");
        accounts.SelectedValue = first;
        if (tradeFavorites.IsChecked != true || !store.Read(first).Trade.Sales.SequenceEqual(source.Trade.Sales))
            throw new Exception("Target import edited source preferences");
    }
}
