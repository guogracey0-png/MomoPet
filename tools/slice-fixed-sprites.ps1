param(
    [Parameter(Mandatory = $true)][string]$InputPath,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [int]$Columns = 4,
    [int]$Rows = 2
)

Add-Type -AssemblyName System.Drawing
if (-not ('MomoFixedSpriteSlicer' -as [type])) {
    Add-Type -ReferencedAssemblies ([System.Drawing.Bitmap].Assembly.Location) -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

public static class MomoFixedSpriteSlicer
{
    static int Clamp(double value) { return (int)Math.Max(0, Math.Min(255, Math.Round(value))); }

    public static void Run(string inputPath, string outputDirectory, int columns, int rows)
    {
        Directory.CreateDirectory(outputDirectory);
        using (var sheet = new Bitmap(inputPath)) {
            int width = sheet.Width / columns, height = sheet.Height / rows;
            for (int row=0; row<rows; row++) for (int col=0; col<columns; col++) {
                using (var frame = new Bitmap(width, height, PixelFormat.Format32bppArgb)) {
                    for (int y=0; y<height; y++) for (int x=0; x<width; x++) {
                        Color c = sheet.GetPixel(col*width+x, row*height+y);
                        int excess = c.G-Math.Max(c.R,c.B), alpha;
                        if (excess>=155) alpha=0;
                        else if (excess<=10) alpha=255;
                        else alpha=Clamp(255.0*(155-excess)/145.0);
                        if (alpha<=3) { frame.SetPixel(x,y,Color.Transparent); continue; }
                        double a=alpha/255.0;
                        int red=Clamp(c.R/a), blue=Clamp(c.B/a);
                        int green=Clamp((c.G-(1.0-a)*255.0)/a);
                        frame.SetPixel(x,y,Color.FromArgb(alpha,red,green,blue));
                    }
                    frame.Save(Path.Combine(outputDirectory,String.Format("frame-{0}.png",row*columns+col)),ImageFormat.Png);
                }
            }
        }
    }
}
'@
}

[MomoFixedSpriteSlicer]::Run((Resolve-Path $InputPath).Path, [IO.Path]::GetFullPath($OutputDirectory), $Columns, $Rows)
