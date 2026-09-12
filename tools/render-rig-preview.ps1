param(
    [string]$PartsDirectory = (Join-Path $PSScriptRoot '..\assets\rig\parts'),
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\assets\rig\rig-preview.png')
)

Add-Type -AssemblyName System.Drawing
if (-not ('MomoRigPreview' -as [type])) {
    Add-Type -ReferencedAssemblies ([System.Drawing.Bitmap].Assembly.Location) -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class MomoRigPreview
{
    static double Wrap(double value) { return value-Math.Floor(value); }
    static double Smooth(double value) { value=Math.Max(0,Math.Min(1,value)); return value*value*(3-2*value); }

    static void Leg(double phase, out float angle, out float lift)
    {
        phase=Wrap(phase); const double stance=.62, forward=13, backward=11;
        if(phase<stance) { double u=Smooth(phase/stance); angle=(float)(-forward+(forward+backward)*u); lift=0; }
        else { double u=Smooth((phase-stance)/(1-stance)); angle=(float)(backward-(forward+backward)*u); lift=(float)(-3.5*Math.Sin(Math.PI*u)); }
    }

    static void Draw(Graphics graphics, Image image, float left, float top, float width, float height,
        float angle, float lift, float pivotX, float pivotY)
    {
        var state=graphics.Save();
        graphics.TranslateTransform(left+width*pivotX,top+height*pivotY+lift);
        graphics.RotateTransform(angle);
        graphics.TranslateTransform(-(left+width*pivotX),-(top+height*pivotY));
        graphics.DrawImage(image,left,top,width,height);
        graphics.Restore(state);
    }

    public static void Run(string partsDirectory, string outputPath)
    {
        Image[] parts=new Image[6];
        try {
            for(int i=0;i<6;i++) parts[i]=Image.FromFile(Path.Combine(partsDirectory,"frame-"+i+".png"));
            const int scale=4, frameWidth=112, frameHeight=116, frames=6;
            using(var canvas=new Bitmap(frameWidth*frames*scale,frameHeight*scale,PixelFormat.Format32bppArgb))
            using(var graphics=Graphics.FromImage(canvas)) {
                graphics.Clear(Color.FromArgb(238,243,246));
                graphics.SmoothingMode=SmoothingMode.AntiAlias;
                graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;
                graphics.ScaleTransform(scale,scale);
                for(int frame=0;frame<frames;frame++) {
                    float origin=frame*frameWidth;
                    double phase=frame/(double)frames;
                    float hnA,hnL,fnA,fnL,hfA,hfL,ffA,ffL;
                    Leg(phase,out hnA,out hnL); Leg(phase+.75,out fnA,out fnL);
                    Leg(phase+.5,out hfA,out hfL); Leg(phase+.25,out ffA,out ffL);
                    Draw(graphics,parts[5],origin-4,61,44,20,(float)(-4+Math.Sin(phase*Math.PI*2)*4.2),0,.92f,.52f);
                    Draw(graphics,parts[4],origin+27,65,17,40,hfA,hfL,.5f,.10f);
                    Draw(graphics,parts[3],origin+73,65,16,40,ffA,ffL,.5f,.10f);
                    Draw(graphics,parts[2],origin+36,66,18,41,hnA,hnL,.5f,.10f);
                    Draw(graphics,parts[1],origin+82,66,17,41,fnA,fnL,.5f,.10f);
                    graphics.DrawImage(parts[0],origin+13,4,86,89);
                }
                graphics.ResetTransform();
                canvas.Save(outputPath,ImageFormat.Png);
            }
        } finally { foreach(var part in parts) if(part!=null) part.Dispose(); }
    }
}
'@
}

[MomoRigPreview]::Run((Resolve-Path $PartsDirectory).Path, [IO.Path]::GetFullPath($OutputPath))
