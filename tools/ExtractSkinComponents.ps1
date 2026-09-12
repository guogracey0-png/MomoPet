param([string]$Source='assets/skin-magenta-drafts',[string]$Output='assets/skin-motion-reviewed')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Collections.Generic;
public static class SkinComponentExtractor {
 static bool Background(Color c){return c.R>165&&c.B>150&&c.G<155&&Math.Min(c.R,c.B)-c.G>55;}
 public static void Extract(string source,string output){
  using(var b=new Bitmap(source)){
   int w=b.Width,h=b.Height;var pixels=new Color[w*h];var seen=new bool[w*h];var groups=new List<List<int>>();
   for(int y=0;y<h;y++)for(int x=0;x<w;x++){int p=y*w+x;pixels[p]=b.GetPixel(x,y);seen[p]=Background(pixels[p]);}
   for(int start=0;start<seen.Length;start++){
    if(seen[start])continue;
    var part=new List<int>();var q=new Queue<int>();q.Enqueue(start);seen[start]=true;
    while(q.Count>0){int p=q.Dequeue();part.Add(p);int x=p%w,y=p/w;
     for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++){
      int nx=x+dx,ny=y+dy;if(nx<0||ny<0||nx>=w||ny>=h)continue;int n=ny*w+nx;if(seen[n])continue;seen[n]=true;q.Enqueue(n);
     }
    }
    if(part.Count>=20)groups.Add(part);
   }
   var cells=new List<int>[8];int[] mainCount=new int[8];for(int i=0;i<8;i++)cells[i]=new List<int>();
   foreach(var part in groups){double sx=0,sy=0;foreach(int p in part){sx+=p%w;sy+=p/w;}int col=Math.Min(3,(int)(sx/part.Count*4/w)),row=Math.Min(1,(int)(sy/part.Count*2/h));int index=row*4+col;cells[index].AddRange(part);if(part.Count>5000)mainCount[index]++;}
   string[] names={"idle","walk","run","dragged","sleep","edge","happy","coffee"};Directory.CreateDirectory(output);
   for(int i=0;i<8;i++){
    if(i==5)continue;
    if(mainCount[i]!=1)throw new Exception(source+" "+names[i]+": expected exactly one complete cat, found "+mainCount[i]);
    int left=w,top=h,right=0,bottom=0;foreach(int p in cells[i]){left=Math.Min(left,p%w);right=Math.Max(right,p%w);top=Math.Min(top,p/w);bottom=Math.Max(bottom,p/w);}
    if(left==0||top==0||right==w-1||bottom==h-1)throw new Exception(source+" "+names[i]+": touches original sheet edge");
    int outW=Math.Max(448,right-left+33),outH=Math.Max(560,bottom-top+33);
    int ox=(outW-(right-left+1))/2,oy=(outH-(bottom-top+1))/2;
    using(var result=new Bitmap(outW,outH,PixelFormat.Format32bppArgb)){
     foreach(int p in cells[i]){var c=pixels[p];result.SetPixel(p%w-left+ox,p/w-top+oy,Color.FromArgb(255,c.R,c.G,c.B));}
     result.Save(Path.Combine(output,names[i]+".png"),ImageFormat.Png);
    }
   }
  }
 }
}
'@
foreach($file in Get-ChildItem -File $Source -Filter '*.png'){
 $dest=[IO.Path]::GetFullPath((Join-Path $Output $file.BaseName))
 [SkinComponentExtractor]::Extract($file.FullName,$dest)
 Copy-Item -LiteralPath "assets/normalized/skin-motion/$($file.BaseName)/edge.png" -Destination (Join-Path $dest 'edge.png')
}
