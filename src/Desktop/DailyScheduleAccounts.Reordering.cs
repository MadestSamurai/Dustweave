using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Dustweave.Desktop;

internal sealed partial class DailyScheduleAccounts
{
    private const string DragFormat = "Dustweave.ScheduleAccount";
    private sealed record AccountDrag(DailyScheduleAccounts Owner, string Key, string[] Order);
    private bool dragging, deferredRender;
    private string? pressedKey;
    private Point pressedAt;
    private InsertionAdorner? insertion;
    private long lastScroll;

    internal DailyScheduleAccounts()
    {
        Background = Brushes.Transparent; AllowDrop = true;
        DragOver += (_, e) => {
            e.Handled = true; e.Effects = DragDropEffects.None;
            if (LocalDrag(e.Data) == null || !IsEnabled) { ClearInsertion(); return; }
            AutoScroll(e.GetPosition);
            int index = InsertionAt(e.GetPosition(this));
            ShowInsertion(index); if (index >= 0) e.Effects = DragDropEffects.Move;
        };
        Drop += (_, e) => {
            e.Handled = true; e.Effects = DragDropEffects.None;
            if (ApplyAccountDrop(e.Data, e.GetPosition(this))) e.Effects = DragDropEffects.Move;
            ClearInsertion();
        };
        DragLeave += (_, _) => ClearInsertion();
        Unloaded += (_, _) => ClearInsertion();
        IsEnabledChanged += (_, _) => { if (!IsEnabled) { pressedKey = null; ClearInsertion(); } };
    }
    private AccountDrag? LocalDrag(IDataObject data) => dragging && data.GetDataPresent(DragFormat, false) &&
        data.GetData(DragFormat, false) is AccountDrag value && ReferenceEquals(value.Owner, this) && selected.SequenceEqual(value.Order) ? value : null;
    internal static bool CanDragFrom(DependencyObject? source, Border card)
    {
        while (source != null && source != card)
        {
            if (source is ButtonBase) return false;
            source = source is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        return source == card;
    }
    private void PrepareReorder(Border card, string name)
    {
        card.Focusable = true; card.Cursor = Cursors.SizeAll;
        L.Bind(card, ToolTipService.ToolTipProperty, "schedule.reorder_help");
        L.Bind(card, AutomationProperties.NameProperty, "schedule.reorder_account", name);
        card.GotKeyboardFocus += (_, e) => { if (e.OriginalSource == card) card.SetResourceReference(Border.BorderBrushProperty, "Focus"); };
        card.LostKeyboardFocus += (_, _) => card.SetResourceReference(Border.BorderBrushProperty, HasUnavailableCard(card) ? "Error" : "Line");
        card.PreviewMouseLeftButtonDown += (_, e) => {
            pressedKey = null;
            if (IsEnabled && CanDragFrom(e.OriginalSource as DependencyObject, card)) { pressedKey = (string)card.Tag; pressedAt = e.GetPosition(this); card.Focus(); }
        };
        card.PreviewMouseLeftButtonUp += (_, _) => pressedKey = null;
        card.MouseLeave += (_, _) => { if (!dragging) pressedKey = null; };
        card.PreviewMouseMove += (_, e) => {
            if (!IsEnabled || dragging || pressedKey != card.Tag as string || e.LeftButton != MouseButtonState.Pressed) return;
            var position = e.GetPosition(this);
            if (Math.Abs(position.X - pressedAt.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(position.Y - pressedAt.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            string key = pressedKey!; pressedKey = null;
            var data = BeginAccountDrag(key); if (data == null) return;
            card.Opacity = .55;
            try { DragDrop.DoDragDrop(card, data, DragDropEffects.Move); }
            finally { card.Opacity = 1; EndAccountDrag(key); }
        };
        card.KeyDown += (_, e) => {
            if (e.OriginalSource != card || Keyboard.Modifiers != ModifierKeys.Control || e.Key is not (Key.Left or Key.Right)) return;
            e.Handled = true; int index = selected.IndexOf((string)card.Tag);
            MoveAccount((string)card.Tag, e.Key == Key.Left ? index - 1 : index + 2);
        };
    }
    internal IDataObject? BeginAccountDrag(string key)
    {
        if (!IsEnabled || dragging || !selected.Contains(key)) return null;
        dragging = true; deferredRender = false;
        return new DataObject(DragFormat, new AccountDrag(this, key, selected.ToArray()));
    }
    internal bool ApplyAccountDrop(IDataObject data, Point point)
    {
        var payload = LocalDrag(data);
        return payload != null && MoveAccount(payload.Key, InsertionAt(point));
    }
    internal void EndAccountDrag(string key)
    {
        dragging = false; ClearInsertion();
        if (deferredRender) { deferredRender = false; Render(); }
        Children.OfType<Border>().FirstOrDefault(c => c.Tag as string == key)?.Focus();
    }
    private bool HasUnavailableCard(Border card) => !available.Any(a => a.Valid && a.AccountKey == card.Tag as string);
    internal bool MoveAccount(string key, int insertionIndex)
    {
        int from = selected.IndexOf(key);
        if (!IsEnabled || from < 0 || insertionIndex < 0 || insertionIndex > selected.Count) return false;
        int destination = insertionIndex > from ? insertionIndex - 1 : insertionIndex;
        if (destination == from) return false;
        selected.RemoveAt(from); selected.Insert(destination, key); Render();
        if (!dragging) Children.OfType<Border>().FirstOrDefault(c => c.Tag as string == key)?.Focus();
        return true;
    }
    private (Border Card, Rect Bounds)[] CardBounds() => Children.OfType<Border>().Where(c => c.Tag is string)
        .Select(c => (c, new Rect(c.TranslatePoint(new Point(), this), c.RenderSize))).ToArray();
    internal int InsertionAt(Point point)
    {
        if (!new Rect(RenderSize).Contains(point)) return -1;
        var bounds = CardBounds(); if (bounds.Length == 0) return -1;
        if (Children.OfType<Button>().Any(add => new Rect(add.TranslatePoint(new Point(), this), add.RenderSize).Contains(point))) return bounds.Length;
        var row = bounds.GroupBy(b => Math.Round(b.Bounds.Top, 1)).MinBy(r => Math.Abs(r.First().Bounds.Top + r.First().Bounds.Height / 2 - point.Y))!;
        foreach (var cell in row) if (point.X < cell.Bounds.Left + cell.Bounds.Width / 2) return selected.IndexOf((string)cell.Card.Tag);
        return selected.IndexOf((string)row.Last().Card.Tag) + 1;
    }
    internal void ShowInsertion(int index)
    {
        ClearInsertion(); var bounds = CardBounds(); if (index < 0 || index > bounds.Length || bounds.Length == 0) return;
        var layer = AdornerLayer.GetAdornerLayer(this); if (layer == null) return;
        var target = bounds[Math.Min(index, bounds.Length - 1)].Bounds;
        double x = index < bounds.Length ? target.Left + 2 : target.Right + 6;
        insertion = new InsertionAdorner(this, new Point(x, target.Top + 8), new Point(x, target.Bottom - 8)) { IsHitTestVisible = false }; layer.Add(insertion);
    }
    internal void ClearInsertion()
    {
        if (insertion == null) return;
        AdornerLayer.GetAdornerLayer(this)?.Remove(insertion); insertion = null;
    }
    private void AutoScroll(Func<IInputElement, Point> position)
    {
        if (Environment.TickCount64 - lastScroll < 70) return;
        DependencyObject? current = this;
        while ((current = VisualTreeHelper.GetParent(current)) != null)
        {
            if (current is not ScrollViewer viewer) continue;
            var point = position(viewer); double delta = point.Y < 36 ? -24 : point.Y > viewer.ActualHeight - 36 ? 24 : 0;
            if (delta != 0 && new Rect(viewer.RenderSize).Contains(point)) { lastScroll = Environment.TickCount64; viewer.ScrollToVerticalOffset(viewer.VerticalOffset + delta); viewer.UpdateLayout(); }
            break;
        }
    }
    private sealed class InsertionAdorner(UIElement owner, Point top, Point bottom) : Adorner(owner)
    {
        protected override void OnRender(DrawingContext drawing)
        {
            var brush = (Brush)FindResource("Primary"); var pen = new Pen(brush, 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            drawing.DrawLine(pen, top, bottom); drawing.DrawEllipse(brush, null, top, 3, 3); drawing.DrawEllipse(brush, null, bottom, 3, 3);
        }
    }
}
