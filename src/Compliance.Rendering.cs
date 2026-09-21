using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MomoPetApp
{
    public partial class PetController
    {

        Brush ComplianceSeverityBrush(string severity){if(severity=="禁止")return new SolidColorBrush(Color.FromRgb(255,196,190));if(severity=="高")return new SolidColorBrush(Color.FromRgb(255,222,184));return new SolidColorBrush(Color.FromRgb(255,241,173));}

        string MarkerValue(string line,params string[] names)
        {
            foreach(string name in names){var match=Regex.Match(line,@"【"+Regex.Escape(name)+@"】\s*([^【\r\n]+)");if(match.Success)return match.Groups[1].Value.Trim();}return "";
        }

        ListBoxItem ComplianceFindingRow(ComplianceFindingData hit)
        {
            var panel=new Grid{Margin=new Thickness(4,2,4,2)};panel.ColumnDefinitions.Add(new ColumnDefinition());panel.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var copy=new StackPanel();copy.Children.Add(new TextBlock{Text=hit.Text,FontWeight=FontWeights.SemiBold,FontSize=13.5,TextTrimming=TextTrimming.CharacterEllipsis});copy.Children.Add(new TextBlock{Text=(hit.RuleId??"AI-REVIEW")+" · "+hit.Category,Foreground=Ui.SubInk,FontSize=10.5,Margin=new Thickness(0,3,0,0)});panel.Children.Add(copy);var state=Ui.PillBadge(hit.Disposition??"待确认",Ui.Ink,ComplianceSeverityBrush(hit.Severity),54);state.Margin=new Thickness(8,0,0,0);Grid.SetColumn(state,1);panel.Children.Add(state);var row=new ListBoxItem{Content=panel,Tag=hit,Padding=new Thickness(6),Background=Ui.Card,Margin=new Thickness(0,0,0,5)};row.ToolTip="严重程度："+hit.Severity+"\n原因："+hit.Reason+"\n建议："+hit.Suggestion;return row;
        }

        void RefreshComplianceFindings()
        {
            if(complianceFindingList==null)return;ComplianceFindingData selected=complianceFindingList.SelectedItem is ListBoxItem?(complianceFindingList.SelectedItem as ListBoxItem).Tag as ComplianceFindingData:null;complianceFindingList.Items.Clear();foreach(var hit in currentComplianceFindings)complianceFindingList.Items.Add(ComplianceFindingRow(hit));int forbidden=currentComplianceFindings.Count(x=>x.Severity=="禁止"&&x.Disposition!="误报"),risk=currentComplianceFindings.Count(x=>x.Severity!="禁止"&&x.Disposition!="误报"),covered=currentComplianceCoveredFindings.Count;complianceSummary.Text=currentComplianceFindings.Count==0?(covered>0?"未发现未覆盖风险 · 风险提示已覆盖 "+covered+" 处":"本地规则未发现问题 · 仍需模型及人工复核"):"待处理 "+currentComplianceFindings.Count+" 处 · 禁止 "+forbidden+" · 其他风险 "+risk+(covered>0?" · 已覆盖 "+covered:"");if(selected!=null)foreach(ListBoxItem row in complianceFindingList.Items)if(row.Tag==selected){row.IsSelected=true;break;}if(complianceFindingList.SelectedIndex<0&&complianceFindingList.Items.Count>0)complianceFindingList.SelectedIndex=0;
        }

        void ShowSelectedComplianceFinding()
        {
            var row=complianceFindingList==null?null:complianceFindingList.SelectedItem as ListBoxItem;var hit=row==null?null:row.Tag as ComplianceFindingData;if(complianceFindingDetail==null)return;if(hit==null){complianceFindingDetail.Text="选择命中项查看详情";return;}complianceFindingDetail.Text="规则编号："+hit.RuleId+"\n类别 / 严重程度："+hit.Category+" / "+hit.Severity+"\n人工处置："+(hit.Disposition??"待确认")+"\n\n规则原因："+hit.Reason+(String.IsNullOrWhiteSpace(hit.ModelComment)?"":"\n\n模型语境解释："+hit.ModelComment)+"\n\n修改建议："+hit.Suggestion+(String.IsNullOrWhiteSpace(hit.RequiredWarning)?"":"\n\n应补风险提示："+hit.RequiredWarning);FocusComplianceFinding(hit);
        }

        void SelectComplianceFinding(ComplianceFindingData hit)
        {
            if(hit==null||complianceFindingList==null)return;foreach(ListBoxItem item in complianceFindingList.Items)if(Object.ReferenceEquals(item.Tag,hit)){item.IsSelected=true;item.BringIntoView();break;}
        }

        void FocusComplianceFinding(ComplianceFindingData hit)
        {
            if(hit==null||complianceOriginalView==null||complianceOriginalView.Visibility!=Visibility.Visible)return;Run marked;if(!complianceMarkRuns.TryGetValue(hit.Id,out marked)||marked==null)return;try{complianceOriginalView.Selection.Select(marked.ContentStart,marked.ContentEnd);marked.BringIntoView();}catch{}
        }

        void SetComplianceDisposition(string disposition)
        {
            var row=complianceFindingList==null?null:complianceFindingList.SelectedItem as ListBoxItem;var hit=row==null?null:row.Tag as ComplianceFindingData;if(hit==null){complianceStatus.Text="请先选择一条命中项";return;}hit.Disposition=disposition;SyncCurrentComplianceAudit();RefreshComplianceFindings();RenderComplianceOriginal(complianceInput.Text??"");SaveComplianceAudits();complianceStatus.Text="已记录人工处置：“"+disposition+"”";
        }

        void RenderComplianceOriginal(string original)
        {
            if(complianceOriginalView==null)return;complianceMarkRuns.Clear();foreach(var hit in currentComplianceFindings.Where(x=>x.Start<0||x.Start+x.Length>original.Length)){int start,length;if(TryLocateCompliancePhrase(original,hit.Text,out start,out length)){hit.Start=start;hit.Length=length;hit.Text=original.Substring(start,length);}}var document=new FlowDocument{PagePadding=new Thickness(0)};var paragraph=new Paragraph{Margin=new Thickness(0)};var accepted=new List<ComplianceFindingData>();foreach(var hit in currentComplianceFindings.Where(x=>x.Disposition!="误报"&&x.Start>=0&&x.Start+x.Length<=original.Length).OrderBy(x=>x.Start).ThenByDescending(x=>x.Length)){if(accepted.Any(x=>hit.Start<x.Start+x.Length))continue;accepted.Add(hit);}int cursor=0;foreach(var hit in accepted){if(hit.Start>cursor)paragraph.Inlines.Add(new Run(original.Substring(cursor,hit.Start-cursor)));var marked=new Run(original.Substring(hit.Start,hit.Length)){Background=ComplianceSeverityBrush(hit.Severity),FontWeight=FontWeights.SemiBold,Cursor=Cursors.Hand};marked.Tag=hit;complianceMarkRuns[hit.Id]=marked;marked.MouseLeftButtonUp+=delegate{SelectComplianceFinding(hit);FocusComplianceFinding(hit);};ToolTipService.SetToolTip(marked,"规则："+hit.RuleId+"\n严重程度："+hit.Severity+"\n原因："+hit.Reason+"\n建议："+hit.Suggestion+"\n人工处置："+(hit.Disposition??"待确认"));paragraph.Inlines.Add(marked);cursor=hit.Start+hit.Length;}if(cursor<original.Length)paragraph.Inlines.Add(new Run(original.Substring(cursor)));if(String.IsNullOrEmpty(original))paragraph.Inlines.Add(new Run("本次由图片审核触发；图片文字请结合左侧命中项查看。"));document.Blocks.Add(paragraph);complianceOriginalView.Document=document;complianceInput.Visibility=Visibility.Collapsed;complianceOriginalView.Visibility=Visibility.Visible;
            if(complianceFindingList!=null)FocusComplianceFinding(complianceFindingList.SelectedItem is ListBoxItem?(complianceFindingList.SelectedItem as ListBoxItem).Tag as ComplianceFindingData:null);
        }
}
}
