param([Parameter(Mandatory=$true)][string]$Root)

Add-Type -AssemblyName System.Drawing
if (-not ('MomoSpriteAudit' -as [type])) {
    Add-Type -ReferencedAssemblies ([System.Drawing.Bitmap].Assembly.Location) -TypeDefinition @'
using System;
using System.Drawing;
using System.IO;

public static class MomoSpriteAudit
{
    public static string One(string path)
    {
        using(var image=new Bitmap(path)) {
            int left=image.Width, top=image.Height, right=-1, bottom=-1;
            int eyeLeft=image.Width, eyeTop=image.Height, eyeRight=-1, eyeBottom=-1;
            for(int y=0;y<image.Height;y++) for(int x=0;x<image.Width;x++) {
                Color c=image.GetPixel(x,y);
                if(c.A>20) { left=Math.Min(left,x); right=Math.Max(right,x); top=Math.Min(top,y); bottom=Math.Max(bottom,y); }
                if(c.A>150 && c.B>65 && c.B>c.R*1.32 && c.B>c.G*1.12) {
                    eyeLeft=Math.Min(eyeLeft,x); eyeRight=Math.Max(eyeRight,x);
                    eyeTop=Math.Min(eyeTop,y); eyeBottom=Math.Max(eyeBottom,y);
                }
            }
            string group=Path.GetFileName(Path.GetDirectoryName(path));
            return String.Format("{0,-12} {1,-16} alpha ({2,3},{3,3}) {4,3}x{5,3}  margins L{6,3} T{7,3} R{8,3} B{9,3}  eye {10,3}x{11,3}",
                group,Path.GetFileName(path),left,top,right-left+1,bottom-top+1,
                left,top,image.Width-1-right,image.Height-1-bottom,eyeRight-eyeLeft+1,eyeBottom-eyeTop+1);
        }
    }
}
'@
}

Get-ChildItem -LiteralPath $Root -Recurse -Filter *.png | Sort-Object DirectoryName,Name | ForEach-Object {
    [MomoSpriteAudit]::One($_.FullName)
}
