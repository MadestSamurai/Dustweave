using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
namespace Dustweave.Desktop;
internal static class DailyDownloadLinks
{
    public const string Release="https://github.com/MadestSamurai/Dustweave/releases/latest";
    public const string Mirror="https://pan.quark.cn/s/0ee553c60c0d?pwd=5AgC";
    public static FrameworkElement Create()
    {
        var l=DailyLanguage.Current;var panel=new WrapPanel{Margin=new(0,0,0,14)};
        var feedback=new TextBlock{TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center};
        void Link(string key,string url) {
            var b=new Button{Tag=url,Margin=new(0,0,8,6)};l.Bind(b,ContentControl.ContentProperty,key);b.ToolTip=url;
            b.Click+=(_,_)=>{try{Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}catch(Exception e){DailyUiText.Error(feedback,e);}};panel.Children.Add(b);
        }
        Link("updates.release_link",Release);Link("updates.mirror_link",Mirror);
        var code=new TextBlock{Margin=new(0,0,10,6),VerticalAlignment=VerticalAlignment.Center};l.Text(code,"updates.mirror_code");panel.Children.Add(code);
        var copy=new Button{Margin=new(0,0,8,6)};l.Bind(copy,ContentControl.ContentProperty,"updates.mirror_copy");
        copy.Click+=(_,_)=>{try{Clipboard.SetText("5AgC");l.Text(feedback,"updates.mirror_copied");}catch{l.Text(feedback,"diagnostics.copy_failed");}};
        panel.Children.Add(copy);panel.Children.Add(feedback);return panel;
    }
}
