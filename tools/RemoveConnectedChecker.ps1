param(
    [Parameter(Mandatory = $true)][string]$InputPath,
    [Parameter(Mandatory = $true)][string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies 'System.Drawing' -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class ConnectedCheckerRemoval {
    public static void Run(string input, string output) {
        using (var source = new Bitmap(input))
        using (var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb)) {
            using (var graphics = Graphics.FromImage(bitmap)) graphics.DrawImageUnscaled(source, 0, 0);
            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var data = bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            int stride = data.Stride;
            byte[] pixels = new byte[stride * bitmap.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            bool[] visited = new bool[bitmap.Width * bitmap.Height];
            var queue = new Queue<int>();

            Action<int,int> add = (x,y) => {
                int index = y * bitmap.Width + x;
                if (visited[index]) return;
                int p = y * stride + x * 4;
                int b = pixels[p], g = pixels[p+1], r = pixels[p+2];
                int max = Math.Max(r, Math.Max(g,b));
                int min = Math.Min(r, Math.Min(g,b));
                if (max - min <= 40 && max >= 80) {
                    visited[index] = true;
                    queue.Enqueue(index);
                }
            };

            for (int x=0; x<bitmap.Width; x++) { add(x,0); add(x,bitmap.Height-1); }
            for (int y=0; y<bitmap.Height; y++) { add(0,y); add(bitmap.Width-1,y); }
            while (queue.Count > 0) {
                int index = queue.Dequeue();
                int x = index % bitmap.Width, y = index / bitmap.Width;
                int p = y * stride + x * 4;
                pixels[p] = pixels[p+1] = pixels[p+2] = pixels[p+3] = 0;
                if (x > 0) add(x-1,y);
                if (x+1 < bitmap.Width) add(x+1,y);
                if (y > 0) add(x,y-1);
                if (y+1 < bitmap.Height) add(x,y+1);
            }
            Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
            bitmap.UnlockBits(data);
            bitmap.Save(output, ImageFormat.Png);
        }
    }
}
'@

$inputFull = (Resolve-Path -LiteralPath $InputPath).Path
$outputFull = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $OutputPath))
$outputDirectory = Split-Path -Parent $outputFull
if ($outputDirectory) { New-Item -ItemType Directory -Force $outputDirectory | Out-Null }
[ConnectedCheckerRemoval]::Run($inputFull, $outputFull)
