using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Dustweave.Desktop;

// Keeps the native ProgressBar range/accessibility contract while drawing a circular track.
public sealed class ProgressRingConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        double fraction = 0;
        if (values.Length >= 3 && values[0] is double value && values[1] is double minimum && values[2] is double maximum
            && double.IsFinite(value) && double.IsFinite(minimum) && double.IsFinite(maximum) && maximum > minimum)
            fraction = Math.Clamp((value - minimum) / (maximum - minimum), 0, 1);
        if (parameter as string == "text")
            return Math.Floor(fraction * 100 + 1e-9).ToString("0", CultureInfo.InvariantCulture) + "%";
        if (fraction <= 0) return Geometry.Empty;
        Geometry geometry;
        if (fraction >= 1)
            geometry = new EllipseGeometry(new Point(36, 36), 31, 31);
        else
        {
            double angle = fraction * 2 * Math.PI;
            var arc = new StreamGeometry();
            using (var context = arc.Open())
            {
                context.BeginFigure(new Point(36, 5), false, false);
                context.ArcTo(new Point(36 + 31 * Math.Sin(angle), 36 - 31 * Math.Cos(angle)),
                    new Size(31, 31), 0, fraction > .5, SweepDirection.Clockwise, true, false);
            }
            geometry = arc;
        }
        geometry.Freeze();
        return geometry;
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
