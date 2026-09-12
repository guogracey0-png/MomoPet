param(
    [Parameter(Mandatory = $true)][string]$InputPath,
    [Parameter(Mandatory = $true)][string]$OutputPath
)

Add-Type -AssemblyName System.Drawing
if (-not ('MomoChromaKey' -as [type])) {
    Add-Type -ReferencedAssemblies ([System.Drawing.Bitmap].Assembly.Location) -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;

public static class MomoChromaKey
{
    static int Clamp(double value) { return (int)Math.Max(0, Math.Min(255, Math.Round(value))); }

    public static void Run(string inputPath, string outputPath)
    {
        using (var source = new Bitmap(inputPath))
        using (var canvas = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb))
        {
            int minX = source.Width, minY = source.Height, maxX = 0, maxY = 0;
            for (int y = 0; y < source.Height; y++) {
                for (int x = 0; x < source.Width; x++) {
                    Color c = source.GetPixel(x, y);
                    int greenExcess = c.G - Math.Max(c.R, c.B);
                    int alpha;
                    if (greenExcess >= 170) alpha = 0;
                    else if (greenExcess <= 12) alpha = 255;
                    else alpha = Clamp(255.0 * (170 - greenExcess) / 158.0);

                    if (alpha <= 3) { canvas.SetPixel(x, y, Color.Transparent); continue; }
                    double a = alpha / 255.0;
                    int red = Clamp(c.R / a);
                    int blue = Clamp(c.B / a);
                    int green = Clamp((c.G - (1.0 - a) * 255.0) / a);
                    canvas.SetPixel(x, y, Color.FromArgb(alpha, red, green, blue));
                    if (alpha > 16) { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
                }
            }
            int pad = 18;
            int left = Math.Max(0, minX-pad), top = Math.Max(0, minY-pad);
            int right = Math.Min(source.Width-1, maxX+pad), bottom = Math.Min(source.Height-1, maxY+pad);
            var rect = new Rectangle(left, top, right-left+1, bottom-top+1);
            using (var cropped = canvas.Clone(rect, PixelFormat.Format32bppArgb)) cropped.Save(outputPath, ImageFormat.Png);
        }
    }
}
'@
}
[MomoChromaKey]::Run((Resolve-Path $InputPath).Path, [IO.Path]::GetFullPath($OutputPath))

