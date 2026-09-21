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

        string Html(string value){return System.Net.WebUtility.HtmlEncode(value??"").Replace("\r\n","<br>").Replace("\n","<br>");}

        string HighlightedComplianceHtml(ComplianceAuditRecord audit)
        {
            string original=audit.OriginalText??"";var hits=(audit.Findings??new List<ComplianceFindingData>()).Where(x=>x.Start>=0&&x.Start+x.Length<=original.Length).OrderBy(x=>x.Start).ThenByDescending(x=>x.Length).ToList();var accepted=new List<ComplianceFindingData>();foreach(var hit in hits)if(!accepted.Any(x=>hit.Start<x.Start+x.Length))accepted.Add(hit);var builder=new StringBuilder();int cursor=0;foreach(var hit in accepted){if(hit.Start>cursor)builder.Append(Html(original.Substring(cursor,hit.Start-cursor)));builder.Append("<mark title=\"").Append(Html(hit.RuleId+" "+hit.Reason)).Append("\">").Append(Html(original.Substring(hit.Start,hit.Length))).Append("</mark>");cursor=hit.Start+hit.Length;}if(cursor<original.Length)builder.Append(Html(original.Substring(cursor)));return builder.ToString();
        }

        void ExportComplianceReport()
        {
            if(currentComplianceAudit==null){complianceStatus.Text="请先完成一次审核";return;}SyncCurrentComplianceAudit();var dialog=new Microsoft.Win32.SaveFileDialog{Title="导出合规审核报告",Filter="HTML 审核报告 (*.html)|*.html",DefaultExt=".html",FileName="合规审核报告-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".html"};if(dialog.ShowDialog()!=true)return;try{var a=currentComplianceAudit;var rows=new StringBuilder();foreach(var hit in a.Findings??new List<ComplianceFindingData>())rows.Append("<tr><td>").Append(Html(hit.RuleId)).Append("</td><td>").Append(Html(hit.Text)).Append("</td><td>").Append(Html(hit.Category+" / "+hit.Severity)).Append("</td><td>").Append(Html(hit.Reason)).Append("</td><td>").Append(Html(hit.Suggestion)).Append("</td><td>").Append(Html(hit.Disposition)).Append("</td></tr>");string report="<!doctype html><meta charset='utf-8'><title>合规审核报告</title><style>body{font-family:'Microsoft YaHei',sans-serif;max-width:1100px;margin:32px auto;color:#40352f}table{border-collapse:collapse;width:100%}td,th{border:1px solid #ddd;padding:8px;vertical-align:top}th{background:#fff0e8}mark{background:#ffdcb8}.original{white-space:normal;line-height:1.8;padding:16px;border:1px solid #ddd}</style><h1>博道咪合规审核报告</h1><p><b>结论：</b>"+Html(a.Conclusion)+"</p><p>审核时间："+Html(a.ReviewedAt)+"　审核人员："+Html(a.Reviewer)+"<br>来源："+Html(a.SourceName)+"　引用位置："+Html(a.SourceReference)+"<br>原文哈希："+Html(a.ContentHash)+"<br>词库版本："+Html(a.RulebookVersion)+"　模型："+Html(a.Provider+" / "+a.Model)+"　提示词版本："+Html(a.PromptVersion)+"</p><h2>命中项与人工处置</h2><table><tr><th>规则</th><th>命中原文</th><th>类别</th><th>原因</th><th>建议</th><th>人工处置</th></tr>"+rows+"</table><h2>带标注原文</h2><div class='original'>"+HighlightedComplianceHtml(a)+"</div><h2>模型复核原文</h2><div class='original'>"+Html(a.ModelResult)+"</div><p>本结果仅作内部初筛，最终以合规人员意见为准。</p>";File.WriteAllText(dialog.FileName,report,new UTF8Encoding(true));SaveComplianceAudits();complianceStatus.Text="审核报告已导出："+dialog.FileName;}catch(Exception ex){complianceStatus.Text="报告导出失败："+ex.Message;}
        }

        void ShowComplianceHistory()
        {
            if(complianceHistoryPanel==null)
            {
                complianceHistoryPanel=new Window
                {
                    Title="合规审核记录",Width=Math.Min(1080,SystemParameters.WorkArea.Width-48),Height=Math.Min(720,SystemParameters.WorkArea.Height-48),MinWidth=760,MinHeight=520,
                    WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.CanResize,ShowInTaskbar=false,
                    AllowsTransparency=true,Background=Brushes.Transparent,Owner=compliancePanel,
                    Topmost=compliancePanel!=null&&compliancePanel.Topmost
                };
                Ui.StyleWindow(complianceHistoryPanel);

                var outer=new Border{CornerRadius=new CornerRadius(18),Padding=new Thickness(22)};
                Ui.StyleCard(outer);
                var grid=new Grid();
                grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
                grid.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});

                var header=new Grid{Margin=new Thickness(0,0,0,14)};
                header.ColumnDefinitions.Add(new ColumnDefinition());
                header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
                var heading=new StackPanel();
                heading.Children.Add(Ui.Title("合规审核记录",20));
                heading.Children.Add(Ui.Subtitle("双击记录可恢复原文、命中规则和人工处置状态"));
                header.Children.Add(heading);
                var close=Ui.MakeCloseButton();
                close.Click+=delegate{complianceHistoryPanel.Hide();};
                Grid.SetColumn(close,1);header.Children.Add(close);
                grid.Children.Add(header);
                AddShelfControl(complianceHistoryPanel,header,close);
                EnableWindowInteraction(complianceHistoryPanel,header);

                complianceHistoryList=new ListBox
                {
                    Margin=new Thickness(0),Padding=new Thickness(6),
                    Background=Ui.Inner,BorderBrush=Ui.Line,BorderThickness=new Thickness(1)
                };
                complianceHistoryList.MouseDoubleClick+=delegate
                {
                    var row=complianceHistoryList.SelectedItem as ListBoxItem;
                    var audit=row==null?null:row.Tag as ComplianceAuditRecord;
                    if(audit==null)return;
                    currentComplianceAudit=audit;
                    currentComplianceFindings.Clear();
                    currentComplianceFindings.AddRange(audit.Findings??new List<ComplianceFindingData>());
                    currentComplianceCoveredFindings.Clear();
                    currentComplianceCoveredFindings.AddRange(audit.CoveredFindings??new List<ComplianceFindingData>());
                    complianceInput.Text=audit.OriginalText??"";
                    complianceResult.Text=audit.ModelResult??"";
                    complianceSourceName=audit.SourceName??"历史记录";
                    complianceSource.Text="历史审核："+complianceSourceName;
                    complianceReferenceBox.Text=audit.SourceReference??"";
                    complianceReviewerBox.Text=audit.Reviewer??"";
                    RefreshComplianceFindings();
                    RenderComplianceOriginal(complianceInput.Text);
                    complianceHistoryPanel.Hide();
                };
                Grid.SetRow(complianceHistoryList,1);grid.Children.Add(complianceHistoryList);
                outer.Child=grid;complianceHistoryPanel.Content=outer;
                complianceHistoryPanel.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e)
                {
                    if(!exiting){e.Cancel=true;complianceHistoryPanel.Hide();}
                };
            }
            complianceHistoryList.Items.Clear();
            foreach(var audit in complianceAudits)
            {
                DateTime time;DateTime.TryParse(audit.ReviewedAt,out time);
                complianceHistoryList.Items.Add(new ListBoxItem
                {
                    Content=time.ToString("yyyy-MM-dd HH:mm")+" · "+audit.Conclusion+" · "+audit.SourceName+" · 待处理 "+(audit.Findings==null?0:audit.Findings.Count)+" 项"+(audit.CoveredFindings!=null&&audit.CoveredFindings.Count>0?" · 已覆盖 "+audit.CoveredFindings.Count+" 项":""),
                    Tag=audit,Padding=new Thickness(12,10,12,10),Margin=new Thickness(0,0,0,4),
                    Background=Ui.Card,Foreground=Ui.Ink
                });
            }
            complianceHistoryPanel.Show();complianceHistoryPanel.Activate();
        }
}
}
