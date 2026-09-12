param(
    [Parameter(Mandatory=$true)][string]$SheetDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies 'System.Drawing' -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
public static class MagentaSkinKeyer {
    static bool IsMagenta(Color c) {
        int minRB=Math.Min(c.R,c.B);
        return c.R>165 && c.B>150 && c.G<155 && minRB-c.G>55;
    }
    public static void ExtractCell(Bitmap sheet,int column,int row,string path) {
        int left=(int)Math.Floor(sheet.Width*column/4.0),right=(int)Math.Floor(sheet.Width*(column+1)/4.0);
        int top=(int)Math.Floor(sheet.Height*row/2.0),bottom=(int)Math.Floor(sheet.Height*(row+1)/2.0);
        using(var cell=new Bitmap(right-left,bottom-top,PixelFormat.Format32bppArgb)) {
            for(int y=top;y<bottom;y++) for(int x=left;x<right;x++) {
                Color c=sheet.GetPixel(x,y);
                cell.SetPixel(x-left,y-top,IsMagenta(c)?Color.Transparent:Color.FromArgb(255,c.R,c.G,c.B));
            }
            cell.Save(path,ImageFormat.Png);
        }
    }
}
'@
$names=@('idle','walk','run','dragged','sleep','edge','happy','coffee')
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
Get-ChildItem -LiteralPath $SheetDirectory -Filter '*.png' -File | ForEach-Object {
    $skinOut=Join-Path $OutputDirectory $_.BaseName
    New-Item -ItemType Directory -Force -Path $skinOut | Out-Null
    $sheet=[Drawing.Bitmap]::FromFile($_.FullName)
    try {
        for($i=0;$i -lt 8;$i++){
            [MagentaSkinKeyer]::ExtractCell($sheet,$i%4,[Math]::Floor($i/4),(Join-Path $skinOut ($names[$i]+'.png')))
        }
    } finally {$sheet.Dispose()}
}
Write-Output 'Magenta action sheets converted to opaque cats on transparent backgrounds.'
