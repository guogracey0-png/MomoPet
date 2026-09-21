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

        DataTemplate BuildPrecisionLayerItemTemplate()
        {
            var template=new DataTemplate(typeof(PrecisionLayer));var row=new FrameworkElementFactory(typeof(StackPanel));row.SetValue(StackPanel.OrientationProperty,Orientation.Horizontal);row.SetValue(FrameworkElement.MarginProperty,new Thickness(5,3,5,3));var thumbnail=new FrameworkElementFactory(typeof(Border));thumbnail.SetValue(FrameworkElement.WidthProperty,46.0);thumbnail.SetValue(FrameworkElement.HeightProperty,40.0);thumbnail.SetValue(Border.BackgroundProperty,Ui.Inner);thumbnail.SetValue(Border.BorderBrushProperty,Ui.Line);thumbnail.SetValue(Border.BorderThicknessProperty,new Thickness(1));var image=new FrameworkElementFactory(typeof(Image));image.SetValue(Image.StretchProperty,Stretch.Uniform);image.SetBinding(Image.SourceProperty,new Binding("Bitmap"));thumbnail.AppendChild(image);row.AppendChild(thumbnail);var details=new FrameworkElementFactory(typeof(StackPanel));details.SetValue(FrameworkElement.WidthProperty,178.0);details.SetValue(StackPanel.VerticalAlignmentProperty,VerticalAlignment.Center);details.SetValue(FrameworkElement.MarginProperty,new Thickness(7,0,0,0));var name=new FrameworkElementFactory(typeof(TextBlock));name.SetBinding(TextBlock.TextProperty,new Binding("Name"));name.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);name.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);details.AppendChild(name);var size=new FrameworkElementFactory(typeof(TextBlock));size.SetBinding(TextBlock.TextProperty,new Binding("SizeLabel"));size.SetValue(TextBlock.FontSizeProperty,10.0);size.SetValue(TextBlock.ForegroundProperty,Ui.SubInk);details.AppendChild(size);row.AppendChild(details);template.VisualTree=row;return template;
        }

        void SetPrecisionModeFromUi()
        {
            if(precisionModeCombo==null)return;string[] modes={"point","bbox","lasso","doodle","arrow"};int index=Math.Max(0,Math.Min(modes.Length-1,precisionModeCombo.SelectedIndex));precisionMode=modes[index];ClearPrecisionMarks();
            if(imageCanvas!=null)imageCanvas.Cursor=precisionMarkingActive?(precisionMode=="point"?Cursors.Cross:Cursors.Pen):Cursors.Arrow;
            if(precisionStatus!=null)precisionStatus.Text=precisionMarkingActive?(precisionMode=="point"?"请在图片上单击需要修改的位置。":precisionMode=="bbox"?"请在图片上拖出需要修改的矩形区域。":"请在图片上绘制标记；完成后点击“提交精确编辑”。"):"已选择标记方式；点击“开始精确编辑”后再在图片上标记。";
        }

        void BeginPrecisionMarking()
        {
            if(workingBitmap==null){imageStatus.Text="请先打开或生成一张图片";return;}if(imageToolMode!="precision")ShowImageToolMode("precision");precisionMarkingActive=true;ClearPrecisionMarks();SetPrecisionModeFromUi();imageStatus.Text="已进入局部编辑标记状态；完成标记后点击“提交局部编辑”。";
        }

        void ClearPrecisionMarks()
        {
            precisionSelecting=false;precisionSelectionRect=Rect.Empty;precisionPoint=new Point(Double.NaN,Double.NaN);precisionPath.Clear();
            if(imageCanvas!=null)imageCanvas.ReleaseMouseCapture();LayoutPrecisionOverlay();
        }

        Point ClampPrecisionPoint(Point point)
        {
            Rect bounds=RenderedImageRect();return bounds.IsEmpty?point:new Point(Math.Max(bounds.Left,Math.Min(point.X,bounds.Right)),Math.Max(bounds.Top,Math.Min(point.Y,bounds.Bottom)));
        }

        void PrecisionMouseDown(MouseButtonEventArgs e)
        {
            if(!precisionMarkingActive)return;
            if(workingBitmap==null){imageStatus.Text="请先打开或生成一张图片";return;}Rect bounds=RenderedImageRect();Point point=ClampPrecisionPoint(e.GetPosition(imageCanvas));if(bounds.IsEmpty||!bounds.Contains(point))return;
            ClearImageTextLayer();precisionSelecting=true;
            if(precisionMode=="point"){precisionPoint=point;precisionPath.Clear();precisionSelecting=false;precisionMarkingActive=false;imageCanvas.Cursor=Cursors.Arrow;imageStatus.Text="已标记点选坐标 "+PrecisionPointTag()+"；点击“提交精确编辑”发送。";}
            else if(precisionMode=="bbox"){precisionPoint=point;precisionSelectionRect=new Rect(point,point);}
            else {precisionPath.Clear();precisionPath.Add(point);}
            if(precisionSelecting)imageCanvas.CaptureMouse();LayoutPrecisionOverlay();e.Handled=true;
        }

        void PrecisionMouseMove(MouseEventArgs e)
        {
            if(!precisionSelecting)return;Point point=ClampPrecisionPoint(e.GetPosition(imageCanvas));if(precisionMode=="bbox")precisionSelectionRect=new Rect(precisionPoint,point);else if(precisionMode!="point"&&(precisionPath.Count==0||DistanceSquared(precisionPath[precisionPath.Count-1],point)>3))precisionPath.Add(point);LayoutPrecisionOverlay();e.Handled=true;
        }

        double DistanceSquared(Point a,Point b){double x=a.X-b.X,y=a.Y-b.Y;return x*x+y*y;}

        void PrecisionMouseUp(MouseButtonEventArgs e)
        {
            if(!precisionSelecting)return;precisionSelecting=false;imageCanvas.ReleaseMouseCapture();if(precisionMode=="bbox"&&(precisionSelectionRect.Width<5||precisionSelectionRect.Height<5))precisionSelectionRect=Rect.Empty;LayoutPrecisionOverlay();
            precisionMarkingActive=false;imageCanvas.Cursor=Cursors.Arrow;if(precisionMode=="bbox"&&!precisionSelectionRect.IsEmpty)imageStatus.Text="已标记框选坐标 "+PrecisionBoxTag()+"；点击“提交精确编辑”发送。";else if((precisionMode=="lasso"||precisionMode=="doodle"||precisionMode=="arrow")&&precisionPath.Count>1)imageStatus.Text="已标记区域；点击“提交精确编辑”发送。";e.Handled=true;
        }

        void LayoutPrecisionOverlay()
        {
            if(precisionOverlay==null)return;precisionOverlay.Width=imageCanvas.ActualWidth;precisionOverlay.Height=imageCanvas.ActualHeight;precisionOverlay.Children.Clear();
            if(!precisionSelectionRect.IsEmpty){precisionBoxOverlay.Visibility=Visibility.Visible;precisionBoxOverlay.Width=precisionSelectionRect.Width;precisionBoxOverlay.Height=precisionSelectionRect.Height;Canvas.SetLeft(precisionBoxOverlay,precisionSelectionRect.Left);Canvas.SetTop(precisionBoxOverlay,precisionSelectionRect.Top);precisionOverlay.Children.Add(precisionBoxOverlay);}else precisionBoxOverlay.Visibility=Visibility.Collapsed;
            var red=new SolidColorBrush(Color.FromRgb(255,75,58));if(!Double.IsNaN(precisionPoint.X)&&precisionMode=="point"){var dot=new System.Windows.Shapes.Ellipse{Width=18,Height=18,Fill=new SolidColorBrush(Color.FromArgb(70,255,75,58)),Stroke=red,StrokeThickness=3};Canvas.SetLeft(dot,precisionPoint.X-9);Canvas.SetTop(dot,precisionPoint.Y-9);precisionOverlay.Children.Add(dot);}
            if(precisionPath.Count>1){var line=new System.Windows.Shapes.Polyline{Stroke=red,StrokeThickness=4,StrokeLineJoin=PenLineJoin.Round,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round};foreach(Point p in precisionPath)line.Points.Add(p);precisionOverlay.Children.Add(line);if(precisionMode=="arrow")AddPrecisionArrowHead(precisionOverlay,precisionPath[precisionPath.Count-2],precisionPath[precisionPath.Count-1],red);}
        }

        void LayoutPrecisionLayers()
        {
            if(precisionLayerCanvas==null)return;precisionLayerCanvas.Width=imageCanvas.ActualWidth;precisionLayerCanvas.Height=imageCanvas.ActualHeight;precisionLayerCanvas.Children.Clear();Rect display=RenderedImageRect();if(display.IsEmpty||workingBitmap==null||!layerCompositionActive){if(layerSelectionBox!=null)layerSelectionBox.Visibility=Visibility.Collapsed;if(layerResizeHandles!=null)foreach(var handle in layerResizeHandles)handle.Visibility=Visibility.Collapsed;return;}
            foreach(PrecisionLayer layer in precisionLayers.OrderBy(x=>x.Z)){if(!layer.Visible||layer.Bitmap==null)continue;var visual=new Image{Source=layer.Bitmap,Stretch=Stretch.Fill,IsHitTestVisible=false};layer.Visual=visual;double x=display.Left+layer.X/workingBitmap.PixelWidth*display.Width,y=display.Top+layer.Y/workingBitmap.PixelHeight*display.Height,w=Math.Max(2,layer.Width/workingBitmap.PixelWidth*display.Width),h=Math.Max(2,layer.Height/workingBitmap.PixelHeight*display.Height);visual.Width=w;visual.Height=h;Canvas.SetLeft(visual,x);Canvas.SetTop(visual,y);Panel.SetZIndex(visual,layer.Z);precisionLayerCanvas.Children.Add(visual);}
            if(selectedPrecisionLayer!=null&&selectedPrecisionLayer.Visible){double x=display.Left+selectedPrecisionLayer.X/workingBitmap.PixelWidth*display.Width,y=display.Top+selectedPrecisionLayer.Y/workingBitmap.PixelHeight*display.Height,w=Math.Max(2,selectedPrecisionLayer.Width/workingBitmap.PixelWidth*display.Width),h=Math.Max(2,selectedPrecisionLayer.Height/workingBitmap.PixelHeight*display.Height);layerSelectionBox.Visibility=Visibility.Visible;layerSelectionBox.Width=w;layerSelectionBox.Height=h;Canvas.SetLeft(layerSelectionBox,x);Canvas.SetTop(layerSelectionBox,y);if(!precisionOverlay.Children.Contains(layerSelectionBox))precisionOverlay.Children.Add(layerSelectionBox);PlaceLayerResizeHandle(0,x,y);PlaceLayerResizeHandle(1,x+w,y);PlaceLayerResizeHandle(2,x+w,y+h);PlaceLayerResizeHandle(3,x,y+h);}else{if(layerSelectionBox!=null)layerSelectionBox.Visibility=Visibility.Collapsed;if(layerResizeHandles!=null)foreach(var handle in layerResizeHandles)handle.Visibility=Visibility.Collapsed;}
        }

        void PlaceLayerResizeHandle(int index,double x,double y){if(layerResizeHandles==null||index<0||index>=layerResizeHandles.Length)return;var handle=layerResizeHandles[index];handle.Visibility=Visibility.Visible;Canvas.SetLeft(handle,x-handle.Width/2);Canvas.SetTop(handle,y-handle.Height/2);if(!precisionOverlay.Children.Contains(handle))precisionOverlay.Children.Add(handle);}

        PrecisionLayer PrecisionLayerAt(Point point)
        {
            if(!layerCompositionActive||workingBitmap==null)return null;Rect display=RenderedImageRect();if(display.IsEmpty)return null;double x=(point.X-display.Left)/display.Width*workingBitmap.PixelWidth,y=(point.Y-display.Top)/display.Height*workingBitmap.PixelHeight;return precisionLayers.Where(l=>l.Visible&&x>=l.X&&x<=l.X+l.Width&&y>=l.Y&&y<=l.Y+l.Height).OrderByDescending(l=>l.Z).FirstOrDefault();
        }

        bool TryStartLayerDrag(MouseButtonEventArgs e)
        {
            Point point=e.GetPosition(imageCanvas);string resize=LayerResizeHitMode(point);if(resize!=null&&selectedPrecisionLayer!=null){StartLayerResize(resize,point);e.Handled=true;return true;}PrecisionLayer layer=PrecisionLayerAt(point);if(layer==null)return false;selectedPrecisionLayer=layer;layerDragging=true;layerDragStart=point;layerDragX=layer.X;layerDragY=layer.Y;imageCanvas.CaptureMouse();RefreshPrecisionLayerList();LayoutPrecisionLayers();e.Handled=true;return true;
        }

        void MovePrecisionLayer(MouseEventArgs e)
        {
            if(selectedPrecisionLayer==null||workingBitmap==null)return;Rect display=RenderedImageRect();Point now=e.GetPosition(imageCanvas);selectedPrecisionLayer.X=Math.Max(-selectedPrecisionLayer.Width/2,Math.Min(workingBitmap.PixelWidth-selectedPrecisionLayer.Width/2,layerDragX+(now.X-layerDragStart.X)/Math.Max(1,display.Width)*workingBitmap.PixelWidth));selectedPrecisionLayer.Y=Math.Max(-selectedPrecisionLayer.Height/2,Math.Min(workingBitmap.PixelHeight-selectedPrecisionLayer.Height/2,layerDragY+(now.Y-layerDragStart.Y)/Math.Max(1,display.Height)*workingBitmap.PixelHeight));LayoutPrecisionLayers();e.Handled=true;
        }

        void FinishLayerDrag(MouseButtonEventArgs e){layerDragging=false;imageCanvas.ReleaseMouseCapture();LayoutPrecisionLayers();if(precisionStatus!=null)precisionStatus.Text="图层位置已调整；保存时会按当前画布合成。";e.Handled=true;}

        string LayerResizeHitMode(Point point){if(selectedPrecisionLayer==null||workingBitmap==null||!selectedPrecisionLayer.Visible)return null;Rect display=RenderedImageRect();double x=display.Left+selectedPrecisionLayer.X/workingBitmap.PixelWidth*display.Width,y=display.Top+selectedPrecisionLayer.Y/workingBitmap.PixelHeight*display.Height,w=selectedPrecisionLayer.Width/workingBitmap.PixelWidth*display.Width,h=selectedPrecisionLayer.Height/workingBitmap.PixelHeight*display.Height,hit=13;if(Math.Abs(point.X-x)<=hit&&Math.Abs(point.Y-y)<=hit)return "nw";if(Math.Abs(point.X-(x+w))<=hit&&Math.Abs(point.Y-y)<=hit)return "ne";if(Math.Abs(point.X-(x+w))<=hit&&Math.Abs(point.Y-(y+h))<=hit)return "se";if(Math.Abs(point.X-x)<=hit&&Math.Abs(point.Y-(y+h))<=hit)return "sw";return null;}

        void StartLayerResize(string mode,Point point){Rect display=RenderedImageRect();layerResizeMode=mode;layerResizing=true;if(mode=="nw")layerResizeAnchor=new Point(selectedPrecisionLayer.X+selectedPrecisionLayer.Width,selectedPrecisionLayer.Y+selectedPrecisionLayer.Height);else if(mode=="ne")layerResizeAnchor=new Point(selectedPrecisionLayer.X,selectedPrecisionLayer.Y+selectedPrecisionLayer.Height);else if(mode=="se")layerResizeAnchor=new Point(selectedPrecisionLayer.X,selectedPrecisionLayer.Y);else layerResizeAnchor=new Point(selectedPrecisionLayer.X+selectedPrecisionLayer.Width,selectedPrecisionLayer.Y);imageCanvas.CaptureMouse();imageCanvas.Cursor=(mode=="nw"||mode=="se")?Cursors.SizeNWSE:Cursors.SizeNESW;}

        void ResizePrecisionLayer(MouseEventArgs e){if(selectedPrecisionLayer==null||workingBitmap==null)return;Rect display=RenderedImageRect();Point p=e.GetPosition(imageCanvas);double px=(p.X-display.Left)/Math.Max(1,display.Width)*workingBitmap.PixelWidth,py=(p.Y-display.Top)/Math.Max(1,display.Height)*workingBitmap.PixelHeight;double left=Math.Min(px,layerResizeAnchor.X),top=Math.Min(py,layerResizeAnchor.Y),width=Math.Abs(px-layerResizeAnchor.X),height=Math.Abs(py-layerResizeAnchor.Y);if(width<12)width=12;if(height<12)height=12;if(layerResizeMode=="nw"||layerResizeMode=="sw")left=layerResizeAnchor.X-width;if(layerResizeMode=="nw"||layerResizeMode=="ne")top=layerResizeAnchor.Y-height;selectedPrecisionLayer.X=left;selectedPrecisionLayer.Y=top;selectedPrecisionLayer.Width=width;selectedPrecisionLayer.Height=height;LayoutPrecisionLayers();}

        void FinishLayerResize(MouseButtonEventArgs e){layerResizing=false;layerResizeMode=null;imageCanvas.Cursor=Cursors.Arrow;imageCanvas.ReleaseMouseCapture();RefreshPrecisionLayerList();LayoutPrecisionLayers();if(precisionStatus!=null)precisionStatus.Text="图层大小已调整；可继续拖动移动或调整前后层级。";e.Handled=true;}

        void RefreshPrecisionLayerList(){if(precisionLayerList==null)return;precisionLayerList.ItemsSource=null;precisionLayerList.ItemsSource=precisionLayers.OrderByDescending(x=>x.Z).ToList();precisionLayerList.Visibility=precisionLayers.Count>0?Visibility.Visible:Visibility.Collapsed;if(selectedPrecisionLayer!=null){precisionLayerList.SelectedItem=selectedPrecisionLayer;SyncSelectedLayerScale();}}

        void SyncSelectedLayerScale(){if(precisionLayerScale==null)return;double scale=100;if(selectedPrecisionLayer!=null&&selectedPrecisionLayer.NaturalWidth>0)scale=selectedPrecisionLayer.Width/selectedPrecisionLayer.NaturalWidth*100;precisionLayerScale.Value=Math.Max(precisionLayerScale.Minimum,Math.Min(precisionLayerScale.Maximum,scale));if(precisionLayerScaleText!=null)precisionLayerScaleText.Text=Math.Round(scale)+"%";}

        void ApplySelectedLayerScale(){if(precisionLayerScaleText!=null)precisionLayerScaleText.Text=Math.Round(precisionLayerScale.Value)+"%";if(selectedPrecisionLayer==null||selectedPrecisionLayer.NaturalWidth<=0)return;double scale=precisionLayerScale.Value/100;double oldWidth=selectedPrecisionLayer.Width,oldHeight=selectedPrecisionLayer.Height;selectedPrecisionLayer.Width=selectedPrecisionLayer.NaturalWidth*scale;selectedPrecisionLayer.Height=selectedPrecisionLayer.NaturalHeight*scale;selectedPrecisionLayer.X-=(selectedPrecisionLayer.Width-oldWidth)/2;selectedPrecisionLayer.Y-=(selectedPrecisionLayer.Height-oldHeight)/2;LayoutPrecisionLayers();}

        void NudgeSelectedLayerScale(double amount){if(selectedPrecisionLayer==null||precisionLayerScale==null)return;precisionLayerScale.Value=Math.Max(precisionLayerScale.Minimum,Math.Min(precisionLayerScale.Maximum,precisionLayerScale.Value+amount));RefreshPrecisionLayerList();}

        void MoveSelectedPrecisionLayer(int direction){if(selectedPrecisionLayer==null)return;int z=selectedPrecisionLayer.Z+direction;PrecisionLayer swap=precisionLayers.FirstOrDefault(x=>x.Z==z);if(swap==null)return;swap.Z=selectedPrecisionLayer.Z;selectedPrecisionLayer.Z=z;RefreshPrecisionLayerList();LayoutPrecisionLayers();}

        void ToggleSelectedPrecisionLayer(){if(selectedPrecisionLayer==null)return;selectedPrecisionLayer.Visible=!selectedPrecisionLayer.Visible;selectedPrecisionLayer.Name=(selectedPrecisionLayer.Visible?"◉ ":"○ ")+selectedPrecisionLayer.Name.TrimStart('◉','○',' ');RefreshPrecisionLayerList();LayoutPrecisionLayers();}

        BitmapSource RenderPrecisionLayerComposition()
        {
            if(!layerCompositionActive||workingBitmap==null)return workingBitmap;var visual=new DrawingVisual();using(DrawingContext context=visual.RenderOpen()){context.DrawImage(workingBitmap,new Rect(0,0,workingBitmap.PixelWidth,workingBitmap.PixelHeight));foreach(PrecisionLayer layer in precisionLayers.Where(x=>x.Visible&&x.Bitmap!=null).OrderBy(x=>x.Z))context.DrawImage(layer.Bitmap,new Rect(layer.X,layer.Y,layer.Width,layer.Height));}var bitmap=new RenderTargetBitmap(workingBitmap.PixelWidth,workingBitmap.PixelHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();return bitmap;
        }

        void RestorePreLayerSplitImage()
        {
            if(preLayerSplitBitmap==null){imageStatus.Text="还没有可恢复的拆分前图片";return;}
            try
            {
                workingEncodedBytes=preLayerSplitEncodedBytes;
                workingBitmap=preLayerSplitEncodedBytes!=null?LoadEditorBytes(preLayerSplitEncodedBytes):preLayerSplitBitmap;
                layerCompositionActive=false;layerDragging=false;layerResizing=false;selectedPrecisionLayer=null;precisionLayers.Clear();precisionResultPaths.Clear();
                if(precisionResultBox!=null){precisionResultBox.ItemsSource=null;precisionResultBox.Visibility=Visibility.Collapsed;}
                if(precisionLayerList!=null){precisionLayerList.ItemsSource=null;precisionLayerList.Visibility=Visibility.Collapsed;}
                ClearPrecisionMarks();ClearImageTextLayer();ExitCropMode();editorImage.Source=workingBitmap;widthBox.Text=workingBitmap.PixelWidth.ToString();heightBox.Text=workingBitmap.PixelHeight.ToString();ScheduleCompressionEstimate();LayoutPrecisionLayers();ResetImageHistory();
                imageStatus.Text="已恢复拆分前原图；已生成的图层文件不会被删除。";
                if(precisionStatus!=null)precisionStatus.Text="当前是拆分前原图；如需再次拆分可重新发起任务。";
            }
            catch(Exception ex){imageStatus.Text="恢复原图失败："+ex.Message;}
        }

        void ExportPrecisionLayersZip()
        {
            if(!EnsurePrecisionLayersForExport())return;var dialog=new Microsoft.Win32.SaveFileDialog{Filter="ZIP 图层包 (*.zip)|*.zip",FileName="博道咪-图层-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".zip"};if(dialog.ShowDialog()!=true)return;
            try
            {
                using(var stream=File.Create(dialog.FileName))using(var archive=new ZipArchive(stream,ZipArchiveMode.Create))
                {
                    WriteZipBitmap(archive,"预览-当前合成.png",RenderPrecisionLayerComposition());
                    int index=1;foreach(PrecisionLayer layer in precisionLayers.OrderBy(x=>x.Z)){WriteZipLayerPng(archive,"图层/"+index.ToString("D2")+"-"+SafeExportName(layer.Name)+".png",layer);index++;}
                    var info=archive.CreateEntry("图层说明.txt",CompressionLevel.Optimal);using(var writer=new StreamWriter(info.Open(),new UTF8Encoding(true))){writer.WriteLine("博道咪图层包");writer.WriteLine("预览-当前合成.png 为按当前图层位置与顺序合成的预览图。");index=1;foreach(PrecisionLayer layer in precisionLayers.OrderBy(x=>x.Z)){writer.WriteLine(String.Format("{0:D2}. {1} | x={2:0.##}, y={3:0.##}, 宽={4:0.##}, 高={5:0.##}, {6}",index,layer.Name,layer.X,layer.Y,layer.Width,layer.Height,layer.Visible?"显示":"隐藏"));index++;}}
                }
                imageStatus.Text="已导出 ZIP 图层包："+Path.GetFileName(dialog.FileName);
            }
            catch(Exception ex){imageStatus.Text="导出 ZIP 图层包失败："+ex.Message;}
        }

        void WriteZipBitmap(ZipArchive archive,string name,BitmapSource bitmap)
        {
            if(bitmap==null)return;var entry=archive.CreateEntry(name,CompressionLevel.Optimal);using(var encoded=new MemoryStream()){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));encoder.Save(encoded);encoded.Position=0;using(var output=entry.Open()){encoded.CopyTo(output);}}
        }

        void WriteZipLayerPng(ZipArchive archive,string name,PrecisionLayer layer)
        {
            if(layer==null)return;var entry=archive.CreateEntry(name,CompressionLevel.Optimal);using(var output=entry.Open()){
                // 分层结果的原始 PNG 已带 Alpha，直接复制可避免任何重编码损失。
                if(!String.IsNullOrWhiteSpace(layer.Path)&&File.Exists(layer.Path)&&String.Equals(Path.GetExtension(layer.Path),".png",StringComparison.OrdinalIgnoreCase)){using(var source=File.OpenRead(layer.Path))source.CopyTo(output);}
                else {using(var encoded=new MemoryStream()){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(layer.Bitmap));encoder.Save(encoded);encoded.Position=0;encoded.CopyTo(output);}}
            }
        }

        void ExportSelectedPrecisionLayerPng()
        {
            if(selectedPrecisionLayer==null){imageStatus.Text="请先在图层列表中选中要下载的图层。";return;}var dialog=new Microsoft.Win32.SaveFileDialog{Filter="PNG 图片 (*.png)|*.png",FileName=SafeExportName(selectedPrecisionLayer.Name)+".png"};if(dialog.ShowDialog()!=true)return;
            try{
                if(!String.IsNullOrWhiteSpace(selectedPrecisionLayer.Path)&&File.Exists(selectedPrecisionLayer.Path)&&String.Equals(Path.GetExtension(selectedPrecisionLayer.Path),".png",StringComparison.OrdinalIgnoreCase))File.Copy(selectedPrecisionLayer.Path,dialog.FileName,true);
                else {using(var output=File.Create(dialog.FileName)){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(selectedPrecisionLayer.Bitmap));encoder.Save(output);}}
                imageStatus.Text="已下载透明 PNG 图层："+Path.GetFileName(dialog.FileName);
            }catch(Exception ex){imageStatus.Text="下载图层失败："+ex.Message;}
        }

        bool EnsurePrecisionLayersForExport()
        {
            if(!layerCompositionActive||precisionLayers.Count==0){imageStatus.Text="请先完成图层拆分，再导出图层。";return false;}if(workingBitmap==null){imageStatus.Text="当前没有可导出的底图。";return false;}return true;
        }

        string SafeExportName(string value)
        {
            string name=String.IsNullOrWhiteSpace(value)?"图层":value.Trim();foreach(char bad in Path.GetInvalidFileNameChars())name=name.Replace(bad,'_');name=name.Replace('◉',' ').Replace('○',' ').Trim();return String.IsNullOrWhiteSpace(name)?"图层":name;
        }

        void ExportPrecisionLayersPsd()
        {
            if(!EnsurePrecisionLayersForExport())return;var dialog=new Microsoft.Win32.SaveFileDialog{Filter="Photoshop 图层文件 (*.psd)|*.psd",FileName="博道咪-图层-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".psd"};if(dialog.ShowDialog()!=true)return;
            try{WritePrecisionPsd(dialog.FileName);imageStatus.Text="已导出可编辑 PSD："+Path.GetFileName(dialog.FileName)+"（可在 Photoshop 中继续移动、缩放和调整图层顺序）。";}
            catch(Exception ex){imageStatus.Text="导出 PSD 失败："+ex.Message;}
        }

        List<PsdExportLayer> BuildPsdExportLayers()
        {
            var layers=new List<PsdExportLayer>();foreach(PrecisionLayer layer in precisionLayers.OrderByDescending(x=>x.Z))layers.Add(new PsdExportLayer{Name=SafeExportName(layer.Name),Bitmap=RasterizePrecisionLayer(layer),Visible=layer.Visible});layers.Add(new PsdExportLayer{Name="底图",Bitmap=workingBitmap,Visible=true});return layers;
        }

        BitmapSource RasterizePrecisionLayer(PrecisionLayer layer)
        {
            var visual=new DrawingVisual();using(DrawingContext context=visual.RenderOpen()){context.DrawImage(layer.Bitmap,new Rect(layer.X,layer.Y,layer.Width,layer.Height));}var bitmap=new RenderTargetBitmap(workingBitmap.PixelWidth,workingBitmap.PixelHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();return bitmap;
        }

        void WritePrecisionPsd(string path)
        {
            int width=workingBitmap.PixelWidth,height=workingBitmap.PixelHeight;if(width<1||height<1||width>30000||height>30000)throw new InvalidOperationException("当前图片尺寸不支持 PSD 导出。");var layers=BuildPsdExportLayers();BitmapSource composite=RenderPrecisionLayerComposition();
            using(var file=File.Create(path))using(var writer=new BinaryWriter(file,Encoding.ASCII))
            {
                WriteAscii(writer,"8BPS");WritePsdU16(writer,1);writer.Write(new byte[6]);WritePsdU16(writer,4);WritePsdI32(writer,height);WritePsdI32(writer,width);WritePsdU16(writer,8);WritePsdU16(writer,3);WritePsdI32(writer,0);WritePsdI32(writer,0);
                byte[] layerInfo=BuildPsdLayerInfo(layers,width,height);using(var section=new MemoryStream())using(var sectionWriter=new BinaryWriter(section,Encoding.ASCII)){WritePsdI32(sectionWriter,layerInfo.Length);sectionWriter.Write(layerInfo);if(section.Length%2!=0)sectionWriter.Write((byte)0);WritePsdI32(sectionWriter,0);sectionWriter.Flush();WritePsdI32(writer,(int)section.Length);writer.Write(section.ToArray());}
                WritePsdComposite(writer,composite,width,height);
            }
        }

        byte[] BuildPsdLayerInfo(List<PsdExportLayer> layers,int width,int height)
        {
            using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream,Encoding.ASCII))
            {
                WritePsdU16(writer,layers.Count);foreach(PsdExportLayer layer in layers){WritePsdI32(writer,0);WritePsdI32(writer,0);WritePsdI32(writer,height);WritePsdI32(writer,width);WritePsdU16(writer,4);for(int channel=0;channel<3;channel++){WritePsdI16(writer,(short)channel);WritePsdI32(writer,2+width*height);}WritePsdI16(writer,-1);WritePsdI32(writer,2+width*height);WriteAscii(writer,"8BIM");WriteAscii(writer,"norm");writer.Write((byte)255);writer.Write((byte)0);writer.Write((byte)(layer.Visible?0:2));writer.Write((byte)0);byte[] extra=BuildPsdLayerExtra(layer.Name);WritePsdI32(writer,extra.Length);writer.Write(extra);}
                foreach(PsdExportLayer layer in layers){byte[][] planes=PsdPlanes(layer.Bitmap,width,height);for(int channel=0;channel<4;channel++){WritePsdU16(writer,0);writer.Write(planes[channel]);}}
                writer.Flush();return stream.ToArray();
            }
        }

        byte[] BuildPsdLayerExtra(string name)
        {
            using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream,Encoding.ASCII)){WritePsdI32(writer,0);WritePsdI32(writer,0);byte[] bytes=Encoding.ASCII.GetBytes("Layer");writer.Write((byte)bytes.Length);writer.Write(bytes);while(stream.Length%4!=0)writer.Write((byte)0);writer.Flush();return stream.ToArray();}
        }

        byte[][] PsdPlanes(BitmapSource source,int width,int height)
        {
            var converted=new FormatConvertedBitmap();converted.BeginInit();converted.Source=source;converted.DestinationFormat=PixelFormats.Bgra32;converted.EndInit();int stride=width*4;byte[] pixels=new byte[stride*height];converted.CopyPixels(new Int32Rect(0,0,width,height),pixels,stride,0);byte[][] result={new byte[width*height],new byte[width*height],new byte[width*height],new byte[width*height]};for(int i=0,p=0;i<pixels.Length;i+=4,p++){result[0][p]=pixels[i+2];result[1][p]=pixels[i+1];result[2][p]=pixels[i];result[3][p]=pixels[i+3];}return result;
        }

        void WritePsdComposite(BinaryWriter writer,BitmapSource source,int width,int height){byte[][] planes=PsdPlanes(source,width,height);WritePsdU16(writer,0);for(int channel=0;channel<4;channel++)writer.Write(planes[channel]);}

        void WriteAscii(BinaryWriter writer,string text){writer.Write(Encoding.ASCII.GetBytes(text));}

        void WritePsdU16(BinaryWriter writer,int value){writer.Write((byte)((value>>8)&255));writer.Write((byte)(value&255));}

        void WritePsdI16(BinaryWriter writer,short value){WritePsdU16(writer,(ushort)value);}

        void WritePsdI32(BinaryWriter writer,int value){writer.Write((byte)((value>>24)&255));writer.Write((byte)((value>>16)&255));writer.Write((byte)((value>>8)&255));writer.Write((byte)(value&255));}

        void AddPrecisionArrowHead(Canvas canvas,Point before,Point end,Brush brush)
        {
            Vector direction=before-end;if(direction.Length<1)return;direction.Normalize();Vector side=new Vector(-direction.Y,direction.X);Point a=end+direction*16+side*8,b=end+direction*16-side*8;var left=new System.Windows.Shapes.Line{X1=end.X,Y1=end.Y,X2=a.X,Y2=a.Y,Stroke=brush,StrokeThickness=4,StrokeEndLineCap=PenLineCap.Round};var right=new System.Windows.Shapes.Line{X1=end.X,Y1=end.Y,X2=b.X,Y2=b.Y,Stroke=brush,StrokeThickness=4,StrokeEndLineCap=PenLineCap.Round};canvas.Children.Add(left);canvas.Children.Add(right);
        }

        int PrecisionCoordinate(double value,double origin,double span){return Math.Max(0,Math.Min(1000,(int)Math.Round((value-origin)/Math.Max(1,span)*1000)));}

        string PrecisionPointTag(){Rect r=RenderedImageRect();return r.IsEmpty||Double.IsNaN(precisionPoint.X)?"":String.Format("<point>{0} {1}</point>",PrecisionCoordinate(precisionPoint.X,r.Left,r.Width),PrecisionCoordinate(precisionPoint.Y,r.Top,r.Height));}

        string PrecisionBoxTag(){Rect r=RenderedImageRect();if(r.IsEmpty||precisionSelectionRect.IsEmpty)return "";return String.Format("<bbox>{0} {1} {2} {3}</bbox>",PrecisionCoordinate(precisionSelectionRect.Left,r.Left,r.Width),PrecisionCoordinate(precisionSelectionRect.Top,r.Top,r.Height),PrecisionCoordinate(precisionSelectionRect.Right,r.Left,r.Width),PrecisionCoordinate(precisionSelectionRect.Bottom,r.Top,r.Height));}

        string PreparePrecisionMarkedReference()
        {
            if(workingBitmap==null||precisionPath.Count<2)return PrepareWorkingImageFile();int w=workingBitmap.PixelWidth,h=workingBitmap.PixelHeight;Rect display=RenderedImageRect();var visual=new DrawingVisual();using(DrawingContext context=visual.RenderOpen()){
                context.DrawImage(workingBitmap,new Rect(0,0,w,h));var pen=new Pen(new SolidColorBrush(Color.FromRgb(255,50,45)),Math.Max(5,Math.Min(w,h)/170.0));pen.StartLineCap=PenLineCap.Round;pen.EndLineCap=PenLineCap.Round;pen.LineJoin=PenLineJoin.Round;var geometry=new StreamGeometry();using(StreamGeometryContext g=geometry.Open()){Point first=MapPrecisionPoint(precisionPath[0],display,w,h);g.BeginFigure(first,false,false);for(int i=1;i<precisionPath.Count;i++)g.LineTo(MapPrecisionPoint(precisionPath[i],display,w,h),true,false);}geometry.Freeze();context.DrawGeometry(null,pen,geometry);if(precisionMode=="arrow")DrawPrecisionArrow(context,MapPrecisionPoint(precisionPath[precisionPath.Count-2],display,w,h),MapPrecisionPoint(precisionPath[precisionPath.Count-1],display,w,h),pen);}
            var result=new RenderTargetBitmap(w,h,96,96,PixelFormats.Pbgra32);result.Render(visual);string file=Path.Combine(imageTempDir,"precision-mark-"+Guid.NewGuid().ToString("N")+".png");var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(result));using(var stream=File.Create(file))encoder.Save(stream);return file;
        }

        Point MapPrecisionPoint(Point point,Rect display,int width,int height){return new Point((point.X-display.Left)/Math.Max(1,display.Width)*width,(point.Y-display.Top)/Math.Max(1,display.Height)*height);}

        void DrawPrecisionArrow(DrawingContext context,Point before,Point end,Pen pen){Vector direction=before-end;if(direction.Length<1)return;direction.Normalize();Vector side=new Vector(-direction.Y,direction.X);context.DrawLine(pen,end,end+direction*30+side*15);context.DrawLine(pen,end,end+direction*30-side*15);}

        bool HasTransparentPixels(BitmapSource source)
        {
            if(source==null||source.PixelWidth<1||source.PixelHeight<1)return false;
            try{
                var converted=new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0);int stride=converted.PixelWidth*4;var pixels=new byte[stride*converted.PixelHeight];converted.CopyPixels(pixels,stride,0);
                for(int index=3;index<pixels.Length;index+=4)if(pixels[index]<255)return true;
            }catch{}return false;
        }

        void RunPrecisionImageRequest(bool layers)
        {
            if(workingBitmap==null){imageStatus.Text="请先打开一张图片";return;}if(imageAiBusy||!ValidatePrecisionAi())return;string prompt=(precisionPromptBox.Text??"").Trim();if(layers)prompt=String.IsNullOrEmpty(prompt)?"将图片拆分为可独立使用的前景元素和背景图层，保持所有图层透明区域为真正的 Alpha PNG。":prompt+"\n同时将图片拆分为可独立使用的前景元素和背景图层，透明区域必须保留 Alpha。";if(String.IsNullOrEmpty(prompt)){imageStatus.Text="请填写对标记区域的修改要求";return;}
            if(layers&&!ActiveImageSupportsLayers()){imageStatus.Text="当前来源未声明支持原生图层拆分。请在“管理图像来源”中选择支持该能力的来源；普通图像模型仍可用于局部编辑。";return;}
            if(layers){preLayerSplitBitmap=workingBitmap;preLayerSplitEncodedBytes=workingEncodedBytes??EncodeBitmap(workingBitmap,"PNG",100);}
            string input=null;string tag="";if(precisionMode=="point"&&!Double.IsNaN(precisionPoint.X))tag=PrecisionPointTag();else if(precisionMode=="bbox"&&!precisionSelectionRect.IsEmpty)tag=PrecisionBoxTag();else if((precisionMode=="lasso"||precisionMode=="doodle"||precisionMode=="arrow")&&precisionPath.Count>1){input=PreparePrecisionMarkedReference();prompt="参考图中红色标记就是需要处理的区域。"+prompt;}if(String.IsNullOrEmpty(input))input=layers?PrepareLayerDecompositionInput():PrepareWorkingImageFile();if(!String.IsNullOrEmpty(tag))prompt="请仅处理 image 1 "+tag+" 指向的区域，其余内容不变。\n"+prompt;
            string size=precisionSizeBox==null?"auto":Convert.ToString(precisionSizeBox.SelectedItem);string outputFormat=precisionOutputFormatBox==null?"png":Convert.ToString(precisionOutputFormatBox.SelectedItem).ToLowerInvariant();string optimize=precisionOptimizeBox!=null&&Convert.ToString(precisionOptimizeBox.SelectedItem)=="快速"?"fast":"standard";bool transparent=precisionTransparentBackgroundBox!=null&&precisionTransparentBackgroundBox.IsChecked==true;
            bool ark=String.Equals(ActiveImageProtocol(),"ark",StringComparison.OrdinalIgnoreCase);
            if(transparent&&!ActiveImageSupportsTransparency()){imageStatus.Text="当前来源未声明支持透明 PNG；请在来源设置中开启并确认模型能力，或关闭透明通道。";return;}
            if(transparent&&!layers&&ark&&!HasTransparentPixels(workingBitmap)){imageStatus.Text="当前参考图没有 Alpha 通道。该原生局部编辑接口要求上传带真实透明像素的 PNG；请关闭“透明通道”，或改用带透明区域的 PNG。";return;}
            // 图层拆分本身返回透明 PNG 图层，不需要也不应附带 background: transparent。
            if(layers&&transparent){transparent=false;if(precisionTransparentBackgroundBox!=null)precisionTransparentBackgroundBox.IsChecked=false;}
            imageAiBusy=true;
            if(!layers&&!ark){string output=Path.Combine(imageTempDir,"result-"+Guid.NewGuid().ToString("N")+".png");imageStatus.Text="正在用当前来源提交局部编辑…";var request=new Dictionary<string,object>{{"mode","edit"},{"provider","auto"},{"protocol",ActiveImageProtocol()},{"base_url",ActiveImageBaseUrl()},{"model",ActiveImageModel()},{"image_path",input},{"output_path",output},{"prompt",prompt},{"size",imageAiConfig.ImageSize},{"transparent_background",transparent},{"quality",imageAiConfig.ImageQuality}};TrackImageTask(request,false);RunImageHelper(request,null,LoadActiveImageKey(),delegate(Dictionary<string,object> response){if(response!=null&&response.ContainsKey("ok")&&Convert.ToBoolean(response["ok"]))DeleteTemporaryImageFile(input);HandleGeneratedImageResponse(response,output);});return;}
            string outputDir=Path.Combine(imageTempDir,"precision-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(outputDir);imageStatus.Text=layers?"正在提交原生图层拆分任务（将返回底图与透明 PNG 图层）…":"正在提交当前来源的原生局部编辑任务…";var nativeRequest=new Dictionary<string,object>{{"mode","precision"},{"protocol",ActiveImageProtocol()},{"base_url",ActiveImageBaseUrl()},{"model",ActiveImageModel()},{"image_path",input},{"output_dir",outputDir},{"prompt",prompt},{"size",size},{"output_format",outputFormat},{"transparent_background",transparent},{"optimize_mode",optimize},{"layer_decomposition",layers}};TrackImageTask(nativeRequest,layers);RunImageHelper(nativeRequest,null,LoadActiveImageKey(),delegate(Dictionary<string,object> response){if(response!=null&&response.ContainsKey("ok")&&Convert.ToBoolean(response["ok"]))DeleteTemporaryImageFile(input);HandlePrecisionImageResponse(response,layers);});
        }

        void HandlePrecisionImageResponse(Dictionary<string,object> response,bool layers)
        {
            imageAiBusy=false;if(!ImageResponseOk(response))return;var paths=response.ContainsKey("output_paths")?response["output_paths"] as System.Collections.IList:null;if(paths==null||paths.Count==0){imageStatus.Text="精确编辑未返回可预览图片";FinishImageTask(false,imageStatus.Text);return;}precisionResultPaths.Clear();foreach(object item in paths){string path=Convert.ToString(item);if(File.Exists(path))precisionResultPaths.Add(path);}if(precisionResultPaths.Count==0){imageStatus.Text="精确编辑图片下载失败";FinishImageTask(false,imageStatus.Text);return;}
            precisionLayers.Clear();layerCompositionActive=false;selectedPrecisionLayer=null;
            if(layers){ShowTransparentLayerCanvas();BuildPrecisionLayerCanvas(response);}else ShowPrecisionResultAt(0);
            var labels=new List<string>();for(int i=0;i<precisionResultPaths.Count;i++)labels.Add((layers?(i==0?"拆分参考图（不显示）":"透明图层 "+i):"编辑结果 "+(i+1))+" · "+Path.GetFileName(precisionResultPaths[i]));precisionResultBox.ItemsSource=null;precisionResultBox.ItemsSource=labels;precisionResultBox.Visibility=layers?Visibility.Collapsed:Visibility.Visible;if(!layers)precisionResultBox.SelectedIndex=0;
            imageStatus.Text=layers?"图层拆分完成：画布为透明底，仅显示拆出的图层。":"精确编辑完成；可预览后保存到中转袋。";FinishImageTask(true,layers?"已返回 "+Math.Max(0,precisionResultPaths.Count-1)+" 个透明图层，可继续移动、缩放和导出。":"局部编辑结果已载入画布；可按住“对比原图”检查。");if(precisionStatus!=null)precisionStatus.Text=layers?"拆分前原图未叠在画布上；选中图层后可拖动、缩放及调整层级，需要时点“恢复拆分前原图”。":"结果保存在临时工作区；选中预览后可用底部“保存到中转袋 / 覆盖原图”确认。";
        }

        void ShowTransparentLayerCanvas()
        {
            try
            {
                BitmapSource reference=LoadEditorBitmap(precisionResultPaths[0]);var blank=new WriteableBitmap(reference.PixelWidth,reference.PixelHeight,96,96,PixelFormats.Pbgra32,null);blank.Freeze();workingBitmap=blank;workingEncodedBytes=EncodeBitmap(blank,"PNG",100);ClearCompressionCandidate();ClearImageTextLayer();ClearPrecisionMarks();ExitCropMode();editorImage.Source=workingBitmap;widthBox.Text=workingBitmap.PixelWidth.ToString();heightBox.Text=workingBitmap.PixelHeight.ToString();outputFormatBox.SelectedItem="PNG";ScheduleCompressionEstimate();ResetImageHistory();
            }
            catch(Exception ex){throw new InvalidOperationException("无法创建透明图层画布："+ex.Message);}
        }

        void RequestVisionLayerLayout()
        {
            if(precisionLayers.Count==0||precisionResultPaths.Count<2)return;if(!HasTextOcrConfiguration()){imageStatus.Text="图层已拆分，但未配置支持识图的文本模型；当前只能使用本地初始位置。";return;}imageAiBusy=true;var paths=new List<string>();paths.Add(precisionResultPaths[0]);paths.AddRange(precisionLayers.Select(x=>x.Path));var request=new Dictionary<string,object>{{"mode","layer_layout"},{"base_url",ActiveTextBaseUrl()},{"model",ActiveTextModel()},{"image_paths",paths}};RunImageHelper(request,LoadActiveTextKey(),null,delegate(Dictionary<string,object> response){imageAiBusy=false;if(!ImageResponseOk(response)){imageStatus.Text="视觉文本模型未能完成图层定位，保留可手动调整的图层画布。";return;}ApplyVisionLayerLayout(response);});
        }

        void ApplyVisionLayerLayout(Dictionary<string,object> response)
        {
            var values=response.ContainsKey("layers")?response["layers"] as System.Collections.IEnumerable:null;int placed=0;if(values!=null)foreach(object value in values){var map=value as Dictionary<string,object>;if(map==null)continue;int index;try{index=Convert.ToInt32(map["index"]);}catch{continue;}if(index<1||index>precisionLayers.Count)continue;double x=NumberFromMap(map,"x"),y=NumberFromMap(map,"y"),w=NumberFromMap(map,"width"),h=NumberFromMap(map,"height");if(w<=0||h<=0||w>1.5||h>1.5)continue;var layer=precisionLayers[index-1];layer.X=Math.Max(-workingBitmap.PixelWidth*.5,Math.Min(workingBitmap.PixelWidth,x*workingBitmap.PixelWidth));layer.Y=Math.Max(-workingBitmap.PixelHeight*.5,Math.Min(workingBitmap.PixelHeight,y*workingBitmap.PixelHeight));layer.Width=w*workingBitmap.PixelWidth;layer.Height=h*workingBitmap.PixelHeight;layer.NaturalWidth=layer.Width;layer.NaturalHeight=layer.Height;if(map.ContainsKey("z"))try{layer.Z=Convert.ToInt32(map["z"]);}catch{}placed++;}RefreshPrecisionLayerList();LayoutPrecisionLayers();imageStatus.Text=placed>0?"视觉文本模型已还原 "+placed+" 个图层的位置、大小和层级；仍可在画布上微调。":"视觉文本模型未返回可用坐标，保留可手动调整的图层画布。";if(precisionStatus!=null)precisionStatus.Text="拖动图层移动位置；使用“图层缩放”调整大小；上移/下移改变前后关系。";
        }

        void BuildPrecisionLayerCanvas(Dictionary<string,object> response)
        {
            if(precisionResultPaths.Count<2||workingBitmap==null)return;var raw=response.ContainsKey("layers")?response["layers"] as System.Collections.IList:null;
            for(int i=1;i<precisionResultPaths.Count;i++){try{BitmapSource bitmap=LoadEditorBitmap(precisionResultPaths[i]);var layer=new PrecisionLayer{Name="◉ 图层 "+i,Path=precisionResultPaths[i],Bitmap=bitmap,Z=i-1,Width=bitmap.PixelWidth,Height=bitmap.PixelHeight,NaturalWidth=bitmap.PixelWidth,NaturalHeight=bitmap.PixelHeight};Dictionary<string,object> meta=raw!=null&&i<raw.Count?raw[i] as Dictionary<string,object>:null;bool hasPosition=false;if(meta!=null){if(meta.ContainsKey("name")&&!String.IsNullOrWhiteSpace(Convert.ToString(meta["name"])))layer.Name="◉ "+Convert.ToString(meta["name"]);if(meta.ContainsKey("z_index"))try{layer.Z=Convert.ToInt32(meta["z_index"]);}catch{}hasPosition=ApplyLayerBoundingBox(layer,meta,workingBitmap.PixelWidth,workingBitmap.PixelHeight);}if(!hasPosition){layer.X=(workingBitmap.PixelWidth-layer.Width)/2.0;layer.Y=(workingBitmap.PixelHeight-layer.Height)/2.0;}precisionLayers.Add(layer);}catch(Exception ex){imageStatus.Text="第 "+i+" 个图层无法载入："+ex.Message;}}
            if(precisionLayers.Count==0)return;layerCompositionActive=true;selectedPrecisionLayer=precisionLayers.OrderByDescending(x=>x.Z).First();RefreshPrecisionLayerList();LayoutPrecisionLayers();
        }

        bool ApplyLayerBoundingBox(PrecisionLayer layer,Dictionary<string,object> meta,int baseWidth,int baseHeight)
        {
            object raw;if(!meta.TryGetValue("bounding_box",out raw)||raw==null)return false;double x=0,y=0,w=0,h=0;var list=raw as System.Collections.IList;if(list!=null&&list.Count>=4){try{x=Convert.ToDouble(list[0]);y=Convert.ToDouble(list[1]);w=Convert.ToDouble(list[2]);h=Convert.ToDouble(list[3]);}catch{return false;}}else{var box=raw as Dictionary<string,object>;if(box==null)return false;try{x=Convert.ToDouble(box.ContainsKey("x")?box["x"]:box["left"]);y=Convert.ToDouble(box.ContainsKey("y")?box["y"]:box["top"]);w=Convert.ToDouble(box.ContainsKey("width")?box["width"]:box["w"]);h=Convert.ToDouble(box.ContainsKey("height")?box["height"]:box["h"]);}catch{return false;}}
            double max=Math.Max(Math.Max(Math.Abs(x),Math.Abs(y)),Math.Max(Math.Abs(w),Math.Abs(h)));if(max<=1.01){x*=baseWidth;y*=baseHeight;w*=baseWidth;h*=baseHeight;}else if(max<=1000&&baseWidth>1000){x=x/1000*baseWidth;y=y/1000*baseHeight;w=w/1000*baseWidth;h=h/1000*baseHeight;}if(w>1&&h>1){layer.X=x;layer.Y=y;layer.Width=w;layer.Height=h;layer.NaturalWidth=w;layer.NaturalHeight=h;return true;}return false;
        }

        // 接口未提供 bbox 时，使用透明 PNG 的可见像素在原图里寻找最接近的位置。
        // 图层本身若保留了原始画布尺寸则无需匹配，直接从 (0,0) 绘制即可。

        void AutoPlacePrecisionLayer(PrecisionLayer layer,BitmapSource baseImage)
        {
            if(layer.Bitmap==null||baseImage==null)return;if(layer.Bitmap.PixelWidth==baseImage.PixelWidth&&layer.Bitmap.PixelHeight==baseImage.PixelHeight){layer.X=0;layer.Y=0;return;}if(layer.Width>baseImage.PixelWidth||layer.Height>baseImage.PixelHeight){double fit=Math.Min((double)baseImage.PixelWidth/layer.Width,(double)baseImage.PixelHeight/layer.Height);layer.Width*=fit;layer.Height*=fit;layer.NaturalWidth=layer.Width;layer.NaturalHeight=layer.Height;}
            try{var base32=new FormatConvertedBitmap(baseImage,PixelFormats.Bgra32,null,0);var layer32=new FormatConvertedBitmap(layer.Bitmap,PixelFormats.Bgra32,null,0);int bw=base32.PixelWidth,bh=base32.PixelHeight,lw=layer32.PixelWidth,lh=layer32.PixelHeight;byte[] bp=new byte[bw*bh*4],lp=new byte[lw*lh*4];base32.CopyPixels(bp,bw*4,0);layer32.CopyPixels(lp,lw*4,0);var samples=new List<Point>();int stride=Math.Max(3,Math.Min(lw,lh)/18);for(int sy=stride/2;sy<lh&&samples.Count<72;sy+=stride)for(int sx=stride/2;sx<lw&&samples.Count<72;sx+=stride){if(lp[(sy*lw+sx)*4+3]>110)samples.Add(new Point(sx,sy));}if(samples.Count<6){layer.X=(bw-layer.Width)/2;layer.Y=(bh-layer.Height)/2;return;}int step=Math.Max(8,Math.Min(Math.Max(1,bw-lw),Math.Max(1,bh-lh))/42);double best=Double.MaxValue;int bestX=0,bestY=0;for(int y=0;y<=Math.Max(0,bh-lh);y+=step)for(int x=0;x<=Math.Max(0,bw-lw);x+=step){double score=0;foreach(Point p in samples){int si=((int)p.Y*lw+(int)p.X)*4,bi=((y+(int)p.Y)*bw+x+(int)p.X)*4;int db=bp[bi]-lp[si],dg=bp[bi+1]-lp[si+1],dr=bp[bi+2]-lp[si+2];score+=db*db+dg*dg+dr*dr;if(score>=best)break;}if(score<best){best=score;bestX=x;bestY=y;}}layer.X=bestX;layer.Y=bestY;}
            catch{layer.X=(baseImage.PixelWidth-layer.Width)/2;layer.Y=(baseImage.PixelHeight-layer.Height)/2;}
        }

        void ShowPrecisionResult()
        {
            if(precisionResultBox==null||precisionResultBox.SelectedIndex<0||precisionResultBox.SelectedIndex>=precisionResultPaths.Count)return;ShowPrecisionResultAt(precisionResultBox.SelectedIndex);
        }

        void ShowPrecisionResultAt(int index){if(index<0||index>=precisionResultPaths.Count)return;try{string path=precisionResultPaths[index];workingEncodedBytes=File.ReadAllBytes(path);workingBitmap=LoadEditorBytes(workingEncodedBytes);ClearCompressionCandidate();ClearImageTextLayer();ClearPrecisionMarks();ExitCropMode();editorImage.Source=workingBitmap;widthBox.Text=workingBitmap.PixelWidth.ToString();heightBox.Text=workingBitmap.PixelHeight.ToString();outputFormatBox.SelectedItem="PNG";ScheduleCompressionEstimate();PushImageState();}catch(Exception ex){imageStatus.Text="结果预览失败："+ex.Message;}}

        string PrepareWorkingImageFile(){string path=Path.Combine(imageTempDir,"input-"+Guid.NewGuid().ToString("N")+".png");File.WriteAllBytes(path,EncodeBitmap(workingBitmap,"PNG",100));return path;}

        string PrepareLayerDecompositionInput()
        {
            // 未修改的图片必须原样交给模型：不缩放、不压缩、不转换格式、不铺白底。
            if(!layerCompositionActive&&workingEncodedBytes==null&&editingImageItem!=null&&!String.IsNullOrWhiteSpace(editingImageItem.Value)&&File.Exists(editingImageItem.Value))return editingImageItem.Value;
            // AI 返回的当前图片已有原始字节时，同样逐字节写出，保持来源文件编码与尺寸。
            if(!layerCompositionActive&&workingEncodedBytes!=null){string ext=workingEncodedBytes.Length>8&&workingEncodedBytes[0]==137&&workingEncodedBytes[1]==80?".png":".jpg";string direct=Path.Combine(imageTempDir,"layer-input-"+Guid.NewGuid().ToString("N")+ext);File.WriteAllBytes(direct,workingEncodedBytes);return direct;}
            // 只有用户已在画布上改变图层位置/尺寸、当前不存在源文件时，才将“当前画布状态”导出；不做任何缩放。
            return PrepareWorkingImageFile();
        }

        void DeleteTemporaryImageFile(string path)
        {
            try{if(String.IsNullOrWhiteSpace(path))return;string tempRoot=Path.GetFullPath(imageTempDir).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;string full=Path.GetFullPath(path);if(full.StartsWith(tempRoot,StringComparison.OrdinalIgnoreCase)&&File.Exists(full))File.Delete(full);}catch{}
        }
}
}
