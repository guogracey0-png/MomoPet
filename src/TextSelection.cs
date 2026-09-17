using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace MomoPetApp
{
    // Keep existing TextBlocks and their bindings intact; selection is drawn in an adorner.
    // This also covers messages and result labels inserted after a window has loaded.
    public static class TextSelection
    {
        static bool installed;
        static TextBlock current;
        static TextPointer anchor, extent;
        static bool dragging;
        static bool contextOpening;
        static SelectionAdorner highlight;
        static AdornerLayer layer;
        static readonly DependencyProperty AttachedProperty=DependencyProperty.RegisterAttached("Attached",typeof(bool),typeof(TextSelection),new PropertyMetadata(false));

        public static void Install()
        {
            if(installed)return;installed=true;
            EventManager.RegisterClassHandler(typeof(TextBlock),FrameworkElement.LoadedEvent,new RoutedEventHandler(OnLoaded));
        }

        static DependencyObject Parent(DependencyObject value)
        {
            var content=value as FrameworkContentElement;
            if(content!=null)return content.Parent;
            try{return VisualTreeHelper.GetParent(value)??LogicalTreeHelper.GetParent(value);}catch{return LogicalTreeHelper.GetParent(value);}
        }

        static bool IsControlLabel(TextBlock text)
        {
            for(DependencyObject p=Parent(text);p!=null;p=Parent(p)){
                if(p is ButtonBase||p is MenuItem||p is TextBoxBase||p is PasswordBox||p is ComboBox||p is ListBoxItem||p is ToolTip)return true;
                var link=p as Border;if(link!=null&&link.Cursor==Cursors.Hand)return true;
            }
            return false;
        }

        static void OnLoaded(object sender,RoutedEventArgs e)
        {
            var text=sender as TextBlock;
            if(text==null||(bool)text.GetValue(AttachedProperty))return;
            var window=Window.GetWindow(text);if(window==null||window.Title=="博道咪")return;
            text.SetValue(AttachedProperty,true);
            if(!IsControlLabel(text)){
                text.Focusable=true;text.Cursor=Cursors.IBeam;
                text.MouseLeftButtonDown+=Begin;
                text.MouseMove+=Move;
                text.MouseLeftButtonUp+=End;
                text.LostMouseCapture+=delegate{dragging=false;};
                text.PreviewKeyDown+=KeyDown;
                text.LostKeyboardFocus+=delegate{if(current==text&&!dragging&&!contextOpening)Clear();};
                text.Unloaded+=delegate{if(current==text)Clear();};
                CommandManager.AddCanExecuteHandler(text,delegate(object s,CanExecuteRoutedEventArgs args){
                    if(args.Command==ApplicationCommands.Copy){args.CanExecute=current==text&&!String.IsNullOrEmpty(SelectedText);args.Handled=true;}
                    if(args.Command==ApplicationCommands.SelectAll){args.CanExecute=true;args.Handled=true;}
                });
                CommandManager.AddExecutedHandler(text,delegate(object s,ExecutedRoutedEventArgs args){
                    if(args.Command==ApplicationCommands.Copy){Copy(SelectedText);args.Handled=true;}
                    if(args.Command==ApplicationCommands.SelectAll){SelectAll(text);args.Handled=true;}
                });
            }
            // Preserve existing app context menus and native editor menus.
            if(text.ContextMenu!=null)return;
            for(DependencyObject p=Parent(text);p!=null;p=Parent(p)){
                var element=p as FrameworkElement;if(element!=null&&element.ContextMenu!=null)return;
                if(p is TextBoxBase||p is PasswordBox||p is ToolTip||p is MenuItem)return;
            }
            var menu=new ContextMenu();
            var copy=new MenuItem{Header="复制选中文字",InputGestureText="Ctrl+C"};
            copy.Click+=delegate{Copy(current==text?SelectedText:"");};menu.Items.Add(copy);
            var all=new MenuItem{Header="复制全部文字"};all.Click+=delegate{Copy(new TextRange(text.ContentStart,text.ContentEnd).Text);};menu.Items.Add(all);
            var select=new MenuItem{Header="全选",InputGestureText="Ctrl+A"};select.Click+=delegate{SelectAll(text);};menu.Items.Add(select);
            text.ContextMenuOpening+=delegate{if(current==text)contextOpening=true;};
            menu.Closed+=delegate{contextOpening=false;};
            menu.Opened+=delegate{copy.IsEnabled=current==text&&!String.IsNullOrEmpty(SelectedText);select.IsEnabled=!IsControlLabel(text);};text.ContextMenu=menu;
        }

        static TextPointer Position(TextBlock text,Point point)
        {
            point.X=Math.Max(0,Math.Min(point.X,Math.Max(0,text.ActualWidth-1)));
            if(point.Y<0)return text.ContentStart;
            if(point.Y>text.ActualHeight)return text.ContentEnd;
            return text.GetPositionFromPoint(point,true)??text.ContentEnd;
        }

        static void Begin(object sender,MouseButtonEventArgs e)
        {
            var text=(TextBlock)sender;
            // Hyperlinks retain their normal navigation behavior.
            for(DependencyObject p=e.OriginalSource as DependencyObject;p!=null&&p!=text;p=Parent(p))if(p is Hyperlink)return;
            Clear();current=text;text.Focus();anchor=extent=Position(text,e.GetPosition(text));
            layer=AdornerLayer.GetAdornerLayer(text);if(layer!=null){highlight=new SelectionAdorner(text);layer.Add(highlight);}
            if(e.ClickCount>=2){anchor=text.ContentStart;extent=text.ContentEnd;}
            dragging=true;text.CaptureMouse();Redraw();e.Handled=true;
        }

        static void Move(object sender,MouseEventArgs e)
        {
            if(!dragging||current!=sender||e.LeftButton!=MouseButtonState.Pressed)return;
            for(DependencyObject p=Parent(current);p!=null;p=Parent(p)){
                var scroll=p as ScrollViewer;if(scroll==null)continue;
                Point position=e.GetPosition(scroll);
                if(position.Y<12)scroll.LineUp();else if(position.Y>scroll.ActualHeight-12)scroll.LineDown();
                break;
            }
            extent=Position(current,e.GetPosition(current));Redraw();e.Handled=true;
        }

        static void End(object sender,MouseButtonEventArgs e)
        {
            if(current!=sender||!dragging)return;
            dragging=false;if(current.IsMouseCaptured)current.ReleaseMouseCapture();e.Handled=true;
        }

        static void KeyDown(object sender,KeyEventArgs e)
        {
            if((Keyboard.Modifiers&ModifierKeys.Control)!=0&&e.Key==Key.C){Copy(SelectedText);e.Handled=true;}
            else if((Keyboard.Modifiers&ModifierKeys.Control)!=0&&e.Key==Key.A){SelectAll((TextBlock)sender);e.Handled=true;}
            else if(e.Key==Key.Escape){Clear();e.Handled=true;}
        }

        public static string SelectedText
        {
            get{if(current==null||anchor==null||extent==null)return "";return new TextRange(anchor,extent).Text;}
        }

        public static void SelectAll(TextBlock text)
        {
            if(current!=text){Clear();current=text;layer=AdornerLayer.GetAdornerLayer(text);if(layer!=null){highlight=new SelectionAdorner(text);layer.Add(highlight);}}
            text.Focus();anchor=text.ContentStart;extent=text.ContentEnd;Redraw();
        }

        public static bool HasSelectionWithin(Window window)
        {
            if(window==null)return false;
            var editor=Keyboard.FocusedElement as TextBox;
            if(editor!=null&&editor.IsReadOnly&&Window.GetWindow(editor)==window&&editor.SelectionLength>0)return true;
            return current!=null&&Window.GetWindow(current)==window&&(dragging||!String.IsNullOrEmpty(SelectedText));
        }

        public static void Clear()
        {
            dragging=false;contextOpening=false;if(current!=null&&current.IsMouseCaptured)current.ReleaseMouseCapture();
            if(layer!=null&&highlight!=null)layer.Remove(highlight);
            current=null;anchor=null;extent=null;layer=null;highlight=null;
        }

        static void Redraw(){if(highlight!=null)highlight.InvalidateVisual();}

        public static void Copy(string text)
        {
            if(String.IsNullOrEmpty(text))return;
            try{Clipboard.SetDataObject(text,true);}catch(ExternalException){System.Media.SystemSounds.Beep.Play();}
        }

        sealed class SelectionAdorner:Adorner
        {
            readonly TextBlock text;
            readonly Brush brush=new SolidColorBrush(Color.FromArgb(80,93,147,218));
            public SelectionAdorner(TextBlock owner):base(owner){text=owner;IsHitTestVisible=false;ClipToBounds=true;}
            protected override void OnRender(DrawingContext drawing)
            {
                if(current!=text||anchor==null||extent==null)return;
                TextPointer first=anchor.CompareTo(extent)<=0?anchor:extent,last=anchor.CompareTo(extent)<=0?extent:anchor;
                if(first.CompareTo(last)==0)return;
                // Draw one rectangle per visual line, keeping the original text formatting intact.
                for(TextPointer p=first;p!=null&&p.CompareTo(last)<0;){
                    TextPointer next=p.GetLineStartPosition(1);TextPointer end=next==null||next.CompareTo(last)>0?last:next;
                    Rect left=p.GetCharacterRect(LogicalDirection.Forward),right=end.GetCharacterRect(LogicalDirection.Backward);
                    if(!left.IsEmpty&&!right.IsEmpty){
                        double x=Math.Min(left.X,right.Right),y=Math.Min(left.Y,right.Y),width=Math.Abs(right.Right-left.X);
                        if(Math.Abs(left.Y-right.Y)>left.Height*.5){width=text.ActualWidth-x;}
                        drawing.DrawRectangle(brush,null,new Rect(x,y,Math.Max(1,width),Math.Max(left.Height,right.Height)));
                    }
                    if(next==null||next.CompareTo(p)<=0)break;p=next;
                }
            }
        }
    }
}
