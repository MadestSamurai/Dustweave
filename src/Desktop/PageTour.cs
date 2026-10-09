using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Dustweave.Desktop;

/// <summary>Highlights the actual page and navigation item; intercepts background clicks during the tour.</summary>
internal sealed class PageTour : Adorner
{
    private static DailyLanguage L => DailyLanguage.Current;
    private readonly VisualCollection visuals;
    private readonly Canvas canvas = new();
    private readonly Border card;
    private readonly TextBlock title = new() { FontSize = 20, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock text = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 18), FontSize = 14 };
    private readonly TextBlock counter = new() { Margin = new(0, 0, 0, 8) };
    private readonly Button back = new() { Margin = new(0, 0, 8, 0) }, next = new(), skip = new() { Margin = new(0, 0, 8, 0) };
    private FrameworkElement? navigation, target;
    internal event Action<int>? Move;
    internal event Action? Skip;
    internal PageTour(UIElement owner) : base(owner)
    {
        visuals = new(this) { canvas };
        var body = new StackPanel(); counter.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk");
        body.Children.Add(counter); body.Children.Add(title); body.Children.Add(text);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        L.Bind(back, ContentControl.ContentProperty, "onboarding.back"); L.Bind(skip, ContentControl.ContentProperty, "onboarding.skip");
        next.Style = (Style)Application.Current.FindResource("PrimaryButton");
        actions.Children.Add(skip); actions.Children.Add(back); actions.Children.Add(next); body.Children.Add(actions);
        card = new() { Child = body, Padding = new(22), CornerRadius = new(12), BorderThickness = new(1) };
        card.SetResourceReference(Border.BackgroundProperty, "Surface"); card.SetResourceReference(Border.BorderBrushProperty, "Line");
        canvas.Children.Add(card);
        back.Click += (_, _) => Move?.Invoke(-1); next.Click += (_, _) => Move?.Invoke(1); skip.Click += (_, _) => Skip?.Invoke();
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Skip?.Invoke(); } };
    }
    internal void Show(FrameworkElement navigation, FrameworkElement target, string titleKey, string bodyKey, int index, int total)
    {
        this.navigation = navigation; this.target = target;
        L.Text(title, titleKey); L.Text(text, bodyKey); L.Text(counter, "onboarding.page", index + 1, total);
        back.IsEnabled = index > 0; L.Bind(next, ContentControl.ContentProperty, index == total - 1 ? "onboarding.finish" : "onboarding.next");
        InvalidateMeasure(); InvalidateVisual(); next.Focus();
    }
    private Rect Bounds(FrameworkElement? element)
    {
        if (element?.IsVisible != true) return Rect.Empty;
        try { var rect = element.TransformToAncestor(AdornedElement).TransformBounds(new Rect(element.RenderSize)); rect.Inflate(3, 3); rect.Intersect(new Rect(AdornedElement.RenderSize)); return rect; }
        catch (InvalidOperationException) { return Rect.Empty; }
    }
    protected override void OnRender(DrawingContext drawing)
    {
        var full = new RectangleGeometry(new Rect(AdornedElement.RenderSize));
        Geometry shade = full;
        foreach (var rect in new[] { Bounds(navigation), Bounds(target) }.Where(x => !x.IsEmpty))
            shade = Geometry.Combine(shade, new RectangleGeometry(rect, 9, 9), GeometryCombineMode.Exclude, null);
        drawing.DrawGeometry(TryFindResource("ModalShade") as Brush ?? new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), null, shade);
        foreach (var rect in new[] { Bounds(navigation), Bounds(target) }.Where(x => !x.IsEmpty))
            drawing.DrawRoundedRectangle(null, new Pen((Brush)FindResource("Primary"), 2), rect, 9, 9);
    }
    protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters) => new PointHitTestResult(this, hitTestParameters.HitPoint);
    protected override Size MeasureOverride(Size available)
    {
        var size = AdornedElement.RenderSize;
        card.Width = Math.Max(280, Math.Min(430, size.Width - 48));
        card.MaxHeight = Math.Max(220, size.Height - 48);
        card.Measure(new(card.Width, card.MaxHeight)); canvas.Measure(size); return size;
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        canvas.Arrange(new Rect(finalSize));
        double left = Math.Max(20, finalSize.Width - card.DesiredSize.Width - 28);
        double top = Math.Max(90, finalSize.Height - card.DesiredSize.Height - 32);
        Canvas.SetLeft(card, left); Canvas.SetTop(card, top);
        return finalSize;
    }
    protected override int VisualChildrenCount => visuals.Count;
    protected override Visual GetVisualChild(int index) => visuals[index];
}
