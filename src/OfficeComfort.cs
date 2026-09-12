using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media.Imaging;

namespace MomoPetApp
{
    public sealed class OfficeWindowState
    {
        public double Left,Top,Width,Height;
        public bool Placed;
        public Dictionary<string,string> Text=new Dictionary<string,string>();
        public Dictionary<string,string> Documents=new Dictionary<string,string>();
        public Dictionary<string,double> Scroll=new Dictionary<string,double>();
        public List<Dictionary<string,object>> Messages;
        public string Canvas,Original,BeforeSplit;
        public bool Layered;
        public List<OfficeLayerState> Layers=new List<OfficeLayerState>();
    }
    public sealed class OfficeLayerState { public string Name,Image;public double X,Y,Width,Height,NaturalWidth,NaturalHeight;public int Z;public bool Visible; }

    public partial class PetController
    {
        readonly HashSet<Window> comfortWindows=new HashSet<Window>();
        readonly HashSet<Window> comfortDirty=new HashSet<Window>();
        readonly HashSet<Window> comfortLoaded=new HashSet<Window>();
        DispatcherTimer comfortSaveTimer;
        StashItem lastOfficeCapturedItem;
        bool presentationQuiet;
        [System.Runtime.InteropServices.DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
        void CheckPresentationQuiet()
        {
            IntPtr foreground=GetForegroundWindow();uint pid;GetWindowThreadProcessId(foreground,out pid);
            bool fullscreen=false;NativeRect rect;var info=new PetMonitorInfo{Size=System.Runtime.InteropServices.Marshal.SizeOf(typeof(PetMonitorInfo))};
            var cls=new StringBuilder(128);GetClassName(foreground,cls,cls.Capacity);
            if(pid!=(uint)System.Diagnostics.Process.GetCurrentProcess().Id&&cls.ToString()!="Progman"&&cls.ToString()!="WorkerW"&&GetWindowRect(foreground,out rect)&&GetMonitorInfo(MonitorFromWindow(foreground,2),ref info))
                fullscreen=rect.Left<=info.Monitor.Left&&rect.Top<=info.Monitor.Top&&rect.Right>=info.Monitor.Right&&rect.Bottom>=info.Monitor.Bottom;
            if(fullscreen==presentationQuiet)return;presentationQuiet=fullscreen;
            if(fullscreen){CancelShelfPeekTimers();HideShelfPeek();HideLauncherAndRestoreShelves();if(speechBubble!=null)speechBubble.Visibility=Visibility.Collapsed;if(pet!=null)pet.Hide();}
            else if(!globalPetHidden&&pet!=null)pet.Show();
        }
        readonly System.Runtime.CompilerServices.ConditionalWeakTable<BitmapSource,string> comfortBitmapFiles=new System.Runtime.CompilerServices.ConditionalWeakTable<BitmapSource,string>();
        // Explicit allowlist: settings fields and credentials are never captured.
        static readonly string[] draftFields={"titleBox","aiQuestionBox","templateNameBox","pocketChatInput","redrawPromptBox","complianceInput","complianceReviewerBox","complianceReferenceBox"};
        static readonly string[] documentFields={"aiResultBox","pocketChatHistory"};
        string ComfortStatePath(Window window)
        {
            using(var hash=SHA256.Create())return Path.Combine(dataDir,"workspace-"+BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(window.Title))).Replace("-","").Substring(0,20)+".json");
        }
        object ComfortField(string name){var field=GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic);return field==null?null:field.GetValue(this);}
        bool BelongsToWindow(DependencyObject control,Window window){return control!=null&&Window.GetWindow(control)==window;}

        void TrackOfficeWindow(Window window)
        {
            if(!comfortWindows.Add(window))return;
            bool loaded=false;
            Action dirty=delegate{if(!loaded)return;comfortDirty.Add(window);if(comfortSaveTimer==null){comfortSaveTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};comfortSaveTimer.Tick+=delegate{comfortSaveTimer.Stop();FlushOfficeState();};}comfortSaveTimer.Stop();comfortSaveTimer.Start();};
            window.Loaded+=delegate{if(loaded)return;RestoreOfficeState(window);loaded=true;comfortLoaded.Add(window);};
            window.AddHandler(TextBoxBase.TextChangedEvent,new TextChangedEventHandler(delegate{dirty();}));
            window.LocationChanged+=delegate{if(manuallyPlacedWindows.Contains(window))dirty();};
            window.SizeChanged+=delegate{dirty();};
            window.PreviewMouseLeftButtonUp+=delegate{dirty();};
            window.IsVisibleChanged+=delegate{dirty();};
            window.Closing+=delegate{if(loaded)SaveOfficeState(window);};
            window.Closed+=delegate{comfortWindows.Remove(window);comfortLoaded.Remove(window);comfortDirty.Remove(window);};
        }

        void FlushOfficeState()
        {
            foreach(Window window in comfortDirty.ToList())SaveOfficeState(window);
            comfortDirty.Clear();
        }

        void SaveOfficeState(Window window)
        {
            try{
                Rect bounds=window.WindowState==WindowState.Normal?new Rect(window.Left,window.Top,window.Width,window.Height):window.RestoreBounds;
                if(bounds.IsEmpty||Double.IsNaN(bounds.Width)||Double.IsNaN(bounds.Left))return;
                var state=new OfficeWindowState{Left=bounds.Left,Top=bounds.Top,Width=bounds.Width,Height=bounds.Height,Placed=manuallyPlacedWindows.Contains(window)};
                foreach(string name in draftFields){var box=ComfortField(name) as TextBox;if(BelongsToWindow(box,window)){state.Text[name]=box.Text;state.Scroll[name]=box.VerticalOffset;}}
                foreach(string name in documentFields){var box=ComfortField(name) as RichTextBox;if(!BelongsToWindow(box,window))continue;using(var stream=new MemoryStream()){new TextRange(box.Document.ContentStart,box.Document.ContentEnd).Save(stream,DataFormats.XamlPackage);state.Documents[name]=Convert.ToBase64String(stream.ToArray());}state.Scroll[name]=box.VerticalOffset;}
                if(window==imageEditorPanel){
                    state.Messages=pocketChatMessages.ToList();
                    state.Canvas=StoreComfortBitmap(workingBitmap);state.Original=StoreComfortBitmap(originalBitmap);state.BeforeSplit=StoreComfortBitmap(preLayerSplitBitmap);state.Layered=layerCompositionActive;
                    foreach(var layer in precisionLayers)state.Layers.Add(new OfficeLayerState{Name=layer.Name,Image=StoreComfortBitmap(layer.Bitmap),X=layer.X,Y=layer.Y,Width=layer.Width,Height=layer.Height,NaturalWidth=layer.NaturalWidth,NaturalHeight=layer.NaturalHeight,Z=layer.Z,Visible=layer.Visible});
                }
                string content=new JavaScriptSerializer{MaxJsonLength=Int32.MaxValue}.Serialize(state),path=ComfortStatePath(window),temporary=path+".tmp";
                File.WriteAllText(temporary,content,new UTF8Encoding(false));
                if(File.Exists(path))File.Replace(temporary,path,path+".bak");else File.Move(temporary,path);
            }catch(Exception error){System.Diagnostics.Debug.WriteLine("Workspace save: "+error.Message);}
        }

        void RestoreOfficeState(Window window)
        {
            try{
                string path=ComfortStatePath(window);if(!File.Exists(path))return;
                var state=new JavaScriptSerializer{MaxJsonLength=Int32.MaxValue}.Deserialize<OfficeWindowState>(File.ReadAllText(path));if(state==null)return;
                var area=SystemParameters.WorkArea;
                if(state.Width>0&&state.Height>0){window.Width=Math.Max(window.MinWidth,Math.Min(state.Width,area.Width));window.Height=Math.Max(window.MinHeight,Math.Min(state.Height,area.Height));}
                if(state.Placed){manuallyPlacedWindows.Add(window);window.Left=Math.Max(area.Left,Math.Min(state.Left,area.Right-window.Width));window.Top=Math.Max(area.Top,Math.Min(state.Top,area.Bottom-window.Height));}
                foreach(var entry in state.Text){var box=ComfortField(entry.Key) as TextBox;if(BelongsToWindow(box,window)&&String.IsNullOrEmpty(box.Text))box.Text=entry.Value??"";}
                foreach(var entry in state.Documents){var box=ComfortField(entry.Key) as RichTextBox;if(!BelongsToWindow(box,window))continue;using(var stream=new MemoryStream(Convert.FromBase64String(entry.Value)))new TextRange(box.Document.ContentStart,box.Document.ContentEnd).Load(stream,DataFormats.XamlPackage);}
                if(window==imageEditorPanel&&state.Messages!=null&&pocketChatMessages.Count==0)pocketChatMessages.AddRange(state.Messages);
                if(window==imageEditorPanel&&workingBitmap==null&&!String.IsNullOrEmpty(state.Canvas)&&File.Exists(state.Canvas)){
                    workingBitmap=LoadEditorBitmap(state.Canvas);originalBitmap=!String.IsNullOrEmpty(state.Original)&&File.Exists(state.Original)?LoadEditorBitmap(state.Original):workingBitmap;workingEncodedBytes=File.ReadAllBytes(state.Canvas);
                    preLayerSplitBitmap=!String.IsNullOrEmpty(state.BeforeSplit)&&File.Exists(state.BeforeSplit)?LoadEditorBitmap(state.BeforeSplit):null;
                    precisionLayers.Clear();foreach(var saved in state.Layers){if(!File.Exists(saved.Image))continue;precisionLayers.Add(new PrecisionLayer{Name=saved.Name,Path=saved.Image,Bitmap=LoadEditorBitmap(saved.Image),X=saved.X,Y=saved.Y,Width=saved.Width,Height=saved.Height,NaturalWidth=saved.NaturalWidth,NaturalHeight=saved.NaturalHeight,Z=saved.Z,Visible=saved.Visible});}
                    layerCompositionActive=state.Layered&&precisionLayers.Count>0;editorImage.Source=workingBitmap;widthBox.Text=workingBitmap.PixelWidth.ToString();heightBox.Text=workingBitmap.PixelHeight.ToString();ResetImageHistory();RefreshPrecisionLayerList();LayoutPrecisionLayers();
                    imageStatus.Text="已恢复上次画布与图层；可以继续编辑";
                }
                window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(delegate{foreach(var entry in state.Scroll){var text=ComfortField(entry.Key) as TextBox;if(BelongsToWindow(text,window))text.ScrollToVerticalOffset(entry.Value);var rich=ComfortField(entry.Key) as RichTextBox;if(BelongsToWindow(rich,window))rich.ScrollToVerticalOffset(entry.Value);}}));
            }catch(Exception error){System.Diagnostics.Debug.WriteLine("Workspace restore: "+error.Message);}
        }

        string StoreComfortBitmap(BitmapSource bitmap)
        {
            if(bitmap==null)return null;string known;if(comfortBitmapFiles.TryGetValue(bitmap,out known)&&File.Exists(known))return known;
            string folder=Path.Combine(dataDir,"WorkspaceImages");Directory.CreateDirectory(folder);string path=Path.Combine(folder,Guid.NewGuid().ToString("N")+".png");
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(path))encoder.Save(output);comfortBitmapFiles.Remove(bitmap);comfortBitmapFiles.Add(bitmap,path);return path;
        }

        void CaptureClipboardForOffice()
        {
            try{
                var data=Clipboard.GetDataObject();if(data==null||!CanAcceptStash(data)){React("先复制文字、图片或文件，再按 Ctrl+Alt+S",false);return;}
                lastOfficeCapturedItem=null;AcceptStashDrop(data);
                if(lastOfficeCapturedItem==null)return;
                var item=lastOfficeCapturedItem;
                if(shelfPeekPanel==null)BuildShelfPeekPanel();RefreshShelfEntries();
                AddShelfPeekBubble("审核这份材料","带入合规审核",delegate{OpenComplianceReview(item);},false);
                if(item.Kind=="text")AddShelfPeekBubble("问 AI","带入文字，不自动发送",delegate{OpenAiPocketChat(item.Value);},false);
                if(item.Kind=="image")AddShelfPeekBubble("编辑这张图片","打开图片编辑",delegate{OpenImageEditor(item);},false);
                shelfPeekPanel.Height=Math.Min(SystemParameters.WorkArea.Height-16,13+shelfEntries.Children.Count*42);
                PositionShelfPeekPanel();shelfPeekPanel.Show();
            }catch(Exception error){React("暂存失败："+error.Message,false);}
        }
    }
}
