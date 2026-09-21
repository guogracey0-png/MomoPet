using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MomoPetApp
{
    public partial class PetController
    {

        void RunImageOcr()
        {
            if(workingBitmap==null){imageStatus.Text="请先打开或生成一张图片";return;}if(imageAiBusy||!ValidateImageAi(true))return;imageAiBusy=true;string input=PrepareWorkingImageFile();imageStatus.Text="文本模型正在识别图片文字…";var request=new Dictionary<string,object>{{"mode","ocr"},{"base_url",ActiveTextBaseUrl()},{"model",ActiveTextModel()},{"image_path",input},{"provider",TextUsesToApis()?"toapis":"official"}};
            RunImageHelper(request,LoadActiveTextKey(),null,delegate(Dictionary<string,object> response){imageAiBusy=false;try{File.Delete(input);}catch{}if(!ImageResponseOk(response))return;recognizedTextBox.Text=Convert.ToString(response["text"]);imageStatus.Text="识别完成，可修改文字后点“按文字改图”";});
        }

        void RunImageTextSelection()
        {
            StartImageTextSelection(true);
        }

        void StartImageTextSelection(bool userInitiated)
        {
            if(workingBitmap==null){imageStatus.Text="请先打开或生成一张图片";return;}
            if(imageAiBusy)return;
            if(!userInitiated&&TryLoadImageTextCache(editingImageItem==null?null:editingImageItem.Value)){LayoutImageTextLayer();imageStatus.Text="文字已就绪：可跨行拖选，Ctrl+A 全选，Ctrl+C 复制";return;}
            ExitCropMode();ClearImageTextLayer();imageAiBusy=true;string input=PrepareWorkingImageFile();imageStatus.Text="本地识字中…";
            RunLocalOcr(input,delegate(LocalOcrOutput output,string localError)
            {
                if(output!=null&&output.Blocks.Count>0){ReadLocalOcrOutput(output);imageAiBusy=false;try{File.Delete(input);}catch{}if(editingImageItem!=null&&File.Exists(editingImageItem.Value))SaveImageTextCache(editingImageItem.Value);LayoutImageTextLayer();imageStatus.Text="文字已就绪：可跨行拖选，Ctrl+A 全选，Ctrl+C 复制";return;}
                if(HasTextOcrConfiguration()){StartRemoteImageTextSelection(input);return;}imageAiBusy=false;try{File.Delete(input);}catch{}imageStatus.Text="本地 OCR 没有识别到文字";
            });
        }

        void StartRemoteImageTextSelection(string input)
        {
            imageStatus.Text="本地 OCR 未识别到文字，正在使用文本模型兜底…";var request=new Dictionary<string,object>{{"mode","ocr_layout"},{"base_url",ActiveTextBaseUrl()},{"model",ActiveTextModel()},{"image_path",input},{"provider",TextUsesToApis()?"toapis":"official"}};
            RunImageHelper(request,LoadActiveTextKey(),null,delegate(Dictionary<string,object> response){imageAiBusy=false;try{File.Delete(input);}catch{}if(!ImageResponseOk(response))return;ReadImageTextResponse(response);if(editingImageItem!=null&&File.Exists(editingImageItem.Value))SaveImageTextCache(editingImageItem.Value);LayoutImageTextLayer();imageStatus.Text="文字已就绪：可跨行拖选，Ctrl+A 全选，Ctrl+C 复制";});
        }

        void ReadLocalOcrOutput(LocalOcrOutput output)
        {
            imageTextRegions.Clear();foreach(LocalOcrBlock block in output.Blocks)if(!String.IsNullOrWhiteSpace(block.Text))imageTextRegions.Add(new ImageTextRegion{Text=block.Text,X=block.X,Y=block.Y,Width=Math.Max(.002,block.Width),Height=Math.Max(.002,block.Height)});recognizedTextBox.Text=output.Text??BuildSelectedImageText(0,imageTextRegions.Count-1,imageTextRegions);
        }

        bool HasTextOcrConfiguration(){return imageAiConfig!=null&&!String.IsNullOrWhiteSpace(ActiveTextBaseUrl())&&!String.IsNullOrWhiteSpace(ActiveTextModel())&&!String.IsNullOrWhiteSpace(LoadActiveTextKey());}

        void ReadImageTextResponse(Dictionary<string,object> response)
        {
            imageTextRegions.Clear();object raw;
            if(response.TryGetValue("blocks",out raw)){var values=raw as System.Collections.IEnumerable;if(values!=null)foreach(object value in values){var map=value as Dictionary<string,object>;if(map==null)continue;string text=map.ContainsKey("text")?Convert.ToString(map["text"]):"";if(String.IsNullOrWhiteSpace(text))continue;imageTextRegions.Add(new ImageTextRegion{Text=text,X=NumberFromMap(map,"x"),Y=NumberFromMap(map,"y"),Width=Math.Max(.02,NumberFromMap(map,"width")),Height=Math.Max(.02,NumberFromMap(map,"height"))});}}
            recognizedTextBox.Text=response.ContainsKey("text")?Convert.ToString(response["text"]):String.Join(Environment.NewLine,imageTextRegions.Select(x=>x.Text));
        }

        string ImageTextCachePath(string imagePath)
        {
            if(String.IsNullOrWhiteSpace(imagePath)||!File.Exists(imagePath))return null;var file=new FileInfo(imagePath);string identity="local-geometry-v2|"+Path.GetFullPath(imagePath).ToLowerInvariant()+"|"+file.Length+"|"+file.LastWriteTimeUtc.Ticks;
            using(var sha=SHA256.Create()){string name=String.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(identity)).Select(x=>x.ToString("x2")));return Path.Combine(imageOcrCacheDir,name+".json");}
        }

        bool TryLoadImageTextCache(string imagePath)
        {
            try{string path=ImageTextCachePath(imagePath);if(String.IsNullOrEmpty(path)||!File.Exists(path))return false;var cache=json.Deserialize<ImageTextCache>(File.ReadAllText(path,Encoding.UTF8));if(cache==null||cache.Regions==null||cache.Regions.Count==0)return false;imageTextRegions.Clear();imageTextRegions.AddRange(cache.Regions);recognizedTextBox.Text=cache.Text??String.Join(Environment.NewLine,imageTextRegions.Select(x=>x.Text));return true;}catch{return false;}
        }

        void SaveImageTextCache(string imagePath)
        {
            SaveImageTextCache(imagePath,new ImageTextCache{Text=recognizedTextBox.Text,Regions=imageTextRegions.ToList()});
        }

        void SaveImageTextCache(string imagePath,ImageTextCache cache)
        {
            try{string path=ImageTextCachePath(imagePath);if(String.IsNullOrEmpty(path)||cache==null||cache.Regions==null||cache.Regions.Count==0)return;File.WriteAllText(path,new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(cache),new UTF8Encoding(false));}catch{}
        }

        void QueueImageTextPrecache(string imagePath)
        {
            try{if(String.IsNullOrWhiteSpace(imagePath)||!File.Exists(imagePath))return;string cachePath=ImageTextCachePath(imagePath);if(!String.IsNullOrEmpty(cachePath)&&File.Exists(cachePath))return;lock(imageOcrPrecacheQueue){if(!imageOcrPrecacheQueue.Contains(imagePath))imageOcrPrecacheQueue.Enqueue(imagePath);if(imageOcrPrecacheRunning)return;imageOcrPrecacheRunning=true;}ProcessImageTextPrecacheQueue();}catch{}
        }

        void ProcessImageTextPrecacheQueue()
        {
            string imagePath=null;lock(imageOcrPrecacheQueue){if(imageOcrPrecacheQueue.Count>0)imagePath=imageOcrPrecacheQueue.Dequeue();else{imageOcrPrecacheRunning=false;return;}}
            RunLocalOcr(imagePath,delegate(LocalOcrOutput output,string error){try{if(output!=null&&output.Blocks.Count>0){var regions=output.Blocks.Select(block=>new ImageTextRegion{Text=block.Text,X=block.X,Y=block.Y,Width=block.Width,Height=block.Height}).ToList();SaveImageTextCache(imagePath,new ImageTextCache{Text=output.Text,Regions=regions});}}catch{}finally{ProcessImageTextPrecacheQueue();}});
        }

        void RunLocalOcr(string imagePath,Action<LocalOcrOutput,string> completed)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                LocalOcrOutput result=null;string error=null;
                try
                {
                    var psi=new ProcessStartInfo{FileName=EmbeddedRuntime.ResolveFile("MomoOcr.exe",root),Arguments="\""+imagePath+"\"",WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};string output,errors;
                    using(var process=Process.Start(psi)){localOcrProcess=process;output=process.StandardOutput.ReadToEnd();errors=process.StandardError.ReadToEnd();process.WaitForExit();if(process.ExitCode!=0)error=String.IsNullOrWhiteSpace(errors)?"本地 OCR 失败":errors.Trim();}localOcrProcess=null;
                    if(error==null){result=new LocalOcrOutput();foreach(string line in output.Replace("\r","").Split('\n')){if(String.IsNullOrWhiteSpace(line))continue;string[] parts=line.Split('\t');if(parts.Length>=2&&parts[0]=="TEXT")result.Text=Encoding.UTF8.GetString(Convert.FromBase64String(parts[1]));else if(parts.Length>=6&&parts[0]=="BLOCK"){double x,y,w,h;if(Double.TryParse(parts[2],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out x)&&Double.TryParse(parts[3],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out y)&&Double.TryParse(parts[4],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out w)&&Double.TryParse(parts[5],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out h))result.Blocks.Add(new LocalOcrBlock{Text=Encoding.UTF8.GetString(Convert.FromBase64String(parts[1])),X=x,Y=y,Width=w,Height=h});}}}
                }
                catch(Exception ex){error=ex.Message;}
                UiPost(new Action(delegate{completed(result,error);}));
            });
        }

        double NumberFromMap(Dictionary<string,object> map,string key){object value;if(!map.TryGetValue(key,out value)||value==null)return 0;try{return Math.Max(0,Math.Min(1,Convert.ToDouble(value,System.Globalization.CultureInfo.InvariantCulture)));}catch{return 0;}}

        void ClearImageTextLayer(){imageTextRegions.Clear();orderedImageTextRegions.Clear();imageTextSelectionStart=imageTextSelectionEnd=-1;imageTextSelecting=false;if(imageTextLayer!=null){imageTextLayer.ReleaseMouseCapture();imageTextLayer.Children.Clear();imageTextLayer.Visibility=Visibility.Collapsed;}}

        void LayoutImageTextLayer()
        {
            if(imageTextLayer==null||imageTextRegions.Count==0||workingBitmap==null){if(imageTextLayer!=null)imageTextLayer.Visibility=Visibility.Collapsed;return;}imageTextLayer.Children.Clear();imageTextLayer.Width=imageCanvas.ActualWidth;imageTextLayer.Height=imageCanvas.ActualHeight;Rect rendered=RenderedImageRect();if(rendered.IsEmpty)return;
            orderedImageTextRegions.Clear();var lines=new List<List<ImageTextRegion>>();foreach(ImageTextRegion region in imageTextRegions.OrderBy(x=>x.Y+x.Height/2).ThenBy(x=>x.X)){List<ImageTextRegion> line=lines.LastOrDefault();if(line==null||Math.Abs(line.Average(x=>x.Y+x.Height/2)-(region.Y+region.Height/2))>Math.Max(region.Height,line.Max(x=>x.Height))*.65){line=new List<ImageTextRegion>();lines.Add(line);}line.Add(region);}foreach(var line in lines)orderedImageTextRegions.AddRange(line.OrderBy(x=>x.X));
            RenderImageTextSelection();imageTextLayer.Visibility=Visibility.Visible;Panel.SetZIndex(imageTextLayer,20);
        }

        void RenderImageTextSelection()
        {
            if(imageTextLayer==null)return;imageTextLayer.Children.Clear();if(imageTextSelectionStart<0||imageTextSelectionEnd<0)return;Rect rendered=RenderedImageRect();if(rendered.IsEmpty)return;int first=Math.Min(imageTextSelectionStart,imageTextSelectionEnd),last=Math.Max(imageTextSelectionStart,imageTextSelectionEnd);
            for(int index=first;index<=last&&index<orderedImageTextRegions.Count;index++){ImageTextRegion region=orderedImageTextRegions[index];var highlight=new System.Windows.Shapes.Rectangle{Fill=new SolidColorBrush(Color.FromArgb(112,54,139,255)),IsHitTestVisible=false,RadiusX=2,RadiusY=2,Width=Math.Max(2,region.Width*rendered.Width+3),Height=Math.Max(2,region.Height*rendered.Height+3)};Canvas.SetLeft(highlight,rendered.Left+region.X*rendered.Width-1.5);Canvas.SetTop(highlight,rendered.Top+region.Y*rendered.Height-1.5);imageTextLayer.Children.Add(highlight);}
        }

        int ImageTextHitIndex(Point point)
        {
            Rect rendered=RenderedImageRect();if(rendered.IsEmpty||!rendered.Contains(point)||orderedImageTextRegions.Count==0)return -1;double best=Double.MaxValue;int bestIndex=-1;
            for(int index=0;index<orderedImageTextRegions.Count;index++){ImageTextRegion region=orderedImageTextRegions[index];Rect box=new Rect(rendered.Left+region.X*rendered.Width,rendered.Top+region.Y*rendered.Height,Math.Max(2,region.Width*rendered.Width),Math.Max(2,region.Height*rendered.Height));Rect expanded=new Rect(box.X-4,box.Y-4,box.Width+8,box.Height+8);if(expanded.Contains(point))return index;double dx=point.X<box.Left?box.Left-point.X:(point.X>box.Right?point.X-box.Right:0),dy=point.Y<box.Top?box.Top-point.Y:(point.Y>box.Bottom?point.Y-box.Bottom:0),distance=dx*dx+dy*dy;if(distance<best){best=distance;bestIndex=index;}}
            return best<=28*28?bestIndex:-1;
        }

        void ImageTextMouseDown(object sender,MouseButtonEventArgs e)
        {
            int index=ImageTextHitIndex(e.GetPosition(imageTextLayer));if(index<0)return;imageTextSelectionStart=imageTextSelectionEnd=index;imageTextSelecting=true;imageTextLayer.Focus();imageTextLayer.CaptureMouse();RenderImageTextSelection();e.Handled=true;
        }

        void ImageTextMouseMove(object sender,MouseEventArgs e)
        {
            if(!imageTextSelecting)return;int index=ImageTextHitIndex(e.GetPosition(imageTextLayer));if(index>=0&&index!=imageTextSelectionEnd){imageTextSelectionEnd=index;RenderImageTextSelection();}e.Handled=true;
        }

        void ImageTextMouseUp(object sender,MouseButtonEventArgs e)
        {
            if(!imageTextSelecting)return;imageTextSelecting=false;imageTextLayer.ReleaseMouseCapture();string selected=BuildSelectedImageText(imageTextSelectionStart,imageTextSelectionEnd,orderedImageTextRegions);imageStatus.Text=String.IsNullOrEmpty(selected)?"未选中文字":"已选择 "+selected.Length+" 个字符；Ctrl+C 复制，Ctrl+A 全选";e.Handled=true;
        }

        void ImageTextKeyDown(object sender,KeyEventArgs e)
        {
            if((Keyboard.Modifiers&ModifierKeys.Control)==0)return;if(e.Key==Key.A){imageTextSelectionStart=0;imageTextSelectionEnd=orderedImageTextRegions.Count-1;RenderImageTextSelection();imageStatus.Text="已全选图片文字；按 Ctrl+C 复制";e.Handled=true;}else if(e.Key==Key.C){string selected=BuildSelectedImageText(imageTextSelectionStart,imageTextSelectionEnd,orderedImageTextRegions);if(!String.IsNullOrEmpty(selected)){Clipboard.SetText(selected);imageStatus.Text="已复制 "+selected.Length+" 个字符";}e.Handled=true;}
        }

        string BuildSelectedImageText(int start,int end,IList<ImageTextRegion> source)
        {
            if(source==null||source.Count==0||start<0||end<0)return "";int first=Math.Max(0,Math.Min(start,end)),last=Math.Min(source.Count-1,Math.Max(start,end));var builder=new StringBuilder();ImageTextRegion previous=null;
            for(int index=first;index<=last;index++){ImageTextRegion current=source[index];if(previous!=null){bool newLine=Math.Abs((previous.Y+previous.Height/2)-(current.Y+current.Height/2))>Math.Max(previous.Height,current.Height)*.7;if(newLine)builder.AppendLine();else if(NeedsImageTextSpace(previous.Text,current.Text))builder.Append(' ');}builder.Append(current.Text);previous=current;}return builder.ToString();
        }

        bool NeedsImageTextSpace(string left,string right)
        {
            if(String.IsNullOrEmpty(left)||String.IsNullOrEmpty(right))return false;char a=left[left.Length-1],b=right[0];return a<128&&b<128&&Char.IsLetterOrDigit(a)&&Char.IsLetterOrDigit(b);
        }
}
}
