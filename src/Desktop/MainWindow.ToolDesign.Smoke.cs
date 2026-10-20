using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;

namespace Dustweave.Desktop;

public partial class MainWindow
{
    private void CheckToolChromeForSmoke(Window window, string id, Action<bool, string> check)
    {
        static IEnumerable<FrameworkElement> Elements(DependencyObject node)
        {
            if (node is FrameworkElement element) yield return element;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                foreach (var child in Elements(VisualTreeHelper.GetChild(node, i))) yield return child;
        }
        var visible = Elements(window).Where(e => e.IsVisible).ToArray();
        var title = visible.OfType<TextBlock>().Single(t => t.Name == "WindowHeading");
        check(title.Text == L.Get("tool." + id) && title.FontSize == 26, id + ": shared localized large heading");
        check(window.Title==title.Text+" · Dustweave",id+": taskbar and window title share the same product name");
        check(visible.OfType<TextBlock>().Count(t => t.Text == title.Text && t.FontSize >= 22) == 1, id + ": one heading, without duplicate legacy header");
        check(!visible.Any(e => e.Name is "SourceNoticeText" or "SourceNoticeButton" or "Attribution" or "AboutButton"), id + ": no standalone source banner");
        check(!visible.Any(e => e.Name is "LanguageBox" or "LanguageChoice"), id + ": language is selected once in Dustweave");
        var captions = visible.OfType<Button>().Where(b => b.Name is "WindowClose" or "WindowMaximize" or "WindowMinimize").ToArray();
        check(captions.Length == 3, id + ": all caption controls present");
        double middle = title.TranslatePoint(new Point(0, title.ActualHeight / 2), window).Y;
        foreach (var button in captions)
        {
            var point = button.TranslatePoint(new Point(button.ActualWidth / 2, button.ActualHeight / 2), window);
            check(Math.Abs(point.Y - middle) < 1.1 && button.ActualHeight >= 36 && WindowChrome.GetIsHitTestVisibleInChrome(button),
                id + ": " + button.Name + " aligned with title, accessible and outside loading lock");
        }
        double headingRight = title.TranslatePoint(new Point(title.ActualWidth,0),window).X;
        check(headingRight <= captions.Min(b=>b.TranslatePoint(new Point(),window).X), id + ": title never overlaps caption controls");
        check(window.Background == Application.Current.Resources["AppBackground"], id + ": shared window surface follows theme");
        foreach(string name in new[]{"StartButton","StopButton","PauseButton","ConnectButton","AutoButton"})
        {
            if(window.FindName(name) is not Button {IsVisible:true} action)continue;
            var position=action.TranslatePoint(new Point(),window);
            check(position.X>=0 && position.Y>=0 && position.X+action.ActualWidth<=window.ActualWidth+1
                && position.Y+action.ActualHeight<=window.ActualHeight+1,id+": "+name+" fits the window");
            string label = name switch { "StartButton" => "tools.action_start", "StopButton" or "PauseButton" => "tools.action_stop",
                "AutoButton" => "tools.action_round", _ => id=="equipment"?"tools.refresh_inventory":"tools.action_connect" };
            check(action.Content?.ToString()==L.Get(label),id+": "+name+" uses shared translated wording");
        }
        var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, Key.Escape)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        window.RaiseEvent(key);
        check(window.IsVisible&&!key.Handled,id+": Escape does not close a running tool");
    }
}
