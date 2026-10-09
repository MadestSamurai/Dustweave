param([string]$OutputPath)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if(!$OutputPath){$OutputPath=Join-Path $root 'assets/branding/Dustweave.ico'}
$source=[Drawing.Bitmap]::new((Join-Path $root 'assets/branding/mark.png'))
$frames=[Collections.Generic.List[byte[]]]::new();$sizes=@(16,24,32,48,64,128,256)
try{
 foreach($size in $sizes){
  $bitmap=[Drawing.Bitmap]::new($size,$size,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g=[Drawing.Graphics]::FromImage($bitmap)
  try{
   $g.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias
   $g.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
   $g.Clear([Drawing.Color]::Transparent)
   $path=[Drawing.Drawing2D.GraphicsPath]::new();$radius=[single]($size*.18);$diameter=$radius*2
   $path.AddArc(0,0,$diameter,$diameter,180,90);$path.AddArc($size-$diameter,0,$diameter,$diameter,270,90)
   $path.AddArc($size-$diameter,$size-$diameter,$diameter,$diameter,0,90);$path.AddArc(0,$size-$diameter,$diameter,$diameter,90,90);$path.CloseFigure()
   $brush=[Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#176B57'));$g.FillPath($brush,$path);$brush.Dispose();$path.Dispose()
   $pad=[single]($size*.12);$g.DrawImage($source,$pad,$pad,[single]($size-2*$pad),[single]($size-2*$pad))
   $stream=[IO.MemoryStream]::new();$bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png);$frames.Add($stream.ToArray());$stream.Dispose()
  }finally{$g.Dispose();$bitmap.Dispose()}
 }
 $out=[IO.File]::Create($OutputPath);$writer=[IO.BinaryWriter]::new($out)
 try{
  $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$sizes.Count);$offset=6+16*$sizes.Count
  for($i=0;$i -lt $sizes.Count;$i++){$dim=if($sizes[$i] -eq 256){0}else{$sizes[$i]};$writer.Write([byte]$dim);$writer.Write([byte]$dim);$writer.Write([byte]0);$writer.Write([byte]0);$writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$frames[$i].Length);$writer.Write([uint32]$offset);$offset+=$frames[$i].Length}
  foreach($frame in $frames){$writer.Write($frame)}
 }finally{$writer.Dispose();$out.Dispose()}
}finally{$source.Dispose()}
Write-Host "App icon: $OutputPath"
