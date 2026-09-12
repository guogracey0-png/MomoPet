param([string]$Root='assets/skin-motion-v2',[string]$Output='test-output/skin-audit')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Collections.Generic;
public static class SkinAudit {
 public static string Check(string path) {
  using(var b=new Bitmap(path)) {
   int border=0,solid=0,partial=0,holes=0;
   for(int y=0;y<b.Height;y++)for(int x=0;x<b.Width;x++) {
    int a=b.GetPixel(x,y).A;
    if(a>31 && (x==0||y==0||x==b.Width-1||y==b.Height-1))border++;
    if(a==255)solid++;else if(a>0)partial++;
    if(x>4&&y>4&&x<b.Width-5&&y<b.Height-5&&a<230 && b.GetPixel(x-4,y).A==255&&b.GetPixel(x+4,y).A==255&&b.GetPixel(x,y-4).A==255&&b.GetPixel(x,y+4).A==255)holes++;
   }
   return String.Format("{0}x{1},border={2},solid={3},partial={4},suspectInterior={5}",b.Width,b.Height,border,solid,partial,holes);
  }
 }
 public static void Sheet(string root,string output) {
  string[] names={"idle","walk","run","dragged","sleep","edge","happy","coffee"};
  using(var sheet=new Bitmap(1000,600))using(var g=Graphics.FromImage(sheet))using(var font=new Font("Arial",12)) {
   g.Clear(Color.White);
   for(int i=0;i<8;i++) {
    int x=i%4*250,y=i/4*300;
    for(int py=0;py<270;py+=15)for(int px=0;px<250;px+=15) {
     using(var brush=new SolidBrush(((px/15+py/15)%2==0)?Color.FromArgb(60,67,78):Color.FromArgb(77,86,96)))g.FillRectangle(brush,x+px,y+py,15,15);
    }
    using(var b=new Bitmap(System.IO.Path.Combine(root,names[i]+".png"))) {
     float scale=Math.Min(230f/b.Width,250f/b.Height);
     g.DrawImage(b,x+(250-b.Width*scale)/2,y+(270-b.Height*scale)/2,b.Width*scale,b.Height*scale);
    }
    g.DrawString(names[i],font,Brushes.Black,x+10,y+277);
   }
   sheet.Save(output,ImageFormat.Png);
  }
 }
}
'@
New-Item -ItemType Directory -Force $Output | Out-Null
$rows=@()
foreach($dir in Get-ChildItem -Directory $Root) {
 foreach($file in Get-ChildItem -File $dir.FullName -Filter '*.png') {
  $rows += "$($dir.Name)/$($file.Name),$([SkinAudit]::Check($file.FullName))"
 }
 [SkinAudit]::Sheet($dir.FullName,[IO.Path]::GetFullPath((Join-Path $Output ($dir.Name+'.png'))))
}
$rows | Set-Content (Join-Path $Output 'pixels.csv') -Encoding UTF8
$rows
