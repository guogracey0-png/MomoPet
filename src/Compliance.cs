using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MomoPetApp
{
    public partial class PetController
    {
        Window compliancePanel;
        TextBox complianceInput,complianceResult;
        RichTextBox complianceOriginalView;
        TextBlock complianceStatus,complianceSource;
        Image compliancePreview;
        ComboBox complianceModelBox;
        string complianceImagePath;

        class ComplianceHit { public string Text; public string Detail; }

        string ComplianceRules()
        {
            try { string path=EmbeddedRuntime.ResolveFile("compliance-rules.txt",root); if(File.Exists(path)) return File.ReadAllText(path,System.Text.Encoding.UTF8); }
            catch { }
            return "禁用词：保证、承诺、保障、确保、高收益、无风险、保本、保值、必涨、暴涨、稳赚不赔、内部消息、独家信息、传闻。敏感词包括短期业绩、规模上限、稳、优、领先、安全、低风险、预测、未来收益、推荐、排名评级、最、港股、科创、定投、净值增长收益涨等，须说明风险和修改建议。";
        }

        string ComplianceInstruction()
        {
            return "你是博道基金营销素材的内部合规初筛助手。请严格依据下列合规词库逐字审核，不得自行弱化规则。图片时必须审核所有可见文字、标题、图表、角标和营销语。\n\n"+ComplianceRules()+"\n\n请按以下结构输出，语言简洁但要完整：\n1. 审核结论：通过 / 有风险 / 不可使用。\n2. 命中项必须每项独占一行，严格采用： 【命中原文】原文片段【类别】禁用词或敏感词【原因】具体违规或风险原因【建议】可执行修改建议。命中原文必须与待审核文案完全一致，不能改写。\n3. 未命中时明确写“未发现词库命中项，但仍建议人工复核语境”。\n4. 不要因为素材已有一般性风险提示就忽略禁用词。\n5. 结尾固定写：本结果仅作内部初筛，最终以合规人员意见为准。";
        }

        void BuildCompliancePanel()
        {
            compliancePanel=new Window{Title="博道咪合规初筛",Width=720,Height=610,MinWidth=560,MinHeight=440,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.CanResize,ShowInTaskbar=false,AllowsTransparency=true,Background=Brushes.Transparent,Topmost=pet.Topmost};Ui.StyleWindow(compliancePanel);
            var outer=new Border{CornerRadius=new CornerRadius(16),Padding=new Thickness(18)};Ui.StyleCard(outer);
            var rootGrid=new Grid();rootGrid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});rootGrid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});rootGrid.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});rootGrid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var header=new Grid();header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});header.Children.Add(Ui.Title("🛡  博道咪合规初筛",20));var close=Ui.MakeCloseButton();close.Click+=delegate{compliancePanel.Hide();};Grid.SetColumn(close,1);header.Children.Add(close);rootGrid.Children.Add(header);AddShelfControl(compliancePanel,header,close);EnableWindowInteraction(compliancePanel,header);
            var top=new Grid{Margin=new Thickness(0,10,0,10)};top.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});top.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var modelStack=new StackPanel();modelStack.Children.Add(new TextBlock{Text="审核模型（使用已配置的文本模型）",FontWeight=FontWeights.Bold});complianceModelBox=new ComboBox{Height=34,Margin=new Thickness(0,4,8,0),Padding=new Thickness(9,5,9,5),IsEditable=true,IsTextSearchEnabled=true,StaysOpenOnEdit=true,ItemsSource=ActiveTextModels()};complianceModelBox.Text=ActiveTextModel()??"";EnableModelSearch(complianceModelBox,delegate{return ActiveTextModels();});modelStack.Children.Add(complianceModelBox);top.Children.Add(modelStack);var settings=MakeButton("⚙ 模型设置",Ui.Neutral);settings.VerticalAlignment=VerticalAlignment.Bottom;settings.Click+=delegate{ShowImageAiSettings();};Grid.SetColumn(settings,1);top.Children.Add(settings);Grid.SetRow(top,1);rootGrid.Children.Add(top);
            var body=new Grid();body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
            var sourcePanel=new Grid{Margin=new Thickness(10,0,0,0)};sourcePanel.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});sourcePanel.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});sourcePanel.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});sourcePanel.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            complianceSource=new TextBlock{Text="输入或从中转袋带入待审核文本",Foreground=Ui.SubInk,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(2,0,2,6)};sourcePanel.Children.Add(complianceSource);
            compliancePreview=new Image{Height=108,Stretch=Stretch.Uniform,Visibility=Visibility.Collapsed,Margin=new Thickness(0,0,0,7)};Grid.SetRow(compliancePreview,1);sourcePanel.Children.Add(compliancePreview);
            complianceInput=new TextBox{AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new Thickness(10),VerticalContentAlignment=VerticalAlignment.Top,MinHeight=170,ToolTip="粘贴待审核文案；文字与图片可以分别或一起审核"};complianceInput.TextChanged+=delegate{if(complianceOriginalView!=null&&complianceOriginalView.Visibility==Visibility.Visible)ShowComplianceEditor();};Grid.SetRow(complianceInput,2);sourcePanel.Children.Add(complianceInput);
            complianceOriginalView=new RichTextBox{IsReadOnly=true,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new Thickness(10),Background=Ui.Inner,BorderBrush=Ui.Line,BorderThickness=new Thickness(1),Visibility=Visibility.Collapsed,ToolTip="橙色底色为命中内容；鼠标悬停查看原因和修改建议"};Grid.SetRow(complianceOriginalView,2);sourcePanel.Children.Add(complianceOriginalView);
            var sourceActions=new WrapPanel{Margin=new Thickness(0,8,0,0)};var import=MakeButton("带入中转所选",Ui.Neutral);import.Click+=delegate{OpenComplianceReview(SelectedStash());};var editOriginal=MakeButton("编辑原文",Ui.Neutral);editOriginal.Click+=delegate{ShowComplianceEditor();complianceInput.Focus();};var clearImage=MakeButton("移除图片",Brushes.Transparent);clearImage.Click+=delegate{SetComplianceImage(null);};sourceActions.Children.Add(import);sourceActions.Children.Add(editOriginal);sourceActions.Children.Add(clearImage);Grid.SetRow(sourceActions,3);sourcePanel.Children.Add(sourceActions);Grid.SetColumn(sourcePanel,1);body.Children.Add(sourcePanel);
            var resultPanel=new Grid();resultPanel.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});resultPanel.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});resultPanel.Children.Add(new TextBlock{Text="审核结论与修改建议",FontWeight=FontWeights.Bold,Margin=new Thickness(2,0,2,6)});complianceResult=new TextBox{IsReadOnly=true,IsReadOnlyCaretVisible=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new Thickness(10),VerticalContentAlignment=VerticalAlignment.Top,Background=Ui.Inner,BorderBrush=Ui.Line,BorderThickness=new Thickness(1),ToolTip="结果可直接选择并复制"};Grid.SetRow(complianceResult,1);resultPanel.Children.Add(complianceResult);body.Children.Add(resultPanel);Grid.SetRow(body,2);rootGrid.Children.Add(body);
            var bottom=new Grid{Margin=new Thickness(0,11,0,0)};bottom.ColumnDefinitions.Add(new ColumnDefinition());bottom.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});complianceStatus=new TextBlock{Text="可审核中转袋里的文字或图片；结果可选中复制",Foreground=Ui.SubInk,VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(2,0,8,0)};bottom.Children.Add(complianceStatus);var audit=MakeButton("开始审核",Ui.Accent);audit.Foreground=Brushes.White;audit.Click+=delegate{RunComplianceAudit();};Grid.SetColumn(audit,1);bottom.Children.Add(audit);Grid.SetRow(bottom,3);rootGrid.Children.Add(bottom);
            outer.Child=rootGrid;compliancePanel.Content=outer;compliancePanel.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(!exiting){e.Cancel=true;compliancePanel.Hide();}};
        }

        void SetComplianceImage(string path)
        {
            complianceImagePath=null;if(compliancePreview!=null){compliancePreview.Source=null;compliancePreview.Visibility=Visibility.Collapsed;}
            if(String.IsNullOrWhiteSpace(path)||!File.Exists(path)||!IsImageFile(path))return;
            try{var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelHeight=180;image.UriSource=new Uri(path);image.EndInit();image.Freeze();complianceImagePath=path;compliancePreview.Source=image;compliancePreview.Visibility=Visibility.Visible;}catch{}
        }

        void ShowComplianceEditor()
        {
            if(complianceOriginalView!=null)complianceOriginalView.Visibility=Visibility.Collapsed;
            if(complianceInput!=null)complianceInput.Visibility=Visibility.Visible;
        }

        List<ComplianceHit> ComplianceHitsFromResult(string result)
        {
            var hits=new List<ComplianceHit>();if(String.IsNullOrWhiteSpace(result))return hits;
            var matches=System.Text.RegularExpressions.Regex.Matches(result,@"【命中原文】\s*([^【\r\n]+)\s*【(?:规则类别|类别)】\s*([^【\r\n]+)\s*【(?:违规或风险原因|原因)】\s*([^【\r\n]+)\s*【(?:可执行修改建议|建议)】\s*([^\r\n]+)");
            foreach(System.Text.RegularExpressions.Match match in matches){string text=match.Groups[1].Value.Trim();if(String.IsNullOrWhiteSpace(text)||hits.Any(x=>x.Text==text))continue;hits.Add(new ComplianceHit{Text=text,Detail="类别："+match.Groups[2].Value.Trim()+"\n原因："+match.Groups[3].Value.Trim()+"\n建议："+match.Groups[4].Value.Trim()});}
            // 兼容模型常见的简写格式： 【收益率】【类别】敏感词【原因】…【建议】…
            foreach(System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(result,@"【([^】\r\n]+)】\s*【(?:规则类别|类别)】\s*([^【\r\n]+)\s*【(?:违规或风险原因|原因)】\s*([^【\r\n]+)\s*【(?:可执行修改建议|建议)】\s*([^\r\n]+)")){string text=match.Groups[1].Value.Trim();if(String.IsNullOrWhiteSpace(text)||hits.Any(x=>x.Text==text))continue;hits.Add(new ComplianceHit{Text=text,Detail="类别："+match.Groups[2].Value.Trim()+"\n原因："+match.Groups[3].Value.Trim()+"\n建议："+match.Groups[4].Value.Trim()});}
            // 兼容旧模型不完全遵循结构的返回：至少把「命中原文」高亮出来。
            if(hits.Count==0)foreach(System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(result,@"【命中原文】\s*([^【\r\n]+)")){string text=match.Groups[1].Value.Trim();if(!String.IsNullOrWhiteSpace(text)&&!hits.Any(x=>x.Text==text))hits.Add(new ComplianceHit{Text=text,Detail="该片段被模型标记为需复核。请查看左侧完整审核结论与修改建议。"});}
            return hits;
        }

        void RenderComplianceOriginal(string original,string result)
        {
            if(complianceOriginalView==null)return;var hits=ComplianceHitsFromResult(result);var document=new System.Windows.Documents.FlowDocument();document.PagePadding=new Thickness(0);var paragraph=new System.Windows.Documents.Paragraph{Margin=new Thickness(0)};int cursor=0;
            foreach(ComplianceHit hit in hits){int index=original.IndexOf(hit.Text,cursor,StringComparison.Ordinal);if(index<0)continue;if(index>cursor)paragraph.Inlines.Add(new System.Windows.Documents.Run(original.Substring(cursor,index-cursor)));var marked=new System.Windows.Documents.Run(hit.Text){Background=new SolidColorBrush(Color.FromRgb(255,226,184)),FontWeight=FontWeights.SemiBold};ToolTipService.SetToolTip(marked,hit.Detail);paragraph.Inlines.Add(marked);cursor=index+hit.Text.Length;}
            if(cursor<original.Length)paragraph.Inlines.Add(new System.Windows.Documents.Run(original.Substring(cursor)));if(String.IsNullOrEmpty(original))paragraph.Inlines.Add(new System.Windows.Documents.Run("本次由图片审核触发；图片中的文字请结合左侧结论人工查看。"));document.Blocks.Add(paragraph);complianceOriginalView.Document=document;complianceInput.Visibility=Visibility.Collapsed;complianceOriginalView.Visibility=Visibility.Visible;
        }

        void OpenComplianceReview(StashItem item=null)
        {
            if(compliancePanel==null)BuildCompliancePanel();if(RestoreShelvedIfNeeded(compliancePanel)){if(item==null)return;}
            if(complianceModelBox!=null){complianceModelBox.ItemsSource=null;complianceModelBox.ItemsSource=ActiveTextModels();complianceModelBox.Text=ActiveTextModel()??"";}
            if(item!=null){ShowComplianceEditor();if(item.Kind=="text"){complianceInput.Text=item.Value??"";SetComplianceImage(null);complianceSource.Text="已带入中转文字："+(item.Name??"未命名文字");}else if(item.Kind=="image"&&File.Exists(item.Value)){SetComplianceImage(item.Value);complianceSource.Text="已带入中转图片："+(item.Name??"未命名图片")+"。模型将审核图片内全部可见文字与宣传语。";}else{complianceStatus.Text="只支持审核中转袋中的文字或图片";}}
            if(!compliancePanel.IsVisible){var work=SystemParameters.WorkArea;compliancePanel.Left=Math.Max(work.Left+8,Math.Min(pet.Left-compliancePanel.Width-12,work.Right-compliancePanel.Width-8));compliancePanel.Top=Math.Max(work.Top+8,Math.Min(pet.Top,work.Bottom-compliancePanel.Height-8));compliancePanel.Show();}compliancePanel.Activate();if(item==null)complianceInput.Focus();
        }

        void RunComplianceAudit()
        {
            if(imageAiBusy){complianceStatus.Text="上一项 AI 请求仍在进行";return;}string text=(complianceInput.Text??"").Trim(),model=(complianceModelBox.Text??"").Trim(),key=LoadImageAiKey(true);if(String.IsNullOrWhiteSpace(model))model=ActiveTextModel()??"";if(String.IsNullOrWhiteSpace(text)&&String.IsNullOrWhiteSpace(complianceImagePath)){complianceStatus.Text="请先输入文案，或从中转袋带入图片";return;}if(String.IsNullOrWhiteSpace(ActiveTextBaseUrl())||String.IsNullOrWhiteSpace(model)||String.IsNullOrWhiteSpace(key)){complianceStatus.Text="请先配置文本模型的 Base URL、API Key 和模型";ShowImageAiSettings();return;}
            imageAiConfig.TextModel=model;if(!ActiveTextModels().Contains(model))ActiveTextModels().Add(model);SaveImageAiConfig();imageAiBusy=true;complianceStatus.Text=String.IsNullOrWhiteSpace(complianceImagePath)?"正在逐字审核文案…":"正在审核图片与文字…";var request=new Dictionary<string,object>{{"mode","audit"},{"provider",TextUsesToApis()?"toapis":"official"},{"base_url",ActiveTextBaseUrl()},{"model",model},{"instructions",ComplianceInstruction()},{"content",text},{"image_path",complianceImagePath??""}};
            RunImageHelper(request,key,null,delegate(Dictionary<string,object> response){imageAiBusy=false;if(!ImageResponseOk(response)){complianceStatus.Text=response!=null&&response.ContainsKey("error")?Convert.ToString(response["error"]):"审核请求失败";return;}string result=Convert.ToString(response["text"]);complianceResult.Text=result;complianceResult.Select(0,0);RenderComplianceOriginal(text,result);complianceStatus.Text="审核完成 · 右侧原文已标注命中项，悬停可查看原因与修改建议";});
        }
    }
}
