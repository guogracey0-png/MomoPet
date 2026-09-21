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

        BitmapSource LoadEditorBitmap(string path)
        {
            var image=new BitmapImage(); image.BeginInit(); image.CacheOption=BitmapCacheOption.OnLoad; image.UriSource=new Uri(path); image.EndInit(); image.Freeze(); return image;
        }

        BitmapSource LoadEditorBytes(byte[] bytes)
        {
            using(var stream=new MemoryStream(bytes)) { var image=new BitmapImage(); image.BeginInit(); image.CacheOption=BitmapCacheOption.OnLoad; image.StreamSource=stream; image.EndInit(); image.Freeze(); return image; }
        }

        void EnterCropMode(){if(workingBitmap==null){imageStatus.Text="请先打开或生成一张图片";return;}ClearImageTextLayer();cropModeActive=true;imageCanvas.Cursor=Cursors.Cross;InitializeCropBox();imageStatus.Text="裁切模式：拖动框体移动，拖四角缩放；完成后点击“确认裁剪”";}

        void ExitCropMode(){cropModeActive=false;cropSelecting=false;if(imageCanvas!=null){imageCanvas.ReleaseMouseCapture();imageCanvas.Cursor=Cursors.Arrow;}ResetCrop();}

        void ShowCrop()
        {
            if(cropBox==null||cropRect.IsEmpty)return;cropBox.Visibility=Visibility.Visible;Canvas.SetLeft(cropBox,cropRect.X);Canvas.SetTop(cropBox,cropRect.Y);cropBox.Width=cropRect.Width;cropBox.Height=cropRect.Height;
            if(cropHandles==null)return;double half=5.5;Point[] corners={new Point(cropRect.Left,cropRect.Top),new Point(cropRect.Right,cropRect.Top),new Point(cropRect.Right,cropRect.Bottom),new Point(cropRect.Left,cropRect.Bottom)};
            for(int i=0;i<cropHandles.Length;i++){cropHandles[i].Visibility=Visibility.Visible;Canvas.SetLeft(cropHandles[i],corners[i].X-half);Canvas.SetTop(cropHandles[i],corners[i].Y-half);}
        }

        void ResetCrop()
        {
            cropRect=Rect.Empty;if(cropBox!=null)cropBox.Visibility=Visibility.Collapsed;if(cropHandles!=null)foreach(var handle in cropHandles)handle.Visibility=Visibility.Collapsed;
        }

        void InitializeCropBox()
        {
            if(!cropModeActive){ResetCrop();return;}
            Rect bounds=RenderedImageRect();if(bounds.IsEmpty){ResetCrop();return;}double ratio=SelectedCropRatio(),width=bounds.Width*.72,height=bounds.Height*.72;
            if(ratio>0){if(width/height>ratio)width=height*ratio;else height=width/ratio;}
            width=Math.Max(24,Math.Min(width,bounds.Width));height=Math.Max(24,Math.Min(height,bounds.Height));
            cropRect=new Rect(bounds.Left+(bounds.Width-width)/2,bounds.Top+(bounds.Height-height)/2,width,height);ShowCrop();
        }

        string CropHitMode(Point point)
        {
            if(cropRect.IsEmpty)return "create";double hit=13;
            if(Math.Abs(point.X-cropRect.Left)<=hit&&Math.Abs(point.Y-cropRect.Top)<=hit)return "nw";
            if(Math.Abs(point.X-cropRect.Right)<=hit&&Math.Abs(point.Y-cropRect.Top)<=hit)return "ne";
            if(Math.Abs(point.X-cropRect.Right)<=hit&&Math.Abs(point.Y-cropRect.Bottom)<=hit)return "se";
            if(Math.Abs(point.X-cropRect.Left)<=hit&&Math.Abs(point.Y-cropRect.Bottom)<=hit)return "sw";
            return cropRect.Contains(point)?"move":"create";
        }

        void CropMouseDown(object sender,MouseButtonEventArgs e)
        {
            if(layerCompositionActive&&TryStartLayerDrag(e))return;
            if(imageToolMode=="precision"){PrecisionMouseDown(e);return;}
            if(!cropModeActive)return;
            Rect bounds=RenderedImageRect();if(bounds.IsEmpty)return;Point point=ClampCropPoint(e.GetPosition(imageCanvas),bounds);cropDragMode=CropHitMode(point);cropSelecting=true;cropDragStart=point;cropDragInitial=cropRect;
            if(cropDragMode=="nw")cropResizeAnchor=new Point(cropRect.Right,cropRect.Bottom);else if(cropDragMode=="ne")cropResizeAnchor=new Point(cropRect.Left,cropRect.Bottom);else if(cropDragMode=="se")cropResizeAnchor=new Point(cropRect.Left,cropRect.Top);else if(cropDragMode=="sw")cropResizeAnchor=new Point(cropRect.Right,cropRect.Top);else if(cropDragMode=="create"){cropStart=point;cropRect=new Rect(point,point);}
            imageCanvas.CaptureMouse();e.Handled=true;
        }

        void CropMouseMove(object sender,MouseEventArgs e)
        {
            if(layerResizing){ResizePrecisionLayer(e);return;}
            if(layerDragging){MovePrecisionLayer(e);return;}
            if(imageToolMode=="precision"){PrecisionMouseMove(e);return;}
            if(!cropModeActive){imageCanvas.Cursor=Cursors.Arrow;return;}
            Point point=e.GetPosition(imageCanvas);if(!cropSelecting){string hover=CropHitMode(point);imageCanvas.Cursor=hover=="move"?Cursors.SizeAll:(hover=="nw"||hover=="se"?Cursors.SizeNWSE:(hover=="ne"||hover=="sw"?Cursors.SizeNESW:Cursors.Cross));return;}
            Rect bounds=RenderedImageRect();point=ClampCropPoint(point,bounds);
            if(cropDragMode=="move"){
                double x=cropDragInitial.X+point.X-cropDragStart.X,y=cropDragInitial.Y+point.Y-cropDragStart.Y;x=Math.Max(bounds.Left,Math.Min(x,bounds.Right-cropDragInitial.Width));y=Math.Max(bounds.Top,Math.Min(y,bounds.Bottom-cropDragInitial.Height));cropRect=new Rect(x,y,cropDragInitial.Width,cropDragInitial.Height);
            }else if(cropDragMode=="create"){cropRect=CreateCropRect(point);}else{cropStart=cropResizeAnchor;cropRect=CreateCropRect(point);}
            if(cropRect.Width>=2&&cropRect.Height>=2)ShowCrop();
        }

        void CropMouseUp(object sender,MouseButtonEventArgs e)
        {
            if(layerResizing){FinishLayerResize(e);return;}
            if(layerDragging){FinishLayerDrag(e);return;}
            if(imageToolMode=="precision"){PrecisionMouseUp(e);return;}
            if(!cropModeActive)return;
            if(!cropSelecting)return;cropSelecting=false;imageCanvas.ReleaseMouseCapture();if(cropRect.Width<12||cropRect.Height<12)InitializeCropBox();e.Handled=true;
        }

        Point ClampCropPoint(Point point,Rect bounds)
        {
            return new Point(Math.Max(bounds.Left,Math.Min(point.X,bounds.Right)),Math.Max(bounds.Top,Math.Min(point.Y,bounds.Bottom)));
        }

        Rect CreateCropRect(Point end)
        {
            Rect bounds=RenderedImageRect();if(bounds.IsEmpty)return Rect.Empty;end=ClampCropPoint(end,bounds);
            double dx=end.X-cropStart.X,dy=end.Y-cropStart.Y,w=Math.Abs(dx),h=Math.Abs(dy),ratio=SelectedCropRatio();
            if(ratio>0&&w>0&&h>0){if(w/h>ratio)w=h*ratio;else h=w/ratio;}
            double x=dx<0?cropStart.X-w:cropStart.X,y=dy<0?cropStart.Y-h:cropStart.Y;
            return new Rect(x,y,w,h);
        }

        double SelectedCropRatio()
        {
            if(customCropSizeActive&&customCropOutputWidth>0&&customCropOutputHeight>0)return (double)customCropOutputWidth/customCropOutputHeight;
            int selected=cropPresetBox==null?0:cropPresetBox.SelectedIndex;
            switch(selected){case 1:return 1;case 2:return 4.0/3;case 3:return 3.0/4;case 4:return 16.0/9;case 5:return 9.0/16;case 6:return 3.0/2;case 7:return 2.0/3;case 8:return 1;case 9:return 3840.0/2160;case 10:return 2560.0/1440;case 11:return 1920.0/1080;case 12:return 1280.0/720;case 13:return 1080.0/1920;case 14:return 1242.0/1660;case 15:return 750.0/460;case 16:return 1;default:return 0;}
        }

        bool TryGetCropOutputSize(out int width,out int height)
        {
            if(customCropSizeActive){width=customCropOutputWidth;height=customCropOutputHeight;return width>0&&height>0;}
            width=0;height=0;int selected=cropPresetBox==null?0:cropPresetBox.SelectedIndex;
            switch(selected){case 8:width=1080;height=1080;return true;case 9:width=3840;height=2160;return true;case 10:width=2560;height=1440;return true;case 11:width=1920;height=1080;return true;case 12:width=1280;height=720;return true;case 13:width=1080;height=1920;return true;case 14:width=1242;height=1660;return true;case 15:width=750;height=460;return true;case 16:width=800;height=800;return true;default:return false;}
        }

        void ApplyCustomCropSize()
        {
            int width,height;if(!Int32.TryParse(widthBox.Text,out width)||!Int32.TryParse(heightBox.Text,out height)||width<1||height<1||width>20000||height>20000){imageStatus.Text="请输入 1–20000 的裁切输出宽高";return;}
            if(cropPresetBox!=null)cropPresetBox.SelectedIndex=0;customCropOutputWidth=width;customCropOutputHeight=height;customCropSizeActive=true;cropModeActive=true;ClearImageTextLayer();imageCanvas.Cursor=Cursors.Cross;InitializeCropBox();imageStatus.Text="已生成 "+width+" × "+height+" 的裁切框；拖动调整后点击“确认裁剪”";
        }

        Rect RenderedImageRect()
        {
            if(workingBitmap==null||imageCanvas.ActualWidth<=0||imageCanvas.ActualHeight<=0)return Rect.Empty;
            double scale=Math.Min(imageCanvas.ActualWidth/workingBitmap.PixelWidth,imageCanvas.ActualHeight/workingBitmap.PixelHeight);double w=workingBitmap.PixelWidth*scale,h=workingBitmap.PixelHeight*scale;
            return new Rect((imageCanvas.ActualWidth-w)/2,(imageCanvas.ActualHeight-h)/2,w,h);
        }

        void ApplyCrop()
        {
            if(!cropModeActive){imageStatus.Text="请先点击“裁切”进入裁切模式";return;}
            if(workingBitmap==null||cropRect.IsEmpty||cropRect.Width<4||cropRect.Height<4){imageStatus.Text="请先在图片上拖出裁切区域";return;}
            Rect visible=Rect.Intersect(cropRect,RenderedImageRect());Rect rendered=RenderedImageRect();if(visible.IsEmpty)return;
            int x=(int)Math.Round((visible.X-rendered.X)/rendered.Width*workingBitmap.PixelWidth);int y=(int)Math.Round((visible.Y-rendered.Y)/rendered.Height*workingBitmap.PixelHeight);
            int w=(int)Math.Round(visible.Width/rendered.Width*workingBitmap.PixelWidth);int h=(int)Math.Round(visible.Height/rendered.Height*workingBitmap.PixelHeight);
            x=Math.Max(0,Math.Min(x,workingBitmap.PixelWidth-1));y=Math.Max(0,Math.Min(y,workingBitmap.PixelHeight-1));w=Math.Max(1,Math.Min(w,workingBitmap.PixelWidth-x));h=Math.Max(1,Math.Min(h,workingBitmap.PixelHeight-y));
            var cropped=new CroppedBitmap(workingBitmap,new Int32Rect(x,y,w,h));cropped.Freeze();BitmapSource result=cropped;int outputWidth,outputHeight;
            if(TryGetCropOutputSize(out outputWidth,out outputHeight))result=ResizeBitmap(result,outputWidth,outputHeight);
            workingBitmap=result;workingEncodedBytes=null;ClearCompressionCandidate();editorImage.Source=workingBitmap;widthBox.Text=workingBitmap.PixelWidth.ToString();heightBox.Text=workingBitmap.PixelHeight.ToString();ExitCropMode();ScheduleCompressionEstimate();PushImageState();
            imageStatus.Text=String.Format("已确认裁剪：{0} × {1} px，确认覆盖前不会写入原图",workingBitmap.PixelWidth,workingBitmap.PixelHeight);
        }

        BitmapSource ResizeBitmap(BitmapSource source,int width,int height)
        {
            var scaled=new TransformedBitmap(source,new ScaleTransform((double)width/source.PixelWidth,(double)height/source.PixelHeight));scaled.Freeze();return scaled;
        }

        byte[] EncodeBitmap(BitmapSource source,string format,int quality)
        {
            BitmapEncoder encoder;if(format=="JPEG")encoder=new JpegBitmapEncoder{QualityLevel=Math.Max(1,Math.Min(100,quality))};else encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(source));using(var stream=new MemoryStream()){encoder.Save(stream);return stream.ToArray();}
        }

        int JpegQualityForCompression(double intensity){return Math.Max(18,Math.Min(100,(int)Math.Round(100-intensity*0.86)));}

        BitmapSource FlattenTransparencyForJpeg(BitmapSource source)
        {
            var converted=new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0);int width=converted.PixelWidth,height=converted.PixelHeight,stride=width*4;byte[] pixels=new byte[stride*height];converted.CopyPixels(pixels,stride,0);
            for(int i=0;i<pixels.Length;i+=4){int alpha=pixels[i+3];if(alpha<255){pixels[i]=(byte)((pixels[i]*alpha+255*(255-alpha))/255);pixels[i+1]=(byte)((pixels[i+1]*alpha+255*(255-alpha))/255);pixels[i+2]=(byte)((pixels[i+2]*alpha+255*(255-alpha))/255);pixels[i+3]=255;}}
            var result=new WriteableBitmap(width,height,source.DpiX,source.DpiY,PixelFormats.Bgra32,null);result.WritePixels(new Int32Rect(0,0,width,height),pixels,stride,0);result.Freeze();return result;
        }

        BitmapSource QuantizePngForCompression(BitmapSource source,double intensity)
        {
            if(intensity<8)return source;int step=intensity<24?2:intensity<42?4:intensity<60?8:intensity<76?16:intensity<89?24:32;
            var converted=new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0);int width=converted.PixelWidth,height=converted.PixelHeight,stride=width*4;byte[] pixels=new byte[stride*height];converted.CopyPixels(pixels,stride,0);
            for(int i=0;i<pixels.Length;i+=4){for(int channel=0;channel<3;channel++){int value=pixels[i+channel],rounded=((value+step/2)/step)*step;pixels[i+channel]=(byte)Math.Min(255,rounded);}}
            var result=new WriteableBitmap(width,height,source.DpiX,source.DpiY,PixelFormats.Bgra32,null);result.WritePixels(new Int32Rect(0,0,width,height),pixels,stride,0);result.Freeze();return result;
        }

        byte[] EncodeForCompression(BitmapSource source,string format,double intensity,out BitmapSource encodedBitmap)
        {
            if(format=="JPEG"){encodedBitmap=FlattenTransparencyForJpeg(source);return EncodeBitmap(encodedBitmap,"JPEG",JpegQualityForCompression(intensity));}
            encodedBitmap=QuantizePngForCompression(source,intensity);return EncodeBitmap(encodedBitmap,"PNG",100);
        }

        long CurrentImageByteLength()
        {
            if(workingEncodedBytes!=null)return workingEncodedBytes.LongLength;if(editingImageItem!=null&&!String.IsNullOrWhiteSpace(editingImageItem.Value)&&File.Exists(editingImageItem.Value)&&workingBitmap==originalBitmap)return new FileInfo(editingImageItem.Value).Length;return 0;
        }

        void ClearCompressionCandidate(){compressionCandidateBytes=null;compressionCandidateBitmap=null;Interlocked.Increment(ref compressionEstimateVersion);}

        void ScheduleCompressionEstimate(){if(compressionEstimateTimer==null||workingBitmap==null)return;compressionCandidateBytes=null;compressionCandidateBitmap=null;Interlocked.Increment(ref compressionEstimateVersion);compressionEstimateTimer.Stop();compressionEstimateTimer.Start();}

        string CompressionEstimateText(string format,double intensity,BitmapSource candidate,byte[] bytes,long originalBytes)
        {
            string comparison=originalBytes>0?String.Format("原文件 {0:0.0} KB  →  预计 {1:0.0} KB · {2}",originalBytes/1024.0,bytes.Length/1024.0,bytes.Length<originalBytes?("节省 "+((1.0-bytes.Length/(double)originalBytes)*100).ToString("0.0")+"%"):"未缩小"):String.Format("预计输出 {0:0.0} KB",bytes.Length/1024.0);
            string detail=format=="JPEG"?("JPEG 质量 "+JpegQualityForCompression(intensity)+" · 透明区域铺白"):(intensity<8?"PNG 像素无损":"PNG 透明通道保留 · 颜色智能精简");return comparison+String.Format("\n分辨率锁定 {0} × {1} px · {2}",candidate.PixelWidth,candidate.PixelHeight,detail);
        }

        void BeginCompressionEstimate()
        {
            if(workingBitmap==null||compressionSlider==null)return;BitmapSource source=workingBitmap;if(!source.IsFrozen){source=source.Clone();source.Freeze();}string format=Convert.ToString(outputFormatBox.SelectedItem)??"PNG";double intensity=compressionSlider.Value;long originalBytes=CurrentImageByteLength();int version=compressionEstimateVersion;
            ThreadPool.QueueUserWorkItem(delegate{try{BitmapSource candidate;byte[] bytes=EncodeForCompression(source,format,intensity,out candidate);string message=CompressionEstimateText(format,intensity,candidate,bytes,originalBytes);UiPost(new Action(delegate{if(version!=compressionEstimateVersion)return;compressionCandidateBitmap=candidate;compressionCandidateBytes=bytes;if(compressionSizeText!=null)compressionSizeText.Text=message;}));}catch(Exception ex){UiPost(new Action(delegate{if(version==compressionEstimateVersion&&compressionSizeText!=null)compressionSizeText.Text="大小估算失败："+ex.Message;}));}});
        }

        void UpdateCompressionEstimate()
        {
            if(workingBitmap==null||compressionSlider==null)return;try{string format=Convert.ToString(outputFormatBox.SelectedItem)??"PNG";double intensity=compressionSlider.Value;BitmapSource candidate;byte[] bytes=EncodeForCompression(workingBitmap,format,intensity,out candidate);compressionCandidateBitmap=candidate;compressionCandidateBytes=bytes;
                long originalBytes=CurrentImageByteLength();compressionSizeText.Text=CompressionEstimateText(format,intensity,candidate,bytes,originalBytes);
            }catch(Exception ex){compressionSizeText.Text="大小估算失败："+ex.Message;}
        }

        void ConfirmCompressionAdjustment()
        {
            if(compressionCandidateBytes==null||compressionCandidateBitmap==null)UpdateCompressionEstimate();if(compressionCandidateBytes==null)return;
            workingEncodedBytes=compressionCandidateBytes;workingBitmap=LoadEditorBytes(workingEncodedBytes);editorImage.Source=workingBitmap;widthBox.Text=workingBitmap.PixelWidth.ToString();heightBox.Text=workingBitmap.PixelHeight.ToString();ResetCrop();PushImageState();
            imageStatus.Text=String.Format("已确认调整：{0} × {1} px · {2:0.0} KB；尚未覆盖原图",workingBitmap.PixelWidth,workingBitmap.PixelHeight,workingEncodedBytes.Length/1024.0);ClearCompressionCandidate();ScheduleCompressionEstimate();
        }

        void PrepareLosslessCompression()
        {
            if(workingBitmap==null){imageStatus.Text="请先打开或生成一张图片";return;}try{
                outputFormatBox.SelectedItem="PNG";compressionSlider.Value=0;if(compressionEstimateTimer!=null)compressionEstimateTimer.Stop();Interlocked.Increment(ref compressionEstimateVersion);
                byte[] candidate=EncodeBitmap(workingBitmap,"PNG",100),baseline=null;
                if(workingEncodedBytes!=null)baseline=workingEncodedBytes;else if(editingImageItem!=null&&workingBitmap==originalBitmap&&File.Exists(editingImageItem.Value))baseline=File.ReadAllBytes(editingImageItem.Value);else baseline=EncodeBitmap(workingBitmap,Convert.ToString(outputFormatBox.SelectedItem)??"PNG",92);
                if(candidate.Length>=baseline.Length){compressionSizeText.Text=String.Format("当前 {0:0.0} KB · 无损 PNG {1:0.0} KB",baseline.Length/1024.0,candidate.Length/1024.0);imageStatus.Text="像素无损版本没有更小，已保留当前较小数据；JPEG 无法在保持每个像素不变时靠调质量压缩";return;}
                compressionCandidateBytes=candidate;compressionCandidateBitmap=workingBitmap;compressionSizeText.Text=String.Format("无损候选：{0:0.0} KB → {1:0.0} KB · 像素尺寸不变",baseline.Length/1024.0,candidate.Length/1024.0);imageStatus.Text="已生成像素无损 PNG 候选，点击“应用到当前图片”或“另存压缩副本”";
            }catch(Exception ex){imageStatus.Text="无损压缩失败："+ex.Message;}
        }

        void CompressPreview()
        {
            int target;if(workingBitmap==null)return;if(!Int32.TryParse(targetKbBox.Text,out target)||target<1){imageStatus.Text="请输入目标大小 KB";return;}string format=Convert.ToString(outputFormatBox.SelectedItem)??"PNG";int limit=target*1024;byte[] best=null;BitmapSource bestBitmap=null;double bestIntensity=95;
            if(format=="JPEG"){int low=18,high=100,bestQuality=18;while(low<=high){int quality=(low+high)/2;BitmapSource bitmap=FlattenTransparencyForJpeg(workingBitmap);byte[] data=EncodeBitmap(bitmap,"JPEG",quality);if(data.Length<=limit){best=data;bestBitmap=bitmap;bestQuality=quality;low=quality+1;}else high=quality-1;}if(best==null){bestBitmap=FlattenTransparencyForJpeg(workingBitmap);best=EncodeBitmap(bestBitmap,"JPEG",18);bestQuality=18;}bestIntensity=Math.Max(0,Math.Min(95,(100-bestQuality)/0.86));}
            else {foreach(double intensity in new double[]{0,10,20,30,40,50,60,70,80,90,95}){BitmapSource bitmap;byte[] data=EncodeForCompression(workingBitmap,"PNG",intensity,out bitmap);best=data;bestBitmap=bitmap;bestIntensity=intensity;if(data.Length<=limit)break;}}
            compressionCandidateBytes=best;compressionCandidateBitmap=bestBitmap;compressionSlider.Value=bestIntensity;bool reached=best.Length<=limit;
            compressionSizeText.Text=String.Format("目标 {0} KB  →  {1:0.0} KB\n分辨率锁定 {2} × {3} px · 压缩强度 {4:0}%",target,best.Length/1024.0,workingBitmap.PixelWidth,workingBitmap.PixelHeight,bestIntensity);
            imageStatus.Text=reached?"已在保持分辨率不变的情况下匹配目标，点击“应用到当前图片”或“另存压缩副本”":"保持当前分辨率时无法达到目标大小；没有偷偷缩小图片，已提供当前最小候选";
        }

        void SaveCompressionCopy()
        {
            if(workingBitmap==null){imageStatus.Text="当前没有可以压缩的图片";return;}if(compressionCandidateBytes==null)UpdateCompressionEstimate();if(compressionCandidateBytes==null)return;string format=Convert.ToString(outputFormatBox.SelectedItem)??"PNG",extension=format=="JPEG"?".jpg":".png",baseName=editingImageItem==null?"博道咪图片":Path.GetFileNameWithoutExtension(editingImageItem.Name);
            var dialog=new Microsoft.Win32.SaveFileDialog{Title="另存压缩图片",Filter=format=="JPEG"?"JPEG 图片 (*.jpg)|*.jpg":"PNG 图片 (*.png)|*.png",DefaultExt=extension,FileName=baseName+"-compressed"+extension};if(dialog.ShowDialog()!=true)return;try{File.WriteAllBytes(dialog.FileName,compressionCandidateBytes);imageStatus.Text=String.Format("压缩副本已保存：{0:0.0} KB · 原图未改动",compressionCandidateBytes.Length/1024.0);}catch(Exception ex){imageStatus.Text="压缩副本保存失败："+ex.Message;}
        }

        void BatchCompressToZip()
        {
            var picker=new Microsoft.Win32.OpenFileDialog{Title="选择要批量压缩的图片",Filter="图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp|所有文件|*.*",Multiselect=true,CheckFileExists=true};if(picker.ShowDialog()!=true||picker.FileNames.Length==0)return;
            var save=new Microsoft.Win32.SaveFileDialog{Title="保存批量压缩包",Filter="ZIP 压缩包 (*.zip)|*.zip",DefaultExt=".zip",FileName="博道咪-压缩图片-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".zip"};if(save.ShowDialog()!=true)return;string[] files=picker.FileNames;string zipPath=save.FileName,format=Convert.ToString(outputFormatBox.SelectedItem)??"PNG";double intensity=compressionSlider.Value;imageStatus.Text="正在本地批量压缩 "+files.Length+" 张图片；不会上传网络…";
            ThreadPool.QueueUserWorkItem(delegate{int completed=0;var errors=new List<string>();try{using(var stream=new FileStream(zipPath,FileMode.Create,FileAccess.Write,FileShare.None))using(var archive=new ZipArchive(stream,ZipArchiveMode.Create)){var usedNames=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(string file in files){try{BitmapSource bitmap=LoadEditorBitmap(file),encodedBitmap;byte[] data=EncodeForCompression(bitmap,format,intensity,out encodedBitmap);string name=Path.GetFileNameWithoutExtension(file)+(format=="JPEG"?".jpg":".png"),candidate=name;int suffix=2;while(!usedNames.Add(candidate)){candidate=Path.GetFileNameWithoutExtension(name)+"-"+suffix+(format=="JPEG"?".jpg":".png");suffix++;}var entry=archive.CreateEntry(candidate,CompressionLevel.Optimal);using(var output=entry.Open())output.Write(data,0,data.Length);completed++;}catch(Exception ex){errors.Add(Path.GetFileName(file)+"："+ex.Message);}}}}catch(Exception ex){errors.Add(ex.Message);}UiPost(new Action(delegate{imageStatus.Text=errors.Count==0?("批量压缩完成："+completed+" 张 · 原图未改动 · "+zipPath):("批量压缩完成 "+completed+" 张，失败 "+errors.Count+" 张；"+String.Join("；",errors.Take(2).ToArray()));}));});
        }
}
}
