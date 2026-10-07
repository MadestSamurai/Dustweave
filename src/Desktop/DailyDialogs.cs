using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;

namespace Dustweave.Desktop;

// Shared chrome for application-owned dialogs. OS file pickers and startup-failure
// reporting retain their native UI, which remains available when resources cannot load.
internal static class DailyDialogs
{
    private static readonly DependencyProperty PreparedProperty = DependencyProperty.RegisterAttached(
        "Prepared", typeof(bool), typeof(DailyDialogs), new PropertyMetadata(false));
    internal static int ModalDepth { get; private set; }
    internal static void Prepare(Window window)
    {
        if ((bool)window.GetValue(PreparedProperty)) return;
        window.SetValue(PreparedProperty, true);
        window.Style = (Style)Application.Current.FindResource(typeof(Window));
        window.SetResourceReference(Window.BackgroundProperty, "Surface");
        window.SetResourceReference(Window.ForegroundProperty, "Ink");
        window.ShowInTaskbar = false;
        WindowChrome.SetWindowChrome(window, new WindowChrome {
            CaptionHeight = 48, ResizeBorderThickness = new Thickness(window.ResizeMode == ResizeMode.NoResize ? 0 : 6),
            GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(8), UseAeroCaptionButtons = false
        });
        var body = window.Content; window.Content = null;
        var layout = new DockPanel();
        var header = new DockPanel { LastChildFill = true, Margin = new Thickness(20, 6, 8, 6) };
        var close = new Button { Width = 36, Height = 36, MinWidth = 36, Padding = new Thickness(0),
            Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        var glyph = new System.Windows.Shapes.Path { Data = Geometry.Parse("M 4,4 L 12,12 M 12,4 L 4,12"),
            Width = 16, Height = 16, StrokeThickness = 1.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        glyph.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Ink"); close.Content = glyph;
        DailyLanguage.Current.Bind(close, ToolTipService.ToolTipProperty, "window.close");
        DailyLanguage.Current.Bind(close, AutomationProperties.NameProperty, "window.close");
        WindowChrome.SetIsHitTestVisibleInChrome(close, true); close.Click += (_, _) => window.Close();
        DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        var title = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 12, 0) };
        title.SetBinding(TextBlock.TextProperty, new Binding(nameof(Window.Title)) { Source = window });
        header.Children.Add(title); DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);
        layout.Children.Add(new ContentPresenter { Content = body });
        var frame = new Border { Child = layout, BorderThickness = new Thickness(1) };
        frame.SetResourceReference(Border.BorderBrushProperty, "Line");
        frame.SetResourceReference(Border.BackgroundProperty, "Surface");
        window.Content = frame;
        window.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; window.Close(); } };
    }

    internal static IDisposable Dim(Window owner)
    {
        var content = owner.Content as UIElement ?? throw new InvalidOperationException("Dialog owner has no content.");
        var layer = AdornerLayer.GetAdornerLayer(content) ?? throw new InvalidOperationException("Dialog owner has no adorner layer.");
        var shade = new Shade(content);
        layer.Add(shade); ModalDepth++;
        return new Scope(() => { layer.Remove(shade); ModalDepth--; });
    }
    internal static bool? ShowModal(Window window)
    {
        Prepare(window);
        var focused = Keyboard.FocusedElement;
        using var shade = window.Owner is { IsLoaded: true } owner ? Dim(owner) : null;
        try { return window.ShowDialog(); }
        finally { if (focused is UIElement { IsVisible: true, IsEnabled: true } previous) previous.Focus(); }
    }
    internal static Window Message(Window owner, string title, string text, bool confirmation)
    {
        var dialog = new Window { Owner = owner, Title = title, Width = 520, MaxHeight = Math.Max(300, SystemParameters.WorkArea.Height - 64),
            SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var body = new DockPanel { Margin = new Thickness(24, 8, 24, 24) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
        if (confirmation)
        {
            var cancel = new Button { IsCancel = true, Margin = new Thickness(0, 0, 10, 0) };
            DailyLanguage.Current.Bind(cancel, ContentControl.ContentProperty, "common.cancel"); actions.Children.Add(cancel);
        }
        var accept = new Button { IsDefault = true, Style = (Style)Application.Current.FindResource("PrimaryButton") };
        DailyLanguage.Current.Bind(accept, ContentControl.ContentProperty, confirmation ? "common.confirm" : "updates.got_it");
        accept.Click += (_, _) => dialog.DialogResult = true; actions.Children.Add(accept);
        DockPanel.SetDock(actions, Dock.Bottom); body.Children.Add(actions);
        body.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap } });
        dialog.Content = body; Prepare(dialog); return dialog;
    }
    private sealed class Shade(UIElement owner) : Adorner(owner)
    {
        protected override void OnRender(DrawingContext drawing)
        {
            var brush = TryFindResource("ModalShade") as Brush ?? new SolidColorBrush(Color.FromArgb(100, 0, 0, 0));
            drawing.DrawRectangle(brush, null, new Rect(AdornedElement.RenderSize));
        }
    }
    private sealed class Scope(Action release) : IDisposable { public void Dispose() => release(); }
}
