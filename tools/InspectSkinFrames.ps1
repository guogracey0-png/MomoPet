param([Parameter(Mandatory=$true)][string]$Root)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies 'System.Drawing' -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
public static class SkinComponentInspector {
    public static int[] Areas(string path) {
        using(var image=new Bitmap(path)) {
            int w=image.Width,h=image.Height;var seen=new bool[w*h];var result=new List<int>();
            for(int y=0;y<h;y++) for(int x=0;x<w;x++) {
                int start=y*w+x;if(seen[start]||image.GetPixel(x,y).A<32)continue;
                int area=0;var queue=new Queue<int>();seen[start]=true;queue.Enqueue(start);
                while(queue.Count>0){int p=queue.Dequeue(),px=p%w,py=p/w;area++;
                    for(int oy=-1;oy<=1;oy++)for(int ox=-1;ox<=1;ox++){if(ox==0&&oy==0)continue;int nx=px+ox,ny=py+oy;if(nx<0||ny<0||nx>=w||ny>=h)continue;int np=ny*w+nx;if(seen[np]||image.GetPixel(nx,ny).A<32)continue;seen[np]=true;queue.Enqueue(np);}
                }
                result.Add(area);
            }
            result.Sort((a,b)=>b.CompareTo(a));return result.ToArray();
        }
    }
}
'@
Get-ChildItem -LiteralPath $Root -Recurse -Filter '*.png' -File | ForEach-Object {
    $areas=[SkinComponentInspector]::Areas($_.FullName)
    $second=if($areas.Length -gt 1){$areas[1]}else{0}
    if($second -gt 100){[PSCustomObject]@{Path=$_.FullName.Substring((Resolve-Path $Root).Path.Length+1);Components=$areas.Length;Largest=$areas[0];Second=$second}}
}
