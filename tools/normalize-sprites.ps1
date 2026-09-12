param(
    [Parameter(Mandatory=$true)][string]$InputDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [double]$TargetEyeWidth = 66,
    [double]$TargetEyeX = 260,
    [double]$TargetEyeY = 116,
    [int]$CanvasWidth = 384,
    [int]$CanvasHeight = 320
)

Add-Type -AssemblyName System.Drawing
if (-not ('MomoSpriteNormalizer' -as [type])) {
    Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

public static class MomoSpriteNormalizer
{
    static Rectangle BlueBounds(Bitmap image)
    {
        int left=image.Width, top=image.Height, right=-1, bottom=-1;
        for(int y=0;y<image.Height;y++) for(int x=0;x<image.Width;x++) {
            Color c=image.GetPixel(x,y);
            if(c.A>150 && c.B>65 && c.B>c.R*1.32 && c.B>c.G*1.12) {
                left=Math.Min(left,x); right=Math.Max(right,x); top=Math.Min(top,y); bottom=Math.Max(bottom,y);
            }
        }
        if(right<left || bottom<top) throw new InvalidDataException("Blue eye anchor not found");
        return Rectangle.FromLTRB(left,top,right+1,bottom+1);
    }

    public static void Run(string inputDirectory,string outputDirectory,double targetEyeWidth,double targetEyeX,double targetEyeY,int canvasWidth,int canvasHeight)
    {
        Directory.CreateDirectory(outputDirectory);
        string[] files=Directory.GetFiles(inputDirectory,"*.png").OrderBy(p=>p).ToArray();
        Rectangle[] anchors=new Rectangle[files.Length];
        for(int i=0;i<files.Length;i++) using(var source=new Bitmap(files[i])) anchors[i]=BlueBounds(source);

        // Use one scale for the entire motion group. Per-frame eye shapes may vary.
        int[] widths=anchors.Select(anchor=>anchor.Width).OrderBy(width=>width).ToArray();
        double medianWidth=widths.Length%2==1 ? widths[widths.Length/2] : (widths[widths.Length/2-1]+widths[widths.Length/2])/2.0;
        double scale=targetEyeWidth/medianWidth;

        for(int i=0;i<files.Length;i++) using(var source=new Bitmap(files[i])) {
            string file=files[i]; Rectangle eyes=anchors[i];
            double eyeX=eyes.Left+eyes.Width/2.0, eyeY=eyes.Top+eyes.Height/2.0;
            float dx=(float)(targetEyeX-eyeX*scale), dy=(float)(targetEyeY-eyeY*scale);
            using(var output=new Bitmap(canvasWidth,canvasHeight,PixelFormat.Format32bppArgb))
            using(var graphics=Graphics.FromImage(output)) {
                graphics.Clear(Color.Transparent);
                graphics.CompositingMode=CompositingMode.SourceCopy;
                graphics.CompositingQuality=CompositingQuality.HighQuality;
                graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;
                graphics.SmoothingMode=SmoothingMode.HighQuality;
                graphics.DrawImage(source,new RectangleF(dx,dy,(float)(source.Width*scale),(float)(source.Height*scale)));
                output.Save(Path.Combine(outputDirectory,Path.GetFileName(file)),ImageFormat.Png);
            }
        }
    }
}
'@
}

[MomoSpriteNormalizer]::Run(
    (Resolve-Path $InputDirectory).Path,
    [IO.Path]::GetFullPath($OutputDirectory),
    $TargetEyeWidth,$TargetEyeX,$TargetEyeY,$CanvasWidth,$CanvasHeight
)
