param(
    [Parameter(Mandatory=$true)][string]$SheetDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$HappyDirectory
)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies 'System.Drawing' -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;

public static class SkinAlphaCleaner {
    static bool IsBackground(Color c) {
        if (c.A < 16) return true;
        int max=Math.Max(c.R,Math.Max(c.G,c.B));
        int min=Math.Min(c.R,Math.Min(c.G,c.B));
        return max-min <= 20 && max >= 70;
    }
    public static void CleanAndSave(Bitmap source, string path) {
        var image=new Bitmap(source.Width,source.Height,PixelFormat.Format32bppArgb);
        using(var g=Graphics.FromImage(image)){g.DrawImageUnscaled(source,0,0);}
        int w=image.Width,h=image.Height;
        var seen=new bool[w*h];var queue=new Queue<int>();
        Action<int,int> seed=(x,y)=>{int p=y*w+x;if(!seen[p]&&IsBackground(image.GetPixel(x,y))){seen[p]=true;queue.Enqueue(p);}};
        for(int x=0;x<w;x++){seed(x,0);seed(x,h-1);}
        for(int y=0;y<h;y++){seed(0,y);seed(w-1,y);}
        while(queue.Count>0){int p=queue.Dequeue(),x=p%w,y=p/w;
            if(x>0)seed(x-1,y);if(x+1<w)seed(x+1,y);if(y>0)seed(x,y-1);if(y+1<h)seed(x,y+1);
        }
        for(int y=0;y<h;y++)for(int x=0;x<w;x++){int p=y*w+x;if(seen[p])image.SetPixel(x,y,Color.Transparent);}
        image.Save(path,ImageFormat.Png);image.Dispose();
    }
}
'@

$names=@('idle','walk','run','dragged','sleep','edge','happy','coffee')
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
Get-ChildItem -LiteralPath $SheetDirectory -Filter '*.png' -File | ForEach-Object {
    $id=$_.BaseName;$skinOut=Join-Path $OutputDirectory $id
    New-Item -ItemType Directory -Force -Path $skinOut | Out-Null
    $sheet=[Drawing.Bitmap]::FromFile($_.FullName)
    try {
        for($i=0;$i -lt 8;$i++){
            $column=$i%4;$row=[Math]::Floor($i/4)
            $left=[Math]::Floor($sheet.Width*$column/4);$right=[Math]::Floor($sheet.Width*($column+1)/4)
            $top=[Math]::Floor($sheet.Height*$row/2);$bottom=[Math]::Floor($sheet.Height*($row+1)/2)
            $cellWidth=$right-$left;$cellHeight=$bottom-$top
            $rect=New-Object Drawing.Rectangle($left,$top,$cellWidth,$cellHeight)
            $cell=$sheet.Clone($rect,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
            try{[SkinAlphaCleaner]::CleanAndSave($cell,(Join-Path $skinOut ($names[$i]+'.png')))}finally{$cell.Dispose()}
        }
    } finally {$sheet.Dispose()}
}

if($HappyDirectory -and (Test-Path -LiteralPath $HappyDirectory)){
    Get-ChildItem -LiteralPath $HappyDirectory -Filter '*.png' -File | ForEach-Object {
        $skinOut=Join-Path $OutputDirectory $_.BaseName
        if(Test-Path -LiteralPath $skinOut){
            $source=[Drawing.Bitmap]::FromFile($_.FullName)
            try{[SkinAlphaCleaner]::CleanAndSave($source,(Join-Path $skinOut 'happy.png'))}finally{$source.Dispose()}
        }
    }
}

Get-ChildItem -LiteralPath $OutputDirectory -Recurse -Filter '*.png' -File | ForEach-Object {
    $image=[Drawing.Bitmap]::FromFile($_.FullName)
    try{if(-not [Drawing.Image]::IsAlphaPixelFormat($image.PixelFormat)){throw "Missing alpha: $($_.FullName)"}}finally{$image.Dispose()}
}
Write-Output 'Skin action assets prepared with real alpha.'
