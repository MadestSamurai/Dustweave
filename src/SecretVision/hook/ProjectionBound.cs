using System;
internal static class ProjectionBound
{
    // Largest singular value of the exact linear world-XZ -> board-XY map.
    // A unit direction can use this much speed at most; the Frobenius norm
    // unnecessarily assumes both orthogonal directions occur at full speed.
    internal static double Maximum(double xx,double xy,double yx,double yy)
    {
        var a=xx*xx+xy*xy;var c=yx*yx+yy*yy;var b=xx*yx+xy*yy;
        return Math.Sqrt(Math.Max(0,(a+c+Math.Sqrt((a-c)*(a-c)+4*b*b))/2));
    }
}
