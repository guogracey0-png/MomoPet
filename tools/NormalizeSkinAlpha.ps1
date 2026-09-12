param([Parameter(Mandatory=$true)][string]$Path)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies 'System.Drawing' -TypeDefinition @'
using System.Drawing;
using System.Drawing.Imaging;
public static class OpaqueSkinBody {
    public static void Fix(string path) {
        using(var src=new Bitmap(path)) {
            using(var dst=new Bitmap(src.Width,src.Height,PixelFormat.Format32bppArgb)) {
                for(int y=0;y<src.Height;y++) for(int x=0;x<src.Width;x++) {
                    var c=src.GetPixel(x,y);int a=c.A>=230?255:c.A;
                    dst.SetPixel(x,y,Color.FromArgb(a,c.R,c.G,c.B));
                }
                dst.Save(path+".opaque.png",ImageFormat.Png);
            }
        }
        System.IO.File.Copy(path+".opaque.png",path,true);
        System.IO.File.Delete(path+".opaque.png");
    }
}
'@
[OpaqueSkinBody]::Fix((Resolve-Path -LiteralPath $Path).Path)
