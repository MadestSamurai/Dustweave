using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
namespace Dustweave.Desktop;

internal sealed class DailyResourceReviewWindow : Window
{
    private static DailyLanguage L => DailyLanguage.Current;
    internal Func<bool>? BeforeSave { get; init; }
    private readonly DailyResourceReview store;
    private readonly DailyPreferenceStore preferences;
    private readonly ComboBox accounts = new() { DisplayMemberPath = "Name", SelectedValuePath = "AccountKey", MinWidth = 220 };
    private readonly CheckBox boxes = Check("prefs.trade_boxes"), refill = Check("prefs.potion_refill"), cocktail = Check("prefs.mail_preserve_cocktail");
    private readonly TextBox target = new() { Width = 130, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 0) };
    private readonly Dictionary<string, DailyResourceReview.Options> edits = [];
    private string current = "";
    private bool loading, changed;
    internal bool Saved { get; private set; }
    internal bool Acknowledged { get; private set; }
    internal DailyResourceReviewWindow(string root, DailyAccount[] rows, string selected)
    {
        store = new(root); preferences = new(root);
        Width = 580; MaxHeight = SystemParameters.WorkArea.Height - 64;
        SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        L.Bind(this, TitleProperty, "resource_review.title");
        var panel = new StackPanel { Margin = new(24, 12, 24, 24) };
        panel.Children.Add(Text("resource_review.intro", true));
        panel.Children.Add(accounts); accounts.Margin = new(0, 8, 0, 12);
        panel.Children.Add(boxes); panel.Children.Add(Text("prefs.trade_boxes_hint", true));
        panel.Children.Add(new Separator { Margin = new(0, 14, 0, 14) });
        panel.Children.Add(refill); panel.Children.Add(Text("resource_review.potions", true));
        var amount = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 8, 0, 0) };
        var label = Text("prefs.potion_target"); label.VerticalAlignment = VerticalAlignment.Center; label.Margin = new(0, 0, 12, 0);
        amount.Children.Add(label); amount.Children.Add(target); panel.Children.Add(amount);
        L.Bind(target, System.Windows.Automation.AutomationProperties.NameProperty, "prefs.potion_target");
        panel.Children.Add(new Separator { Margin = new(0, 14, 0, 14) });
        panel.Children.Add(cocktail); panel.Children.Add(Text("resource_review.mail", true));
        status.SetResourceReference(TextBlock.ForegroundProperty, "Error"); panel.Children.Add(status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 20, 0, 0) };
        var keep = new Button { Margin = new(0, 0, 8, 0) }; L.Bind(keep, ContentControl.ContentProperty, "resource_review.keep");
        var save = new Button(); save.SetResourceReference(StyleProperty, "PrimaryButton"); L.Bind(save, ContentControl.ContentProperty, "resource_review.save");
        keep.Click += (_, _) => { Acknowledged = true; Close(); }; save.Click += (_, _) => Save(); actions.Children.Add(keep); actions.Children.Add(save); panel.Children.Add(actions);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        foreach (var check in new[] { boxes, refill, cocktail })
        { check.Checked += (_, _) => Changed(); check.Unchecked += (_, _) => Changed(); }
        target.TextChanged += (_, _) => Changed();
        accounts.ItemsSource = rows; accounts.SelectedValue = rows.Any(r => r.AccountKey == selected) ? selected : rows.First().AccountKey;
        accounts.SelectionChanged += (_, _) =>
        {
            if (loading) return;
            if (!Remember()) { loading = true; accounts.SelectedValue = current; loading = false; return; }
            Load();
        };
        Load();
    }
    private static CheckBox Check(string key)
    {
        var c = new CheckBox { Margin = new(0, 4, 0, 4), Content = Text(key) }; return c;
    }
    private static TextBlock Text(string key, bool muted = false)
    {
        var t = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 0) };
        t.SetResourceReference(TextBlock.ForegroundProperty, muted ? "MutedInk" : "Ink"); L.Text(t, key); return t;
    }
    private void Changed() { target.IsEnabled = refill.IsChecked == true; if (!loading) changed = true; }
    private void Load()
    {
        loading = true;
        try
        {
            current = (string)accounts.SelectedValue;
            var value = edits.GetValueOrDefault(current) ?? DailyResourceReview.Options.From(preferences.Read(current));
            boxes.IsChecked = value.OpenBoxes; refill.IsChecked = value.Refill; target.Text = value.Target.ToString(CultureInfo.InvariantCulture); cocktail.IsChecked = value.PreserveCocktail;
            changed = false; status.Text = "";
        }
        finally { loading = false; }
    }
    private bool Remember()
    {
        if (!changed || current.Length == 0) return true;
        try
        {
            if (!long.TryParse(target.Text, NumberStyles.None, CultureInfo.InvariantCulture, out long count)) throw new InvalidDataException();
            var value = new DailyResourceReview.Options(boxes.IsChecked == true, refill.IsChecked == true, count, cocktail.IsChecked == true);
            value.Apply(preferences.Read(current)); edits[current] = value; return true;
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentException or OverflowException)
        { L.Text(status, "resource_review.invalid"); target.Focus(); return false; }
    }
    private void Save()
    {
        if (!Remember()) return;
        if (BeforeSave != null && !BeforeSave()) return;
        try { store.Save(edits); Saved = true; Acknowledged = true; Close(); }
        catch (Exception e) { L.Text(status, "resource_review.save_failed"); status.ToolTip = e.Message; }
    }
    internal void VerifyForSmoke()
    {
        var rows = ((DailyAccount[])accounts.ItemsSource);
        string a = rows[0].AccountKey, b = rows[1].AccountKey;
        if (boxes.IsChecked == true) throw new Exception("Box opening must start disabled");
        boxes.IsChecked = true; target.Text = "6000"; cocktail.IsChecked = true;
        accounts.SelectedValue = b;
        if (boxes.IsChecked == true) throw new Exception("Account drafts leaked");
        target.Text = "invalid"; Save();
        if (Saved || !IsVisible) throw new Exception("Invalid potion count closed reminder");
        target.Text = "5000"; accounts.SelectedValue = a;
        if (boxes.IsChecked != true || target.Text != "6000" || cocktail.IsChecked != true) throw new Exception("Account draft was lost");
        Save();
        if (!Saved || !Acknowledged || !preferences.Read(a).Trade.OpenBoxesBeforeTrade || !preferences.Read(a).Stages.PreserveBloodCocktailMail ||
            preferences.Read(a).General.TalentPotionTarget != 6000 || preferences.Read(b).Trade.OpenBoxesBeforeTrade)
            throw new Exception("Reminder did not save each account correctly");
    }
}
