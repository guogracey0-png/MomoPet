param([Parameter(Mandatory=$true)][string]$Directory)

Add-Type -AssemblyName System.Drawing
if (-not ('MomoSpriteAligner' -as [type])) {
    Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

public static class MomoSpriteAligner
{
    static PointF EyeCenter(Bitmap image)
    {
        double sx=0, sy=0, count=0;
        for (int y=0; y<image.Height; y++) for (int x=0; x<image.Width; x++) {
            Color c=image.GetPixel(x,y);
            if (c.A>180 && c.B>70 && c.B>c.R*1.35 && c.B>c.G*1.15) {
                sx+=x; sy+=y; count++;
            }
        }
        if (count==0) throw new InvalidDataException("Blue eye anchor not found");
        return new PointF((float)(sx/count),(float)(sy/count));
    }

    public static void Run(string directory)
    {
        string[] files=Directory.GetFiles(directory,"frame-*.png").OrderBy(p=>p).ToArray();
        var centers=new List<PointF>();
        foreach(string file in files) using(var image=new Bitmap(file)) centers.Add(EyeCenter(image));
        float targetX=centers.Average(p=>p.X), targetY=centers.Average(p=>p.Y);

        for(int i=0;i<files.Length;i++) using(var source=new Bitmap(files[i])) {
            int dx=(int)Math.Round(targetX-centers[i].X), dy=(int)Math.Round(targetY-centers[i].Y);
            using(var output=new Bitmap(source.Width,source.Height,PixelFormat.Format32bppArgb))
            using(var graphics=Graphics.FromImage(output)) {
                graphics.Clear(Color.Transparent);
                graphics.CompositingMode=System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                graphics.DrawImageUnscaled(source,dx,dy);
                string temp=files[i]+".aligned.png";
                output.Save(temp,ImageFormat.Png);
                source.Dispose();
                File.Delete(files[i]);
                File.Move(temp,files[i]);
            }
        }
    }
}
'@
}

[MomoSpriteAligner]::Run((Resolve-Path $Directory).Path)
