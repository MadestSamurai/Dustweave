using System.Windows;
using System.Windows.Media;
using WpfPoint = System.Windows.Point;
namespace BD2SecretVision.Desktop;
public sealed class BoardView : FrameworkElement
{
    private Board? board;
    private static readonly Brush Claimed = new SolidColorBrush(Color.FromRgb(190,224,211));
    private static readonly Brush Wall = new SolidColorBrush(Color.FromRgb(203,210,217));
    private static readonly Brush Ground = new SolidColorBrush(Color.FromRgb(242,245,247));
    public Board? Board { get=>board; set {board=value;InvalidateVisual();} }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if(board is not {Width:>0,Height:>0,CellSize:>0} b||b.Rows.Length!=b.Height)return;
        double scale=Math.Min(ActualWidth/b.Width,ActualHeight/b.Height);
        if(scale<=0)return;
        double left=(ActualWidth-b.Width*scale)/2,top=(ActualHeight-b.Height*scale)/2;
        WpfPoint Grid(double x,double y)=>new(left+x*scale,top+(b.Height-y)*scale);
        WpfPoint World(double[] p)=>p.Length>=2?Grid(p[0]/b.CellSize+b.Width/2d,p[1]/b.CellSize+b.Height/2d):new(-10,-10);
        dc.DrawRectangle(Ground,null,new Rect(left,top,b.Width*scale,b.Height*scale));
        // Coalesce row spans instead of creating one UI element per game cell.
        for(int y=0;y<b.Height;y++){
            var row=b.Rows[y];if(row.Length!=b.Width)continue;
            for(int x=0;x<row.Length;){int end=x+1;while(end<row.Length&&row[end]==row[x])end++;
                if(row[x] is 'C' or '#')dc.DrawRectangle(row[x]=='C'?Claimed:Wall,null,new Rect(Grid(x,y+1),Grid(end,y)));
                x=end;
            }
        }
        var red=new SolidColorBrush(Color.FromArgb(150,178,60,60));
        foreach(var body in b.Bodies.Concat(b.Projectiles))dc.DrawEllipse(red,null,World(body.Position),Math.Max(3,body.Radius/b.CellSize*scale),Math.Max(3,body.Radius/b.CellSize*scale));
        foreach(var item in b.Items.Where(i=>!i.Collected)){
            var point=World(item.Position);dc.DrawRectangle(Brushes.DarkGoldenrod,new Pen(Brushes.White,1),new Rect(point.X-4,point.Y-4,8,8));
        }
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(39,95,171)),new Pen(Brushes.White,2),World([b.Player.U,b.Player.V]),6,6);
        dc.DrawRectangle(null,new Pen(Wall,1),new Rect(left,top,b.Width*scale,b.Height*scale));
    }
}
