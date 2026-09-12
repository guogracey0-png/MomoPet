using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace MomoPetApp
{
    public partial class PetController
    {
        Grid imagePocketContent;
        UIElement aiPocketChatContent;
        TextBox pocketChatInput;
        TextBlock pocketChatStatus;
        RichTextBox pocketChatHistory;
        ComboBox pocketChatModelCombo,pocketChatSourceCombo;
        readonly List<Dictionary<string,object>> pocketChatMessages=new List<Dictionary<string,object>>();

        UIElement BuildAiPocketChatContent()
        {
            var grid=new Grid { Margin=new Thickness(20,6,20,10) };
            grid.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });

            var top=new Grid { Margin=new Thickness(0,0,0,12) };top.ColumnDefinitions.Add(new ColumnDefinition());top.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
            var modelArea=new StackPanel();modelArea.Children.Add(new TextBlock { Text="文本来源与模型",FontWeight=FontWeights.Bold });
            pocketChatSourceCombo=new ComboBox{Height=34,Margin=new Thickness(0,4,8,0),Padding=new Thickness(8,4,8,4),DisplayMemberPath="Name",ItemsSource=imageAiConfig.TextProfiles,SelectedItem=ActiveTextProfile(),ToolTip="切换已保存的文本来源，不需要重新填写 Key"};pocketChatSourceCombo.SelectionChanged+=delegate{var selected=pocketChatSourceCombo.SelectedItem as ImageProviderProfile;if(selected!=null&&selected!=ActiveTextProfile())SelectTextProfile(selected);};modelArea.Children.Add(pocketChatSourceCombo);
            pocketChatModelCombo=new ComboBox { Height=34,Margin=new Thickness(0,4,8,0),Padding=new Thickness(8,4,8,4),IsEditable=true,IsTextSearchEnabled=true,StaysOpenOnEdit=true,ItemsSource=ActiveTextModels() };pocketChatModelCombo.Text=ActiveTextModel()??"";EnableModelSearch(pocketChatModelCombo,delegate{return ActiveTextModels();});modelArea.Children.Add(pocketChatModelCombo);top.Children.Add(modelArea);
            var settings=MakeButton("⚙ 模型设置",new SolidColorBrush(Color.FromRgb(235,229,222)));settings.VerticalAlignment=VerticalAlignment.Bottom;settings.Click+=delegate{ShowImageAiSettings();};Grid.SetColumn(settings,1);top.Children.Add(settings);grid.Children.Add(top);

            pocketChatHistory=new RichTextBox { IsReadOnly=true,IsReadOnlyCaretVisible=false,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(16),FontSize=14,Background=Ui.Inner,Foreground=Ui.Ink,BorderBrush=Ui.Line,BorderThickness=new Thickness(1),ToolTip="对话内容可选中并复制" };
            pocketChatHistory.Document=new FlowDocument{PagePadding=new Thickness(0),FontFamily=new FontFamily("Microsoft YaHei UI"),FontSize=14,Foreground=Ui.Ink,LineHeight=23};
            Grid.SetRow(pocketChatHistory,1);grid.Children.Add(pocketChatHistory);

            pocketChatInput=new TextBox { Height=105,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new Thickness(10),VerticalContentAlignment=VerticalAlignment.Top,Margin=new Thickness(0,10,0,6),ToolTip="输入文字；Ctrl+Enter 发送" };
            pocketChatInput.PreviewKeyDown+=delegate(object s,System.Windows.Input.KeyEventArgs e){if(e.Key==System.Windows.Input.Key.Enter&&(System.Windows.Input.Keyboard.Modifiers&System.Windows.Input.ModifierKeys.Control)!=0){SendPocketChat();e.Handled=true;}};Grid.SetRow(pocketChatInput,2);grid.Children.Add(pocketChatInput);

            var bottom=new Grid();bottom.ColumnDefinitions.Add(new ColumnDefinition());bottom.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });bottom.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
            pocketChatStatus=new TextBlock { Foreground=Ui.SubInk,VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap,Text="输入内容后发送，结果可选中复制" };bottom.Children.Add(pocketChatStatus);
            var clear=MakeButton("清空对话",Brushes.Transparent);clear.Click+=delegate{pocketChatMessages.Clear();pocketChatHistory.Document.Blocks.Clear();pocketChatStatus.Text="对话已清空";};Grid.SetColumn(clear,1);bottom.Children.Add(clear);
            var send=MakeButton("发送",new SolidColorBrush(Color.FromRgb(255,126,115)));send.Foreground=Brushes.White;send.Click+=delegate{SendPocketChat();};Grid.SetColumn(send,2);bottom.Children.Add(send);Grid.SetRow(bottom,3);grid.Children.Add(bottom);
            return grid;
        }

        void ShowAiPocketChat()
        {
            if(imageEditorPanel==null)BuildImageEditor();RememberCurrentImagePrompt();imagePocketContent.Visibility=Visibility.Collapsed;aiPocketChatContent.Visibility=Visibility.Visible;Ui.FadeIn(aiPocketChatContent);if(imageStatus!=null)imageStatus.Visibility=Visibility.Collapsed;
            if(pocketChatSourceCombo!=null){pocketChatSourceCombo.ItemsSource=null;pocketChatSourceCombo.ItemsSource=imageAiConfig.TextProfiles;pocketChatSourceCombo.SelectedItem=ActiveTextProfile();}if(pocketChatModelCombo!=null){pocketChatModelCombo.ItemsSource=null;pocketChatModelCombo.ItemsSource=ActiveTextModels();pocketChatModelCombo.Text=ActiveTextModel()??"";}
        }

        void ShowAiPocketImage()
        {
            if(imageEditorPanel==null)BuildImageEditor();aiPocketChatContent.Visibility=Visibility.Collapsed;imagePocketContent.Visibility=Visibility.Visible;Ui.FadeIn(imagePocketContent);if(imageStatus!=null)imageStatus.Visibility=Visibility.Visible;
        }

        void OpenAiPocketChat(string prefill)
        {
            if(stashPanel!=null)stashPanel.Hide();if(imageEditorPanel==null)BuildImageEditor();RestoreShelvedIfNeeded(imageEditorPanel);ShowAiPocketChat();if(!String.IsNullOrEmpty(prefill))pocketChatInput.Text=prefill;PositionImageEditor();imageEditorPanel.Show();imageEditorPanel.Activate();pocketChatInput.Focus();pocketChatInput.CaretIndex=pocketChatInput.Text.Length;
        }

        void OpenAiPocketImageEmpty()
        {
            if(imageEditorPanel==null)BuildImageEditor();RestoreShelvedIfNeeded(imageEditorPanel);editingImageItem=null;originalBitmap=null;workingBitmap=null;workingEncodedBytes=null;cropModeActive=false;ClearCompressionCandidate();ClearImageTextLayer();editorImage.Source=null;widthBox.Clear();heightBox.Clear();ResetImageHistory();if(imageAiConfig.RememberImagePrompt==true)redrawPromptBox.Text=imageAiConfig.SavedImagePrompt??"";else redrawPromptBox.Clear();recognizedTextBox.Clear();ResetCrop();ShowImageToolMode("generate");ShowAiPocketImage();imageStatus.Text="输入提示词即可文生图；已开启保留时，下次会自动带回本次提示词";PositionImageEditor();imageEditorPanel.Show();imageEditorPanel.Activate();
        }

        void SendPocketChat()
        {
            if(imageAiBusy){pocketChatStatus.Text="上一条请求仍在运行";return;}string text=(pocketChatInput.Text??"").Trim(),model=(pocketChatModelCombo.Text??"").Trim(),key=LoadActiveTextKey();
            if(String.IsNullOrEmpty(model)){model=(ActiveTextModel()??"").Trim();if(!String.IsNullOrEmpty(model))pocketChatModelCombo.Text=model;}
            if(String.IsNullOrEmpty(text)){pocketChatStatus.Text="请先输入内容";return;}if(String.IsNullOrEmpty(ActiveTextBaseUrl())||String.IsNullOrEmpty(model)||String.IsNullOrEmpty(key)){pocketChatStatus.Text="请先配置当前文本接口的 Base URL、API Key 和模型";ShowImageAiSettings();return;}
            var profile=ActiveTextProfile();if(profile!=null)profile.Model=model;imageAiConfig.TextModel=model;if(!ActiveTextModels().Contains(model))ActiveTextModels().Add(model);SaveImageAiConfig();
            var userMessage=new Dictionary<string,object>{{"role","user"},{"content",text}};pocketChatMessages.Add(userMessage);AppendPocketChat("你",text);pocketChatInput.Clear();imageAiBusy=true;pocketChatStatus.Text="AI 正在回复…";
            var messages=pocketChatMessages.Select(x=>(object)x).ToArray();var request=new Dictionary<string,object>{{"mode","chat"},{"provider",TextUsesToApis()?"toapis":"official"},{"base_url",ActiveTextBaseUrl()},{"model",model},{"messages",messages}};
            RunImageHelper(request,key,null,delegate(Dictionary<string,object> response){imageAiBusy=false;if(!ImageResponseOk(response)){pocketChatStatus.Text=imageStatus.Text+" · 原文已保留，可重新发送";if(String.IsNullOrEmpty(pocketChatInput.Text))pocketChatInput.Text=text;pocketChatMessages.Remove(userMessage);return;}string answer=Convert.ToString(response["text"]);pocketChatMessages.Add(new Dictionary<string,object>{{"role","assistant"},{"content",answer}});AppendPocketChat("AI",answer);pocketChatStatus.Text="回复完成 · 内容可选中复制";});
        }

        void AppendPocketChat(string role,string content)
        {
            bool assistant=role=="AI";var section=new Section{Background=assistant?Ui.Card:Ui.AccentSoft,BorderBrush=assistant?Ui.Line:Ui.AccentSoft,BorderThickness=new Thickness(1),Padding=new Thickness(14,11,14,12),Margin=new Thickness(0,0,0,10)};
            section.Blocks.Add(new Paragraph(new Run(assistant?"博道咪 AI":"你"){Foreground=assistant?Ui.AccentDeep:Ui.Ink,FontWeight=FontWeights.Bold}){Margin=new Thickness(0,0,0,7),FontSize=12.5});
            string[] lines=(content??"").Replace("\r\n","\n").Replace('\r','\n').Split('\n');foreach(string raw in lines){string line=raw.TrimEnd();if(String.IsNullOrWhiteSpace(line)){section.Blocks.Add(new Paragraph{Margin=new Thickness(0,0,0,5)});continue;}var paragraph=new Paragraph{Margin=new Thickness(0,0,0,6)};AddAiInlineFormatting(paragraph,line);section.Blocks.Add(paragraph);}pocketChatHistory.Document.Blocks.Add(section);pocketChatHistory.ScrollToEnd();
        }
    }
}
