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
    internal static void Prepare(Window window, bool toolWindow = false)
    {
        if ((bool)window.GetValue(PreparedProperty)) return;
        window.SetValue(PreparedProperty, true);
        window.Style = (Style)Application.Current.FindResource(typeof(Window));
        window.SetResourceReference(Window.BackgroundProperty, toolWindow ? "AppBackground" : "Surface");
        window.SetResourceReference(Window.ForegroundProperty, "Ink");
        window.ShowInTaskbar = toolWindow;
        WindowChrome.SetWindowChrome(window, new WindowChrome {
            CaptionHeight = toolWindow ? 80 : 48, ResizeBorderThickness = new Thickness(window.ResizeMode == ResizeMode.NoResize ? 0 : 6),
            GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(8), UseAeroCaptionButtons = false
        });
        var body = window.Content; window.Content = null;
        var layout = new DockPanel();
        var header = new DockPanel { Name = "WindowHeader", LastChildFill = true,
            Height = toolWindow ? 80 : 48, Margin = new Thickness(toolWindow ? 24 : 20, 0, 12, 0) };
        var close = new Button { Name = "WindowClose", Style = (Style)Application.Current.FindResource("CloseCaptionButton"),
            VerticalAlignment = VerticalAlignment.Center, TabIndex = 2 };
        var glyph = new System.Windows.Shapes.Path { Data = Geometry.Parse("M 4,4 L 12,12 M 12,4 L 4,12"),
            Width = 16, Height = 16, StrokeThickness = 1.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        glyph.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, new Binding(nameof(Control.Foreground)) { Source = close }); close.Content = glyph;
        DailyLanguage.Current.Bind(close, ToolTipService.ToolTipProperty, "window.close");
        DailyLanguage.Current.Bind(close, AutomationProperties.NameProperty, "window.close");
        WindowChrome.SetIsHitTestVisibleInChrome(close, true); close.Click += (_, _) => window.Close();
        DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        if (toolWindow)
        {
            Button Caption(string key, string path, Action action)
            {
                var button = new Button { Name = key == "window.maximize" ? "WindowMaximize" : "WindowMinimize",
                    Style = (Style)Application.Current.FindResource("CaptionButton"), VerticalAlignment = VerticalAlignment.Center,
                    TabIndex = key == "window.maximize" ? 1 : 0 };
                var icon = new System.Windows.Shapes.Path { Data=Geometry.Parse(path), Width=16, Height=16, StrokeThickness=1.2 };
                icon.SetBinding(System.Windows.Shapes.Shape.StrokeProperty,new Binding(nameof(Control.Foreground)){Source=button}); button.Content=icon;
                DailyLanguage.Current.Bind(button,ToolTipService.ToolTipProperty,key);
                DailyLanguage.Current.Bind(button,AutomationProperties.NameProperty,key);
                WindowChrome.SetIsHitTestVisibleInChrome(button,true);button.Click+=(_,_)=>action();
                DockPanel.SetDock(button,Dock.Right);header.Children.Add(button);return button;
            }
            var maximize=Caption("window.maximize","M 3,3 L 13,3 13,13 3,13 Z",()=>{
                if(window.WindowState==WindowState.Maximized)SystemCommands.RestoreWindow(window);else SystemCommands.MaximizeWindow(window);
            });
            window.StateChanged+=(_,_)=>{
                string key=window.WindowState==WindowState.Maximized?"window.restore":"window.maximize";
                DailyLanguage.Current.Bind(maximize,ToolTipService.ToolTipProperty,key);
                DailyLanguage.Current.Bind(maximize,AutomationProperties.NameProperty,key);
                ((System.Windows.Shapes.Path)maximize.Content).Data = Geometry.Parse(window.WindowState == WindowState.Maximized
                    ? "M 5,3 L 13,3 13,11 M 3,5 L 11,5 11,13 3,13 Z" : "M 3,3 L 13,3 13,13 3,13 Z");
            };
            maximize.IsEnabled = window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip;
            Caption("window.minimize","M 3,8 L 13,8",()=>SystemCommands.MinimizeWindow(window));
        }
        var title = new TextBlock { Name = "WindowHeading", FontSize = toolWindow ? 26 : 14,
            VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 12, 0) };
        if (toolWindow && ToolWindowDesign.Id(window) is { } id)
        {
            DailyLanguage.Current.Bind(title, TextBlock.TextProperty, "tool." + id);
            ToolWindowDesign.ApplyActions(window);
        }
        else title.SetBinding(TextBlock.TextProperty, new Binding(nameof(Window.Title)) { Source = window });
        title.SetBinding(ToolTipService.ToolTipProperty, new Binding(nameof(TextBlock.Text)) { Source = title });
        header.Children.Add(title); DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);
        layout.Children.Add(new ContentPresenter { Content = body });
        var frame = new Border { Child = layout, BorderThickness = new Thickness(1) };
        frame.SetResourceReference(Border.BorderBrushProperty, "Line");
        frame.SetResourceReference(Border.BackgroundProperty, toolWindow ? "AppBackground" : "Surface");
        window.Content = frame;
        if (toolWindow)
        {
            void Insets(object? sender, EventArgs args) => MainWindow.UpdateWindowInsets(window, frame);
            window.SourceInitialized += Insets;
            window.StateChanged += Insets;
            window.SizeChanged += (_, _) => MainWindow.UpdateWindowInsets(window, frame);
        }
        // Escape belongs to a tool's dropdown or editor, and must not terminate automation.
        if (!toolWindow) window.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; window.Close(); } };
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
    internal static Window Message(Window owner, string title, string text, bool confirmation, string? acceptKey = null, bool safeDefault = false)
    {
        var dialog = new Window { Owner = owner, Title = title, Width = 520, MaxHeight = Math.Max(300, SystemParameters.WorkArea.Height - 64),
            SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var body = new DockPanel { Margin = new Thickness(24, 8, 24, 24) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
        if (confirmation)
        {
            var cancel = new Button { IsCancel = true, IsDefault = safeDefault, Margin = new Thickness(0, 0, 10, 0) };
            DailyLanguage.Current.Bind(cancel, ContentControl.ContentProperty, "common.cancel"); actions.Children.Add(cancel);
        }
        var accept = new Button { IsDefault = !safeDefault, Style = (Style)Application.Current.FindResource("PrimaryButton") };
        DailyLanguage.Current.Bind(accept, ContentControl.ContentProperty, acceptKey ?? (confirmation ? "common.confirm" : "updates.got_it"));
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
