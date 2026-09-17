using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace MomoPetApp
{
    /// <summary>
    /// 博道咪统一 UI 主题与动效库。
    /// 只负责"长得好看 + 有反馈"，不改变任何业务功能。
    /// 所有控件工厂保持与旧版 MakeButton 相同的用法：传入旧配色的画刷会自动映射到新配色。
    /// </summary>
    public static class Ui
    {
        // ---------- 配色（金融生产力工具：柔白底色 + 雾蓝主色） ----------
        // 采用明确的“页面 / 卡片 / 内嵌区”三级明度，避免上一版米白色糊成一片。
        public static readonly Color InkColor = Color.FromRgb(16, 24, 40);          // 主文字 #101828
        public static readonly Color SubInkColor = Color.FromRgb(102, 112, 133);    // 次要文字 #667085
        public static readonly Color AccentColor = Color.FromRgb(76, 109, 166);     // 柔和雾蓝
        public static readonly Color AccentDeepColor = Color.FromRgb(55, 80, 123);  // 深雾蓝
        public static readonly Color PaperColor = Color.FromRgb(248, 247, 245);     // 柔白页面
        public static readonly Color CardColor = Color.FromRgb(255, 255, 255);      // 卡片白
        public static readonly Color InnerColor = Color.FromRgb(248, 250, 252);     // 内嵌区
        public static readonly Color LineColor = Color.FromRgb(234, 236, 240);      // 发丝描边

        public static readonly SolidColorBrush Ink = Freeze(16, 24, 40);
        public static readonly SolidColorBrush SubInk = Freeze(102, 112, 133);
        public static readonly SolidColorBrush Accent = Freeze(76, 109, 166);
        public static readonly SolidColorBrush AccentDeep = Freeze(55, 80, 123);
        public static readonly SolidColorBrush AccentSoft = Freeze(237, 242, 250);
        public static readonly SolidColorBrush Paper = Freeze(248, 247, 245);
        public static readonly SolidColorBrush PeachSoft = Freeze(251, 239, 230);
        public static readonly SolidColorBrush Card = Freeze(255, 255, 255);
        public static readonly SolidColorBrush Inner = Freeze(248, 250, 252);
        public static readonly SolidColorBrush Line = Freeze(234, 236, 240);
        public static readonly SolidColorBrush Green = Freeze(47, 163, 107);
        public static readonly SolidColorBrush GreenSoft = Freeze(233, 246, 239);
        public static readonly SolidColorBrush RedSoft = Freeze(253, 238, 238);
        public static readonly SolidColorBrush Neutral = Freeze(242, 244, 247);
        public static readonly SolidColorBrush Badge = Freeze(255, 59, 48);
        // 行情涨跌（A 股惯例：涨红跌绿）
        public static readonly SolidColorBrush Up = Freeze(224, 72, 77);
        public static readonly SolidColorBrush UpSoft = Freeze(253, 238, 238);
        public static readonly SolidColorBrush Down = Freeze(47, 163, 107);
        public static readonly SolidColorBrush DownSoft = Freeze(233, 246, 239);
        public static readonly SolidColorBrush InputLine = Freeze(208, 213, 221);

        static readonly FontFamily UiFont = new FontFamily("Microsoft YaHei UI");

        static ControlTemplate buttonTemplate;
        static Style textBoxStyle, passwordBoxStyle, richTextBoxStyle, listBoxStyle, listBoxItemStyle, comboBoxItemStyle, comboBoxStyle, checkBoxStyle, datePickerStyle, toolTipStyle, scrollBarStyle;

        static SolidColorBrush Freeze(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        static SolidColorBrush Freeze(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        // ---------- 旧画刷 → 新配色 自动映射（调用点零改动） ----------
        public static Brush NormalizeBrush(Brush brush)
        {
            var solid = brush as SolidColorBrush;
            if (solid == null) return brush;
            Color c = solid.Color;
            if (c.A == 0) return Brushes.Transparent;
            if (Match(c, 255, 126, 115) || Match(c, 255, 116, 107) || Match(c, 255, 111, 77)) return Accent;  // 旧珊瑚 → 雾蓝
            if (Match(c, 84, 163, 112) || Match(c, 63, 169, 123)) return Green;                                // 旧绿 → 新绿
            if (Match(c, 231, 243, 236) || Match(c, 229, 244, 236)) return GreenSoft;
            if (Match(c, 248, 232, 230) || Match(c, 252, 235, 231)) return RedSoft;
            if (Match(c, 255, 232, 226) || Match(c, 245, 225, 219) || Match(c, 255, 237, 229)) return AccentSoft;
            if (Match(c, 235, 229, 222) || Match(c, 238, 234, 231) || Match(c, 244, 238, 233) || Match(c, 245, 239, 233) || Match(c, 244, 238, 230)) return Neutral;
            if (Match(c, 255, 253, 248) || Match(c, 255, 251, 247)) return Paper;
            if (Match(c, 248, 244, 241) || Match(c, 246, 242, 237) || Match(c, 247, 243, 238) || Match(c, 250, 245, 239)) return Inner;
            if (Match(c, 61, 48, 40) || Match(c, 48, 42, 38) || Match(c, 67, 53, 43)) return Ink;
            if (Match(c, 138, 123, 114) || Match(c, 126, 112, 103) || Match(c, 145, 128, 118) || Match(c, 132, 117, 108) || Match(c, 110, 99, 92) || Match(c, 166, 144, 126)) return SubInk;
            if (Match(c, 222, 212, 204) || Match(c, 215, 204, 196) || Match(c, 205, 196, 189) || Match(c, 238, 227, 214)) return Line;
            return brush;
        }

        static bool Match(Color c, byte r, byte g, byte b) { return c.R == r && c.G == g && c.B == b; }

        // ---------- 卡片（统一窗口外壳：纯白底 / 发丝描边 / 空气感投影） ----------
        public static Effect NewCardShadow()
        {
            return new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 12,
                ShadowDepth = 2,
                Opacity = 0.06,
                RenderingBias = RenderingBias.Performance
            };
        }

        public static Border CardBorder()
        {
            return new Border
            {
                Background = Card,
                BorderBrush = Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(22),
                Effect = NewCardShadow()
            };
        }

        /// <summary>把旧代码 new 出来的外壳 Border 升级为新卡片样式。</summary>
        public static void StyleCard(Border border)
        {
            border.Background = Card;
            border.BorderBrush = Line;
            border.BorderThickness = new Thickness(1);
            border.Effect = NewCardShadow();
        }

        /// <summary>窗口内部的信息分区。用卡片代替堆叠的输入框和发丝线，不承载业务逻辑。</summary>
        public static Border SurfacePanel(UIElement child, Thickness padding, Thickness margin)
        {
            return new Border
            {
                Background = Card,
                BorderBrush = Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = padding,
                Margin = margin,
                Child = child
            };
        }

        // ---------- 动效按钮（克制的大厂式反馈：悬停微暗、按下再暗，无缩放弹跳） ----------
        public static Button MakeButton(string text, Brush background)
        {
            EnsureTemplates();
            Brush normalized = NormalizeBrush(background);
            bool primary = SameBrush(normalized, Accent) || SameBrush(normalized, AccentDeep) || SameBrush(normalized, Green);
            bool ghost = normalized == Brushes.Transparent || (normalized is SolidColorBrush && ((SolidColorBrush)normalized).Color.A == 0);
            bool danger = SameBrush(normalized, RedSoft);
            var button = new Button
            {
                Content = text,
                Height = 38,
                MinWidth = 38,
                Padding = new Thickness(16, 7, 16, 7),
                Margin = new Thickness(3, 2, 3, 2),
                Background = normalized,
                Foreground = primary ? Brushes.White : (danger ? Freeze(180, 35, 47) : Ink),
                BorderBrush = ghost ? Brushes.Transparent : (primary ? normalized : Line),
                BorderThickness = ghost || primary ? new Thickness(0) : new Thickness(1),
                FontFamily = UiFont,
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                Template = buttonTemplate,
                SnapsToDevicePixels = true
            };
            return button;
        }

        static bool SameBrush(Brush left, Brush right)
        {
            var a = left as SolidColorBrush;
            var b = right as SolidColorBrush;
            return a != null && b != null && a.Color == b.Color;
        }

        public static Button MakeCloseButton()
        {
            var button = MakeButton("✕", Brushes.Transparent);
            button.Width = 30;
            button.Height = 30;
            button.Padding = new Thickness(0);
            button.Margin = new Thickness(0);
            button.FontFamily = new FontFamily("Segoe UI Symbol");
            button.FontSize = 13;
            button.FontWeight = FontWeights.Normal;
            button.Foreground = SubInk;
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            button.VerticalContentAlignment = VerticalAlignment.Center;
            button.ToolTip = "关闭";
            return button;
        }

        public static TextBlock MakePocketTagText(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Ink,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(7,0,7,0)
            };
        }

        public static Border MakePocketTag(TextBlock text)
        {
            return new Border
            {
                Background = Card,
                BorderBrush = Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(11),
                Height = 21,
                Child = text,
                Cursor = Cursors.Hand,
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.08 }
            };
        }

        static void EnsureTemplates()
        {
            if (buttonTemplate != null) return;
            buttonTemplate = (ControlTemplate)XamlReader.Parse(
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type Button}'>" +
                "<Grid x:Name='Root' Background='Transparent' TextElement.Foreground='{TemplateBinding Foreground}' RenderTransformOrigin='0.5,0.5'><Grid.RenderTransform><TranslateTransform x:Name='Motion'/></Grid.RenderTransform>" +
                "<Border x:Name='Bg' CornerRadius='8' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' SnapsToDevicePixels='True'/>" +
                "<Border x:Name='Shade' CornerRadius='8' Background='#101828' Opacity='0' IsHitTestVisible='False'/>" +
                "<Border x:Name='FocusRing' CornerRadius='9' BorderBrush='#4C6DA6' BorderThickness='1.5' Opacity='0' IsHitTestVisible='False' Margin='-2'/>" +
                "<ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='{TemplateBinding VerticalContentAlignment}' Margin='{TemplateBinding Padding}' RecognizesAccessKey='True'/>" +
                "</Grid>" +
                "<ControlTemplate.Triggers>" +
                "<Trigger Property='IsMouseOver' Value='True'><Trigger.EnterActions><BeginStoryboard><Storyboard>" +
                "<DoubleAnimation Storyboard.TargetName='Shade' Storyboard.TargetProperty='Opacity' To='0.055' Duration='0:0:0.1'/>" +
                "</Storyboard></BeginStoryboard></Trigger.EnterActions><Trigger.ExitActions><BeginStoryboard><Storyboard>" +
                "<DoubleAnimation Storyboard.TargetName='Shade' Storyboard.TargetProperty='Opacity' To='0' Duration='0:0:0.2'/>" +
                "</Storyboard></BeginStoryboard></Trigger.ExitActions></Trigger>" +
                "<Trigger Property='IsPressed' Value='True'><Trigger.EnterActions><BeginStoryboard><Storyboard>" +
                "<DoubleAnimation Storyboard.TargetName='Shade' Storyboard.TargetProperty='Opacity' To='0.11' Duration='0:0:0.06'/><DoubleAnimation Storyboard.TargetName='Motion' Storyboard.TargetProperty='Y' To='1' Duration='0:0:0.06'/>" +
                "</Storyboard></BeginStoryboard></Trigger.EnterActions><Trigger.ExitActions><BeginStoryboard><Storyboard>" +
                "<DoubleAnimation Storyboard.TargetName='Shade' Storyboard.TargetProperty='Opacity' To='0' Duration='0:0:0.12'/><DoubleAnimation Storyboard.TargetName='Motion' Storyboard.TargetProperty='Y' To='0' Duration='0:0:0.12'/>" +
                "</Storyboard></BeginStoryboard></Trigger.ExitActions></Trigger>" +
                "<Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='FocusRing' Property='Opacity' Value='1'/></Trigger>" +
                "<Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger>" +
                "</ControlTemplate.Triggers></ControlTemplate>");

            // 保留经过裁切修复的原生内容承载器，字体与配色统一。
            textBoxStyle = NativeTextBoxStyle();
            passwordBoxStyle = NativePasswordStyle();
            richTextBoxStyle = NativeRichTextBoxStyle();

            listBoxStyle = new Style(typeof(ListBox));
            listBoxStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            listBoxStyle.Setters.Add(new Setter(Control.BorderBrushProperty, Line));
            listBoxStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            listBoxStyle.Setters.Add(new Setter(Control.FontFamilyProperty, UiFont));
            listBoxStyle.Setters.Add(new Setter(Control.ForegroundProperty, Ink));

            listBoxItemStyle = (Style)XamlReader.Parse(
                "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ListBoxItem}'>" +
                "<Setter Property='Padding' Value='6,4,6,4'/>" +
                "<Setter Property='Margin' Value='1'/>" +
                "<Setter Property='Template'><Setter.Value>" +
                "<ControlTemplate TargetType='{x:Type ListBoxItem}'>" +
                "<Border x:Name='Bd' Background='{TemplateBinding Background}' CornerRadius='9' Padding='{TemplateBinding Padding}'>" +
                "<Grid>" +
                "<Border x:Name='HoverBg' Background='#0A1D1D1F' Opacity='0' CornerRadius='8' IsHitTestVisible='False'/>" +
                "<Border x:Name='SelBg' Background='#244C6DA6' Opacity='0' CornerRadius='8' IsHitTestVisible='False'/>" +
                "<ContentPresenter/>" +
                "</Grid>" +
                "</Border>" +
                "<ControlTemplate.Triggers>" +
                "<Trigger Property='IsMouseOver' Value='True'><Trigger.EnterActions><BeginStoryboard><Storyboard>" +
                "<DoubleAnimation Storyboard.TargetName='HoverBg' Storyboard.TargetProperty='Opacity' To='1' Duration='0:0:0.12'/>" +
                "</Storyboard></BeginStoryboard></Trigger.EnterActions><Trigger.ExitActions><BeginStoryboard><Storyboard>" +
                "<DoubleAnimation Storyboard.TargetName='HoverBg' Storyboard.TargetProperty='Opacity' To='0' Duration='0:0:0.18'/>" +
                "</Storyboard></BeginStoryboard></Trigger.ExitActions></Trigger>" +
                "<Trigger Property='IsSelected' Value='True'><Trigger.EnterActions><BeginStoryboard><Storyboard>" +
                "<DoubleAnimation Storyboard.TargetName='SelBg' Storyboard.TargetProperty='Opacity' To='1' Duration='0:0:0.12'/>" +
                "</Storyboard></BeginStoryboard></Trigger.EnterActions><Trigger.ExitActions><BeginStoryboard><Storyboard>" +
                "<DoubleAnimation Storyboard.TargetName='SelBg' Storyboard.TargetProperty='Opacity' To='0' Duration='0:0:0.18'/>" +
                "</Storyboard></BeginStoryboard></Trigger.ExitActions></Trigger>" +
                "</ControlTemplate.Triggers></ControlTemplate>" +
                "</Setter.Value></Setter></Style>");

            comboBoxItemStyle = (Style)XamlReader.Parse(
                "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ComboBoxItem}'>" +
                "<Setter Property='Padding' Value='8,5,8,5'/>" +
                "<Setter Property='Template'><Setter.Value>" +
                "<ControlTemplate TargetType='{x:Type ComboBoxItem}'>" +
                "<Border x:Name='Bd' Background='Transparent' CornerRadius='7' Padding='{TemplateBinding Padding}'>" +
                "<Grid>" +
                "<Border x:Name='HoverBg' Background='#0A1D1D1F' Opacity='0' CornerRadius='7' IsHitTestVisible='False'/>" +
                "<ContentPresenter/>" +
                "</Grid>" +
                "</Border>" +
                "<ControlTemplate.Triggers>" +
                "<Trigger Property='IsMouseOver' Value='True'><Trigger.EnterActions><BeginStoryboard><Storyboard>" +
                "<DoubleAnimation Storyboard.TargetName='HoverBg' Storyboard.TargetProperty='Opacity' To='1' Duration='0:0:0.1'/>" +
                "</Storyboard></BeginStoryboard></Trigger.EnterActions><Trigger.ExitActions><BeginStoryboard><Storyboard>" +
                "<DoubleAnimation Storyboard.TargetName='HoverBg' Storyboard.TargetProperty='Opacity' To='0' Duration='0:0:0.16'/>" +
                "</Storyboard></BeginStoryboard></Trigger.ExitActions></Trigger>" +
                "<Trigger Property='IsHighlighted' Value='True'><Setter Property='Background' Value='#0A1D1D1F' TargetName='Bd'/></Trigger>" +
                "</ControlTemplate.Triggers></ControlTemplate>" +
                "</Setter.Value></Setter></Style>");

            comboBoxStyle = (Style)XamlReader.Parse(
                "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ComboBox}'>" +
                "<Setter Property='Foreground' Value='#101828'/><Setter Property='Background' Value='#FFFFFFFF'/><Setter Property='BorderBrush' Value='#D0D5DD'/><Setter Property='BorderThickness' Value='1'/><Setter Property='FontFamily' Value='Microsoft YaHei UI'/><Setter Property='FontSize' Value='13.5'/><Setter Property='Padding' Value='10,5,8,5'/><Setter Property='MaxDropDownHeight' Value='320'/><Setter Property='SnapsToDevicePixels' Value='True'/>" +
                "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type ComboBox}'>" +
                "<Grid x:Name='Root' RenderTransformOrigin='0.5,0.5'><Grid.RenderTransform><ScaleTransform x:Name='Scale'/></Grid.RenderTransform>" +
                "<Border x:Name='Bd' CornerRadius='9' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'/>" +
                "<Border x:Name='Hover' CornerRadius='9' BorderBrush='#4C6DA6' BorderThickness='1.3' Opacity='0' IsHitTestVisible='False'/>" +
                "<Grid><Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width='32'/></Grid.ColumnDefinitions>" +
                "<ToggleButton x:Name='DropButton' Grid.ColumnSpan='2' Focusable='False' ClickMode='Press' IsChecked='{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'><ToggleButton.Template><ControlTemplate TargetType='{x:Type ToggleButton}'><Border Background='Transparent'/></ControlTemplate></ToggleButton.Template></ToggleButton>" +
                "<ContentPresenter x:Name='ContentSite' Grid.Column='0' Margin='10,0,8,0' VerticalAlignment='Center' HorizontalAlignment='Left' IsHitTestVisible='False' Content='{TemplateBinding SelectionBoxItem}' ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}' ContentStringFormat='{TemplateBinding SelectionBoxItemStringFormat}'/>" +
                "<TextBox x:Name='PART_EditableTextBox' Grid.Column='0' Margin='10,0,8,0' Padding='0' BorderThickness='0' Background='Transparent' Foreground='{TemplateBinding Foreground}' VerticalContentAlignment='Center' Visibility='Collapsed' Style='{x:Null}'/>" +
                "<Path x:Name='Arrow' Grid.Column='1' Width='9' Height='5' Stretch='Fill' Data='M 0 0 L 4.5 5 L 9 0' Stroke='#8E8E93' StrokeThickness='1.5' StrokeStartLineCap='Round' StrokeEndLineCap='Round' HorizontalAlignment='Center' VerticalAlignment='Center' IsHitTestVisible='False' RenderTransformOrigin='0.5,0.5'><Path.RenderTransform><RotateTransform x:Name='ArrowRotate'/></Path.RenderTransform></Path>" +
                "</Grid>" +
                "<Popup x:Name='PART_Popup' Placement='Bottom' IsOpen='{TemplateBinding IsDropDownOpen}' AllowsTransparency='True' PopupAnimation='Fade' Focusable='False'><Grid MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}' MaxHeight='{TemplateBinding MaxDropDownHeight}' Margin='0,5,0,8'><Border Background='#FFFFFFFF' BorderBrush='#E8E8ED' BorderThickness='1' CornerRadius='10' Padding='4'><Border.Effect><DropShadowEffect Color='#000000' BlurRadius='20' ShadowDepth='4' Opacity='0.12'/></Border.Effect><ScrollViewer CanContentScroll='True' VerticalScrollBarVisibility='Auto'><ItemsPresenter/></ScrollViewer></Border></Grid></Popup>" +
                "</Grid><ControlTemplate.Triggers>" +
                "<Trigger Property='IsEditable' Value='True'><Setter TargetName='ContentSite' Property='Visibility' Value='Collapsed'/><Setter TargetName='PART_EditableTextBox' Property='Visibility' Value='Visible'/><Setter TargetName='DropButton' Property='Grid.Column' Value='1'/><Setter TargetName='DropButton' Property='Grid.ColumnSpan' Value='1'/></Trigger>" +
                "<Trigger Property='IsMouseOver' Value='True'><Trigger.EnterActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='Hover' Storyboard.TargetProperty='Opacity' To='0.72' Duration='0:0:0.13'/><DoubleAnimation Storyboard.TargetName='Scale' Storyboard.TargetProperty='ScaleX' To='1.012' Duration='0:0:0.13'/><DoubleAnimation Storyboard.TargetName='Scale' Storyboard.TargetProperty='ScaleY' To='1.012' Duration='0:0:0.13'/></Storyboard></BeginStoryboard></Trigger.EnterActions><Trigger.ExitActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='Hover' Storyboard.TargetProperty='Opacity' To='0' Duration='0:0:0.2'/><DoubleAnimation Storyboard.TargetName='Scale' Storyboard.TargetProperty='ScaleX' To='1' Duration='0:0:0.2'/><DoubleAnimation Storyboard.TargetName='Scale' Storyboard.TargetProperty='ScaleY' To='1' Duration='0:0:0.2'/></Storyboard></BeginStoryboard></Trigger.ExitActions></Trigger>" +
                "<Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='Hover' Property='Opacity' Value='1'/></Trigger>" +
                "<Trigger Property='IsDropDownOpen' Value='True'><Trigger.EnterActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='ArrowRotate' Storyboard.TargetProperty='Angle' To='180' Duration='0:0:0.18'><DoubleAnimation.EasingFunction><CubicEase EasingMode='EaseOut'/></DoubleAnimation.EasingFunction></DoubleAnimation></Storyboard></BeginStoryboard></Trigger.EnterActions><Trigger.ExitActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='ArrowRotate' Storyboard.TargetProperty='Angle' To='0' Duration='0:0:0.18'><DoubleAnimation.EasingFunction><CubicEase EasingMode='EaseOut'/></DoubleAnimation.EasingFunction></DoubleAnimation></Storyboard></BeginStoryboard></Trigger.ExitActions></Trigger>" +
                "<Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.48'/></Trigger>" +
                "</ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>");

            checkBoxStyle = (Style)XamlReader.Parse(
                "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type CheckBox}'>" +
                "<Setter Property='Foreground' Value='#101828'/><Setter Property='FontFamily' Value='Microsoft YaHei UI'/><Setter Property='FontSize' Value='13'/><Setter Property='Cursor' Value='Hand'/><Setter Property='VerticalContentAlignment' Value='Center'/>" +
                "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type CheckBox}'><Grid Background='Transparent'><Grid.ColumnDefinitions><ColumnDefinition Width='23'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>" +
                "<Border x:Name='Box' Width='18' Height='18' CornerRadius='5' Background='#FFFFFFFF' BorderBrush='#C7C7CC' BorderThickness='1.2' VerticalAlignment='Center'><Path x:Name='Tick' Data='M 3 8 L 7 12 L 15 4' Stroke='White' StrokeThickness='2.2' StrokeStartLineCap='Round' StrokeEndLineCap='Round' Opacity='0'/></Border>" +
                "<ContentPresenter Grid.Column='1' Margin='3,0,0,0' VerticalAlignment='Center' RecognizesAccessKey='True'/></Grid>" +
                "<ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Box' Property='BorderBrush' Value='#4C6DA6'/><Setter TargetName='Box' Property='Background' Value='#EDF2FA'/></Trigger><Trigger Property='IsChecked' Value='True'><Setter TargetName='Box' Property='Background' Value='#4C6DA6'/><Setter TargetName='Box' Property='BorderBrush' Value='#4C6DA6'/><Setter TargetName='Tick' Property='Opacity' Value='1'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger></ControlTemplate.Triggers>" +
                "</ControlTemplate></Setter.Value></Setter></Style>");

            datePickerStyle = (Style)XamlReader.Parse(
                "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type DatePicker}'>" +
                "<Setter Property='Foreground' Value='#101828'/><Setter Property='Background' Value='#FFFFFFFF'/><Setter Property='BorderBrush' Value='#D0D5DD'/><Setter Property='BorderThickness' Value='1'/><Setter Property='FontFamily' Value='Microsoft YaHei UI'/><Setter Property='FontSize' Value='13'/><Setter Property='Padding' Value='10,5,8,5'/>" +
                "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type DatePicker}'><Grid x:Name='PART_Root'><Border x:Name='Bd' CornerRadius='9' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'/><Grid><Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width='36'/></Grid.ColumnDefinitions>" +
                "<DatePickerTextBox x:Name='PART_TextBox' Grid.Column='0' Style='{x:Null}' Background='Transparent' BorderThickness='0' Padding='10,0,8,0' Foreground='{TemplateBinding Foreground}' VerticalContentAlignment='Center'/>" +
                "<Button x:Name='PART_Button' Grid.Column='1' Focusable='False' Background='Transparent' BorderThickness='0' Cursor='Hand'><Button.Template><ControlTemplate TargetType='{x:Type Button}'><Border x:Name='CalBg' Background='Transparent' CornerRadius='7' Margin='3'><Grid><Rectangle Width='14' Height='13' RadiusX='2' RadiusY='2' Stroke='#667085' StrokeThickness='1.3'/><Line X1='-5' Y1='-3' X2='5' Y2='-3' Stroke='#667085' StrokeThickness='1.2'/><Ellipse Width='2.5' Height='2.5' Fill='#4C6DA6' Margin='0,4,0,0'/></Grid></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='CalBg' Property='Background' Value='#EDF2FA'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Button.Template></Button>" +
                "</Grid><Border x:Name='FocusRing' CornerRadius='10' BorderBrush='#4C6DA6' BorderThickness='1.4' Opacity='0' IsHitTestVisible='False'/><Popup x:Name='PART_Popup' Placement='Bottom' AllowsTransparency='True' StaysOpen='False' IsOpen='{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}}'><Border Background='White' BorderBrush='#EAECF0' BorderThickness='1' CornerRadius='10' Padding='5' Margin='0,5,0,8'><Border.Effect><DropShadowEffect Color='#000000' BlurRadius='16' ShadowDepth='3' Opacity='0.10'/></Border.Effect><Calendar x:Name='PART_Calendar' BorderThickness='0' Background='White'/></Border></Popup></Grid>" +
                "<ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='FocusRing' Property='Opacity' Value='0.65'/></Trigger><Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='FocusRing' Property='Opacity' Value='1'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>");

            toolTipStyle = (Style)XamlReader.Parse(
                "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ToolTip}'>" +
                "<Setter Property='Foreground' Value='#FFFFFFFF'/><Setter Property='Background' Value='#F0182230'/><Setter Property='Padding' Value='10,7'/><Setter Property='FontFamily' Value='Microsoft YaHei UI'/><Setter Property='FontSize' Value='12'/><Setter Property='MaxWidth' Value='360'/><Setter Property='Placement' Value='Mouse'/><Setter Property='HorizontalOffset' Value='10'/><Setter Property='VerticalOffset' Value='14'/>" +
                "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type ToolTip}'><Border CornerRadius='8' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}'><Border.Effect><DropShadowEffect Color='#50000000' BlurRadius='14' ShadowDepth='3'/></Border.Effect><ContentPresenter/></Border></ControlTemplate></Setter.Value></Setter></Style>");

            scrollBarStyle = new Style(typeof(ScrollBar));
            scrollBarStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            scrollBarStyle.Setters.Add(new Setter(Control.ForegroundProperty, SubInk));
        }

        static Style NativeTextBoxStyle()
        {
            var style = new Style(typeof(TextBox));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, InputLine));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Card));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Ink));
            style.Setters.Add(new Setter(Control.FontFamilyProperty, UiFont));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 13.5));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 5, 10, 5)));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(Control.SnapsToDevicePixelsProperty, true));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            return style;
        }

        static Style InputStyle(Type targetType)
        {
            var style = new Style(targetType);
            style.Setters.Add(new Setter(Control.BorderBrushProperty, InputLine));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Card));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Ink));
            style.Setters.Add(new Setter(Control.FontFamilyProperty, UiFont));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(Control.SnapsToDevicePixelsProperty, true));
            string xaml =
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type " + targetType.Name + "}'>" +
                "<Grid>" +
                "<Border x:Name='Bd' CornerRadius='9' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' Padding='{TemplateBinding Padding}' SnapsToDevicePixels='True'>" +
                "<ScrollViewer x:Name='PART_ContentHost' Margin='0' HorizontalAlignment='Stretch' VerticalAlignment='Stretch' CanContentScroll='False'/>" +
                "</Border>" +
                "<Border x:Name='FocusRing' CornerRadius='10' BorderBrush='#4C6DA6' BorderThickness='1.6' Opacity='0' IsHitTestVisible='False' Margin='-1'/>" +
                "</Grid>" +
                "<ControlTemplate.Triggers>" +
                "<Trigger Property='IsKeyboardFocused' Value='True'><Trigger.EnterActions><BeginStoryboard><Storyboard>" +
                "<DoubleAnimation Storyboard.TargetName='FocusRing' Storyboard.TargetProperty='Opacity' To='1' Duration='0:0:0.14'/>" +
                "</Storyboard></BeginStoryboard></Trigger.EnterActions><Trigger.ExitActions><BeginStoryboard><Storyboard>" +
                "<DoubleAnimation Storyboard.TargetName='FocusRing' Storyboard.TargetProperty='Opacity' To='0' Duration='0:0:0.2'/>" +
                "</Storyboard></BeginStoryboard></Trigger.ExitActions></Trigger>" +
                "</ControlTemplate.Triggers></ControlTemplate>";
            style.Setters.Add(new Setter(Control.TemplateProperty, XamlReader.Parse(xaml)));
            return style;
        }

        static Style NativePasswordStyle()
        {
            var style = new Style(typeof(PasswordBox));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, InputLine));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Card));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Ink));
            style.Setters.Add(new Setter(Control.FontFamilyProperty, UiFont));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 14.0));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 4, 10, 4)));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(Control.SnapsToDevicePixelsProperty, true));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            return style;
        }

        static Style NativeRichTextBoxStyle()
        {
            var style = new Style(typeof(RichTextBox));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, Line));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Card));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Ink));
            style.Setters.Add(new Setter(Control.FontFamilyProperty, UiFont));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 13.5));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12)));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            return style;
        }

        /// <summary>圆角浅灰结果框（只读 RichTextBox / TextBox），用于行情、审核结果等数据展示区。</summary>
        public static Style RoundedViewerStyle(Type targetType)
        {
            var style = new Style(targetType);
            style.Setters.Add(new Setter(Control.BackgroundProperty, Inner));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            string xaml =
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type " + targetType.Name + "}'>" +
                "<Border CornerRadius='10' Background='{TemplateBinding Background}' BorderBrush='#E9E9EE' BorderThickness='1' Padding='{TemplateBinding Padding}' SnapsToDevicePixels='True'>" +
                "<ScrollViewer x:Name='PART_ContentHost' Margin='0'/>" +
                "</Border></ControlTemplate>";
            style.Setters.Add(new Setter(Control.TemplateProperty, XamlReader.Parse(xaml)));
            return style;
        }

        /// <summary>发丝分割线。</summary>
        public static Border Hairline(Thickness margin)
        {
            return new Border { Height = 1, Background = Line, Margin = margin, SnapsToDevicePixels = true };
        }

        /// <summary>小节标签（灰色小字，国外官网式分区标题）。</summary>
        public static TextBlock SectionLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = SubInk,
                FontFamily = UiFont
            };
        }

        /// <summary>胶囊徽标（涨跌、状态等强调信息）。</summary>
        public static Border PillBadge(string text, Brush foreground, Brush background, double minWidth)
        {
            return new Border
            {
                Background = background,
                CornerRadius = new CornerRadius(12),
                Height = 24,
                MinWidth = minWidth,
                Padding = new Thickness(8, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 11.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = foreground,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
        }

        /// <summary>
        /// 给窗口套上主题：统一字体、输入框/列表样式，并挂"打开时淡入上浮"动效。
        /// </summary>
        public static void StyleWindow(Window window)
        {
            if (window == null) return;
            TextSelection.Install();
            EnsureTemplates();
            window.FontFamily = UiFont;
            window.Foreground = Ink;
            window.Background = window.AllowsTransparency ? Brushes.Transparent : Paper;
            window.UseLayoutRounding = true;
            window.SnapsToDevicePixels = true;
            // 输入控件使用 WPF 原生模板：自定义 ContentHost 在部分 DPI 下会裁掉
            // TextBox / PasswordBox 的首行，尤其是密码圆点。优先保证完整、稳定输入。
            TextOptions.SetTextFormattingMode(window, TextFormattingMode.Display);
            if (!window.Resources.Contains(typeof(TextBox))) window.Resources.Add(typeof(TextBox), textBoxStyle);
            if (!window.Resources.Contains(typeof(PasswordBox))) window.Resources.Add(typeof(PasswordBox), passwordBoxStyle);
            if (!window.Resources.Contains(typeof(RichTextBox))) window.Resources.Add(typeof(RichTextBox), richTextBoxStyle);
            if (!window.Resources.Contains(typeof(ListBox))) window.Resources.Add(typeof(ListBox), listBoxStyle);
            if (!window.Resources.Contains(typeof(ListBoxItem))) window.Resources.Add(typeof(ListBoxItem), listBoxItemStyle);
            if (!window.Resources.Contains(typeof(ComboBoxItem))) window.Resources.Add(typeof(ComboBoxItem), comboBoxItemStyle);
            if (!window.Resources.Contains(typeof(ComboBox))) window.Resources.Add(typeof(ComboBox), comboBoxStyle);
            if (!window.Resources.Contains(typeof(CheckBox))) window.Resources.Add(typeof(CheckBox), checkBoxStyle);
            if (!window.Resources.Contains(typeof(DatePicker))) window.Resources.Add(typeof(DatePicker), datePickerStyle);
            if (!window.Resources.Contains(typeof(ToolTip))) window.Resources.Add(typeof(ToolTip), toolTipStyle);
            if (!window.Resources.Contains(typeof(ScrollBar))) window.Resources.Add(typeof(ScrollBar), scrollBarStyle);
            window.Loaded += delegate
            {
                ApplyModernControls(window);
                // 大型图片/文档窗口避免整面板阴影重绘。
                var shell = window.Content as Border;
                if (shell != null && window.ActualWidth * window.ActualHeight > 360000) shell.Effect = null;
            };
            window.IsVisibleChanged += delegate
            {
                if (window.IsVisible && window.Width * window.Height <= 360000)
                    AnimateOpen(window.Content as UIElement, 0);
            };
        }

        public static void ApplyModernControls(DependencyObject root)
        {
            if (root == null) return;
            var text = root as TextBox;
            if (text != null)
            {
                text.Style = textBoxStyle;
                if (!text.AcceptsReturn && text.Height <= 48)
                {
                    text.Padding = new Thickness(10, 4, 10, 4);
                    text.VerticalContentAlignment = VerticalAlignment.Center;
                }
                else if (text.AcceptsReturn) text.VerticalContentAlignment = VerticalAlignment.Top;
            }
            var password = root as PasswordBox;
            if (password != null)
            {
                password.Style = passwordBoxStyle;
                password.Padding = new Thickness(10, 4, 10, 4);
                password.VerticalContentAlignment = VerticalAlignment.Center;
            }
            var rich = root as RichTextBox; if (rich != null) rich.Style = richTextBoxStyle;
            var list = root as ListBox; if (list != null) list.Style = listBoxStyle;
            var combo = root as ComboBox; if (combo != null) combo.Style = comboBoxStyle;
            var check = root as CheckBox; if (check != null) check.Style = checkBoxStyle;
            var date = root as DatePicker; if (date != null) date.Style = datePickerStyle;
            int count = 0; try { count = VisualTreeHelper.GetChildrenCount(root); } catch { }
            for (int index = 0; index < count; index++) ApplyModernControls(VisualTreeHelper.GetChild(root, index));
        }

        /// <summary>窗口/面板打开动效：淡入 + 轻轻上浮。</summary>
        public static void AnimateOpen(UIElement element, double rise)
        {
            if (element == null) return;
            element.BeginAnimation(UIElement.OpacityProperty, null);
            var move = element.RenderTransform as TranslateTransform;
            if (move == null)
            {
                move = new TranslateTransform();
                element.RenderTransform = move;
            }
            move.BeginAnimation(TranslateTransform.YProperty, null);
            move.BeginAnimation(TranslateTransform.XProperty, null);
            var fade = new DoubleAnimation(0.72, 1, TimeSpan.FromMilliseconds(135));
            fade.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            var slide = new DoubleAnimation(rise, 0, TimeSpan.FromMilliseconds(165));
            slide.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            element.BeginAnimation(UIElement.OpacityProperty, fade);
            move.BeginAnimation(TranslateTransform.YProperty, slide);
        }

        /// <summary>Toast 动效：从右侧滑入 + 淡入。</summary>
        public static void AnimateToastIn(UIElement element)
        {
            if (element == null) return;
            var move = new TranslateTransform();
            element.RenderTransform = move;
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240));
            fade.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            var slide = new DoubleAnimation(26, 0, TimeSpan.FromMilliseconds(210));
            slide.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            element.BeginAnimation(UIElement.OpacityProperty, fade);
            move.BeginAnimation(TranslateTransform.XProperty, slide);
        }

        /// <summary>元素切换时的淡入（用于 AI 口袋对话/图片页切换等）。</summary>
        public static void FadeIn(UIElement element)
        {
            if (element == null) return;
            element.BeginAnimation(UIElement.OpacityProperty, null);
            var fade = new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(180));
            fade.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            element.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        /// <summary>内容反馈：轻轻弹一下（用于保存成功等状态变化）。</summary>
        public static void Pulse(UIElement element)
        {
            if (element == null) return;
            var scale = element.RenderTransform as ScaleTransform;
            if (scale == null)
            {
                scale = new ScaleTransform(1, 1);
                element.RenderTransform = scale;
                element.RenderTransformOrigin = new Point(0.5, 0.5);
            }
            var bounce = new DoubleAnimation(1.0, 1.06, TimeSpan.FromMilliseconds(120)) { AutoReverse = true };
            bounce.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, bounce);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, bounce);
        }

        public static TextBox ReadOnlyText(string value,double size)
        {
            var text=new TextBox{Text=value??"",IsReadOnly=true,IsReadOnlyCaretVisible=true,
                AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,FontSize=size,FontFamily=UiFont,
                Foreground=Ink,Background=Brushes.Transparent,BorderThickness=new Thickness(0),
                Padding=new Thickness(0),HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
            var menu=new ContextMenu();
            menu.Items.Add(new MenuItem{Header="复制选中文字",Command=ApplicationCommands.Copy,CommandTarget=text});
            menu.Items.Add(new MenuItem{Header="全选",Command=ApplicationCommands.SelectAll,CommandTarget=text});
            var all=new MenuItem{Header="复制全部文字"};all.Click+=delegate{TextSelection.Copy(text.Text);};menu.Items.Add(all);text.ContextMenu=menu;
            return text;
        }

        /// <summary>统一标题文字。</summary>
        public static TextBlock Title(string text, double size)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = size,
                FontWeight = FontWeights.SemiBold,
                Foreground = Ink,
                FontFamily = UiFont
            };
        }

        /// <summary>统一副标题文字。</summary>
        public static TextBlock Subtitle(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = SubInk,
                FontFamily = UiFont,
                Margin = new Thickness(0, 3, 0, 0)
            };
        }

        /// <summary>统一的轻量提示框，代替样式和当前产品不一致的系统弹窗。</summary>
        public static void Alert(Window owner, string title, string message)
        {
            ShowProductDialog(owner, title, message, false, "知道了");
        }

        /// <summary>统一的二次确认框；返回值与 MessageBox Yes/No 的含义一致。</summary>
        public static bool Confirm(Window owner, string title, string message, string confirmText)
        {
            return ShowProductDialog(owner, title, message, true, String.IsNullOrWhiteSpace(confirmText) ? "确认" : confirmText);
        }

        static bool ShowProductDialog(Window owner, string title, string message, bool cancellable, string confirmText)
        {
            bool accepted = false;
            var dialog = new Window
            {
                Title = title ?? "博道咪",
                Width = 430,
                Height = 220,
                MinWidth = 360,
                MinHeight = 190,
                SizeToContent = SizeToContent.Manual,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.CanResize,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                Topmost = owner == null || owner.Topmost,
                WindowStartupLocation = owner != null && owner.IsVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen
            };
            if (owner != null && owner.IsVisible) dialog.Owner = owner;
            StyleWindow(dialog);
            var card = CardBorder();card.CornerRadius = new CornerRadius(18);card.Padding = new Thickness(22);card.Margin = new Thickness(8);
            var root = new Grid();root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var header = new Grid { Cursor = Cursors.SizeAll };header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });header.Children.Add(Title(title ?? "提示", 18));var close = MakeCloseButton();close.Click += delegate { dialog.Close(); };Grid.SetColumn(close, 1);header.Children.Add(close);header.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) try { dialog.DragMove(); } catch { } };root.Children.Add(header);
            var body = ReadOnlyText(message,14);body.Margin=new Thickness(2,16,2,14);dialog.Height=Math.Min(SystemParameters.WorkArea.Height-40,(message??"").Length>240?540:280);Grid.SetRow(body, 1);root.Children.Add(body);
            var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };if (cancellable) { var cancel = MakeButton("取消", Neutral);cancel.Click += delegate { dialog.Close(); };actions.Children.Add(cancel); }var ok = MakeButton(confirmText, Accent);ok.Foreground = Brushes.White;ok.Click += delegate { accepted = true;dialog.Close(); };actions.Children.Add(ok);Grid.SetRow(actions, 2);root.Children.Add(actions);card.Child = root;dialog.Content = card;dialog.ShowDialog();return accepted;
        }
    }
}
