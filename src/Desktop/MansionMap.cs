using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;

namespace Dustweave.Desktop;
public sealed class MansionMap : FrameworkElement
{
    JsonObject? frame;
    public JsonObject? Frame
    {
        get => frame;
        set
        {
            frame = value;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var board = frame?["Board"];
        if (board == null)
            return;
        double w = board["Width"]!.GetValue<double>(), h = board["Height"]!.GetValue<double>();
        double cell = Math.Min(ActualWidth / (w + 1), ActualHeight / (h + 1));
        if (cell <= 0)
            return;
        double ox = (ActualWidth - w * cell) / 2, oy = (ActualHeight - h * cell) / 2;
        Brush Color(string name) => (Brush)FindResource(name);
        Point World(JsonNode p) => new(ox + (p["X"]!.GetValue<double>() / 2 + (w - 1) / 2 + .5) * cell, oy + ((h - 1) / 2 - p["Z"]!.GetValue<double>() / 2 + .5) * cell);
        foreach (var tile in board["Tiles"]!.AsArray())
        {
            double x = tile!["X"]!.GetValue<double>(), y = tile["Y"]!.GetValue<double>();
            dc.DrawRectangle(Color("Surface"), null, new Rect(ox + x * cell, oy + (h - 1 - y) * cell, cell + .5, cell + .5));
        }

        foreach (var coin in board["Coins"]!.AsArray())
            dc.DrawEllipse(Color("Warning"), null, World(coin!["Position"]!), cell * .08, cell * .08);
        foreach (var exit in board["Exits"]!.AsArray())
        {
            var p = World(exit!);
            dc.DrawRoundedRectangle(Color("PrimarySoft"), new Pen(Color("Focus"), 2), new Rect(p.X - cell * .3, p.Y - cell * .3, cell * .6, cell * .6), 3, 3);
        }

        int target = frame?["Target"]?.GetValue<int>() ?? -1;
        if (target >= 0 && target < board["Tiles"]!.AsArray().Count)
        {
            var destination = World(board["Tiles"]![target]!["World"]!);
            var pen = new Pen(Color("Primary"), 1.5)
            {
                DashStyle = DashStyles.Dot
            };
            dc.DrawLine(pen, World(board["Player"]!), destination);
        }

        foreach (var enemy in board["Enemies"]!.AsArray())
        {
            var p = World(enemy!["Position"]!);
            dc.DrawEllipse(Color("Error"), null, p, cell * .25, cell * .25);
        }

        var player = World(board["Player"]!);
        double yaw = (frame?["Camera"]?["Yaw"]?.GetValue<double>() ?? 0) * Math.PI / 180;
        dc.DrawEllipse(Color("Primary"), new Pen(Color("Surface"), 2), player, cell * .32, cell * .32);
        dc.DrawLine(new Pen(Color("Primary"), 2), player, new Point(player.X + Math.Sin(yaw) * cell * .6, player.Y - Math.Cos(yaw) * cell * .6));
    }
}
