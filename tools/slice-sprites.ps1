param(
    [Parameter(Mandatory = $true)][string]$InputPath,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [int]$Columns = 4,
    [int]$Rows = 2,
    [switch]$KeepLargestComponent
)

Add-Type -AssemblyName System.Drawing
if (-not ('MomoSpriteSlicer' -as [type])) {
    Add-Type -ReferencedAssemblies ([System.Drawing.Bitmap].Assembly.Location) -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

public static class MomoSpriteSlicer
{
    static int Clamp(double value) { return (int)Math.Max(0, Math.Min(255, Math.Round(value))); }

    static void KeepLargest(Bitmap image)
    {
        int width=image.Width, height=image.Height;
        bool[] visited=new bool[width*height];
        System.Collections.Generic.List<int> largest=null;
        int[] dx={1,-1,0,0}, dy={0,0,1,-1};
        for(int y=0;y<height;y++) for(int x=0;x<width;x++) {
            int start=y*width+x;
            if(visited[start] || image.GetPixel(x,y).A<=16) continue;
            var component=new System.Collections.Generic.List<int>();
            var queue=new System.Collections.Generic.Queue<int>();
            visited[start]=true; queue.Enqueue(start);
            while(queue.Count>0) {
                int current=queue.Dequeue(), cx=current%width, cy=current/width;
                component.Add(current);
                for(int direction=0;direction<4;direction++) {
                    int nx=cx+dx[direction], ny=cy+dy[direction];
                    if(nx<0 || nx>=width || ny<0 || ny>=height) continue;
                    int next=ny*width+nx;
                    if(visited[next]) continue;
                    visited[next]=true;
                    if(image.GetPixel(nx,ny).A>16) queue.Enqueue(next);
                }
            }
            if(largest==null || component.Count>largest.Count) largest=component;
        }
        if(largest==null) return;
        bool[] keep=new bool[width*height];
        foreach(int pixel in largest) keep[pixel]=true;
        for(int y=0;y<height;y++) for(int x=0;x<width;x++)
            if(!keep[y*width+x]) image.SetPixel(x,y,Color.Transparent);
    }

    public static void Run(string inputPath, string outputDirectory, int columns, int rows, bool keepLargestComponent)
    {
        Directory.CreateDirectory(outputDirectory);
        using (var sheet = new Bitmap(inputPath)) {
            int cellWidth = sheet.Width / columns, cellHeight = sheet.Height / rows;
            for (int row = 0; row < rows; row++) for (int col = 0; col < columns; col++) {
                var sourceRect = new Rectangle(col*cellWidth, row*cellHeight, cellWidth, cellHeight);
                using (var source = sheet.Clone(sourceRect, PixelFormat.Format32bppArgb))
                using (var canvas = new Bitmap(cellWidth, cellHeight, PixelFormat.Format32bppArgb)) {
                    int minX=cellWidth, minY=cellHeight, maxX=0, maxY=0;
                    for (int y=0; y<cellHeight; y++) for (int x=0; x<cellWidth; x++) {
                        Color c=source.GetPixel(x,y);
                        int excess=c.G-Math.Max(c.R,c.B), alpha;
                        if (excess>=170) alpha=0; else if (excess<=12) alpha=255; else alpha=Clamp(255.0*(170-excess)/158.0);
                        if (alpha<=3) { canvas.SetPixel(x,y,Color.Transparent); continue; }
                        double a=alpha/255.0;
                        int red=Clamp(c.R/a), blue=Clamp(c.B/a), green=Clamp((c.G-(1.0-a)*255.0)/a);
                        canvas.SetPixel(x,y,Color.FromArgb(alpha,red,green,blue));
                        if(alpha>16){minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);}
                    }
                    if(keepLargestComponent) {
                        KeepLargest(canvas);
                        minX=cellWidth; minY=cellHeight; maxX=0; maxY=0;
                        for(int y=0;y<cellHeight;y++) for(int x=0;x<cellWidth;x++)
                            if(canvas.GetPixel(x,y).A>16) { minX=Math.Min(minX,x); maxX=Math.Max(maxX,x); minY=Math.Min(minY,y); maxY=Math.Max(maxY,y); }
                    }
                    int pad=12, left=Math.Max(0,minX-pad), top=Math.Max(0,minY-pad);
                    int right=Math.Min(cellWidth-1,maxX+pad), bottom=Math.Min(cellHeight-1,maxY+pad);
                    var crop=new Rectangle(left,top,right-left+1,bottom-top+1);
                    using(var frame=canvas.Clone(crop,PixelFormat.Format32bppArgb))
                        frame.Save(Path.Combine(outputDirectory,String.Format("frame-{0}.png",row*columns+col)),ImageFormat.Png);
                }
            }
        }
    }
}
'@
}
[MomoSpriteSlicer]::Run((Resolve-Path $InputPath).Path, [IO.Path]::GetFullPath($OutputDirectory), $Columns, $Rows, $KeepLargestComponent.IsPresent)
