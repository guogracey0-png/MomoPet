param(
    [Parameter(Mandatory = $true)][string]$InputPath,
    [Parameter(Mandatory = $true)][string]$OutputPath
)

Add-Type -AssemblyName System.Drawing

if (-not ('PetAssetPrep' -as [type])) {
    Add-Type -ReferencedAssemblies ([System.Drawing.Bitmap].Assembly.Location) -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;

public static class PetAssetPrep
{
    public static void Run(string inputPath, string outputPath)
    {
        using (var source = new Bitmap(inputPath))
        using (var canvas = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(canvas)) g.DrawImageUnscaled(source, 0, 0);
            int width = canvas.Width, height = canvas.Height;
            int minX = width, minY = height, maxX = 0, maxY = 0;
            for (int y = 0; y < height; y++) {
                int left = width, right = -1;
                int searchStart = y < height * 46 / 100 ? width * 26 / 100 : 0;
                for (int x = searchStart; x < width; x++) {
                    Color c = canvas.GetPixel(x, y);
                    int max = Math.Max(c.R, Math.Max(c.G, c.B));
                    int min = Math.Min(c.R, Math.Min(c.G, c.B));
                    if (min < 224 || max - min > 22) { left = Math.Min(left, x); right = x; }
                }
                if (right < 0) {
                    for (int x = 0; x < width; x++) {
                        Color isolated = canvas.GetPixel(x, y);
                        int isoMax = Math.Max(isolated.R, Math.Max(isolated.G, isolated.B));
                        int isoMin = Math.Min(isolated.R, Math.Min(isolated.G, isolated.B));
                        if (isoMin < 224 || isoMax - isoMin > 22) {
                            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                        } else canvas.SetPixel(x, y, Color.Transparent);
                    }
                    continue;
                }
                minX = Math.Min(minX, left); maxX = Math.Max(maxX, right);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                for (int x = 0; x < width; x++) {
                    if (x < left || x > right) {
                        Color outside = canvas.GetPixel(x, y);
                        int outsideMax = Math.Max(outside.R, Math.Max(outside.G, outside.B));
                        int outsideMin = Math.Min(outside.R, Math.Min(outside.G, outside.B));
                        if (outsideMin < 224 || outsideMax - outsideMin > 22) {
                            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                        } else
                            canvas.SetPixel(x, y, Color.Transparent);
                    }
                    else {
                        Color c = canvas.GetPixel(x, y);
                        if (c.R > 224 && c.G > 224 && c.B > 224)
                            canvas.SetPixel(x, y, Color.FromArgb(255, 255, 253, 248));
                    }
                }
            }
            int pad = 24;
            int cropLeft = Math.Max(0, minX - pad), top = Math.Max(0, minY - pad);
            int cropRight = Math.Min(width - 1, maxX + pad), bottom = Math.Min(height - 1, maxY + pad);
            var rect = new Rectangle(cropLeft, top, cropRight - cropLeft + 1, bottom - top + 1);
            using (var cropped = canvas.Clone(rect, PixelFormat.Format32bppArgb)) {
                cropped.Save(outputPath, ImageFormat.Png);
            }
        }
    }
}
'@
}

[PetAssetPrep]::Run((Resolve-Path $InputPath).Path, [System.IO.Path]::GetFullPath($OutputPath))
