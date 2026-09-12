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
    public class ComplianceRuleData
    {
        public string Id { get; set; } public string Term { get; set; } public string Category { get; set; } public string Severity { get; set; }
        public string Reason { get; set; } public string Suggestion { get; set; } public string RequiredWarning { get; set; }
    }

    public class ComplianceFindingData
    {
        public string Id { get; set; } public string RuleId { get; set; } public string Text { get; set; } public int Start { get; set; } public int Length { get; set; }
        public string Category { get; set; } public string Severity { get; set; } public string Reason { get; set; } public string Suggestion { get; set; }
        public string RequiredWarning { get; set; } public string ModelComment { get; set; } public string Disposition { get; set; }
        public string CoverageNote { get; set; }
    }

    public class ComplianceAuditRecord
    {
        public string Id { get; set; } public string ContentHash { get; set; } public string RulebookVersion { get; set; } public string Model { get; set; }
        public string Provider { get; set; } public string PromptVersion { get; set; } public string ReviewedAt { get; set; } public string Reviewer { get; set; }
        public string SourceName { get; set; } public string SourceReference { get; set; } public string OriginalText { get; set; } public string ModelResult { get; set; }
        public string Conclusion { get; set; } public List<ComplianceFindingData> Findings { get; set; } public List<ComplianceFindingData> CoveredFindings { get; set; }
    }

    public class ComplianceModelReview
    {
        public string conclusion { get; set; } public string summary { get; set; } public List<ComplianceModelReviewItem> items { get; set; }
    }

    public class ComplianceModelReviewItem
    {
        public string hit_id { get; set; } public string excerpt { get; set; } public string rule_id { get; set; } public string decision { get; set; }
        public string category { get; set; } public string severity { get; set; } public string reason { get; set; } public string suggestion { get; set; } public double confidence { get; set; }
    }

    public partial class PetController
    {
        const string CompliancePromptVersion="2026-09-10-v4-structured-review";
        Window compliancePanel,complianceHistoryPanel;
        TextBox complianceInput,complianceResult,complianceReviewerBox,complianceReferenceBox;
        RichTextBox complianceOriginalView;
        TextBlock complianceStatus,complianceSource,complianceSummary,complianceFindingDetail;
        Image compliancePreview;
        ComboBox complianceModelBox;
        ListBox complianceFindingList,complianceHistoryList;
        Button complianceLocalTab,complianceModelTab;
        Grid complianceLocalPanel,complianceModelPanel;
        readonly Dictionary<string,Run> complianceMarkRuns=new Dictionary<string,Run>();
        string complianceImagePath,complianceAuditFile,complianceRulebookVersion="未知版本",complianceSourceName="手动输入";
        readonly List<ComplianceRuleData> complianceRuleDefinitions=new List<ComplianceRuleData>();
        readonly List<ComplianceFindingData> currentComplianceFindings=new List<ComplianceFindingData>();
        readonly List<ComplianceFindingData> currentComplianceCoveredFindings=new List<ComplianceFindingData>();
        readonly List<ComplianceAuditRecord> complianceAudits=new List<ComplianceAuditRecord>();
        ComplianceAuditRecord currentComplianceAudit;

        void InitializeCompliancePaths(){complianceAuditFile=Path.Combine(dataDir,"compliance-audits.json");LoadComplianceRuleDefinitions();}

        string ComplianceRules()
        {
            try{string path=EmbeddedRuntime.ResolveFile("compliance-rules.txt",root);if(File.Exists(path))return File.ReadAllText(path,Encoding.UTF8);}catch{}
            return "禁用词：保证、承诺、确保、高收益、无风险、保本、必涨、稳赚不赔。敏感词：预测、未来收益、推荐、排名、最、定投、收益。";
        }

        void LoadComplianceRuleDefinitions()
        {
            complianceRuleDefinitions.Clear();string content=ComplianceRules();var version=Regex.Match(content,@"版本\s*[：:]\s*([^\r\n]+)");if(version.Success)complianceRulebookVersion=version.Groups[1].Value.Trim();int section=0;
            foreach(string raw in content.Replace("\r","").Split('\n')){string line=raw.Trim();if(line.StartsWith("一、禁用词"))section=1;else if(line.StartsWith("二、敏感词"))section=2;else if(line.StartsWith("三、敏感词"))section=3;var match=Regex.Match(line,@"^(\d+)\.\s*(.+)$");if(section==0||!match.Success)continue;int number;Int32.TryParse(match.Groups[1].Value,out number);string body=match.Groups[2].Value.Trim().TrimEnd('。'),termsPart=body,reason="";int colon=body.IndexOf('：');if(colon>=0){termsPart=body.Substring(0,colon);reason=body.Substring(colon+1).Trim();}if(section==1){termsPart=Regex.Replace(termsPart,@"等(?:侮辱性词语|不当表述)$","");if(String.IsNullOrWhiteSpace(reason))reason="禁用词出现时不得使用该素材。";}else if(section==2&&String.IsNullOrWhiteSpace(reason))reason="敏感表述，必须核实语境、证据和合规依据。";else if(section==3&&String.IsNullOrWhiteSpace(reason))reason="出现时必须补充对应风险提示。";string prefix=section==1?"BD-F-":section==2?"BD-S-":"BD-W-",category=section==1?"禁用词":section==2?"敏感词":"风险提示词",severity=section==1?"禁止":section==2?"高":"提示";
                foreach(string termRaw in Regex.Split(termsPart,@"[、,，]")){string term=termRaw.Trim().Trim('“','”','"','。');if(String.IsNullOrWhiteSpace(term))continue;complianceRuleDefinitions.Add(new ComplianceRuleData{Id=prefix+number.ToString("00"),Term=term,Category=category,Severity=severity,Reason=reason,Suggestion=section==1?"删除，或改为客观、中性且可核验的表述。":section==2?"补充客观依据、数据来源和适用边界，并交合规人员复核。":"保留该表述时补充规定的完整风险提示。",RequiredWarning=section==3?reason:""});}
            }
        }

        void LoadComplianceAudits(){try{if(File.Exists(complianceAuditFile)){var loaded=json.Deserialize<List<ComplianceAuditRecord>>(File.ReadAllText(complianceAuditFile,Encoding.UTF8));if(loaded!=null)complianceAudits.AddRange(loaded.Where(x=>x!=null));}}catch{try{File.Copy(complianceAuditFile,complianceAuditFile+".bak",true);}catch{}}}
        void SaveComplianceAudits(){try{Directory.CreateDirectory(dataDir);string temp=complianceAuditFile+".tmp";File.WriteAllText(temp,json.Serialize(complianceAudits),new UTF8Encoding(false));File.Copy(temp,complianceAuditFile,true);File.Delete(temp);}catch(Exception ex){if(complianceStatus!=null)complianceStatus.Text="审核记录保存失败："+ex.Message;}}

        bool IsCompliancePunctuation(char value){return Char.IsWhiteSpace(value)||"，。；：、！？,.…;:!?（）()【】[]“”\"'《》<>—-·/\\".IndexOf(value)>=0;}

        string NormalizeComplianceText(string value)
        {
            if(String.IsNullOrWhiteSpace(value))return "";var result=new StringBuilder();foreach(char c in value)if(!IsCompliancePunctuation(c))result.Append(Char.ToLowerInvariant(c));return result.ToString();
        }

        bool TryLocateCompliancePhrase(string original,string phrase,out int start,out int length)
        {
            start=-1;length=0;if(String.IsNullOrWhiteSpace(original)||String.IsNullOrWhiteSpace(phrase))return false;int exact=original.IndexOf(phrase,StringComparison.Ordinal);if(exact>=0){start=exact;length=phrase.Length;return true;}
            var normalizedOriginal=new StringBuilder();var map=new List<int>();for(int i=0;i<original.Length;i++)if(!IsCompliancePunctuation(original[i])){normalizedOriginal.Append(Char.ToLowerInvariant(original[i]));map.Add(i);}string needle=NormalizeComplianceText(phrase);if(needle.Length>=2){int normalizedIndex=normalizedOriginal.ToString().IndexOf(needle,StringComparison.Ordinal);if(normalizedIndex>=0){start=map[normalizedIndex];int end=map[normalizedIndex+needle.Length-1];length=end-start+1;return true;}}
            foreach(string fragment in Regex.Split(phrase,@"[，。；：、！？,.…;:!?\r\n]+" ).Select(x=>x.Trim()).Where(x=>NormalizeComplianceText(x).Length>=4).OrderByDescending(x=>x.Length)){exact=original.IndexOf(fragment,StringComparison.Ordinal);if(exact>=0){start=exact;length=fragment.Length;return true;}}
            return false;
        }

        bool IsNegatedOrExplanatoryUse(string text,int index,string term)
        {
            int begin=Math.Max(0,index-32),count=index-begin;string before=count>0?text.Substring(begin,count):"";if(Regex.IsMatch(before,"(不得|不能|不可|禁止|避免|切勿|不要|不应|严禁|并非|不是|不代表|不构成|无法|不)\\s*[‘’“”\\\"']?$"))return true;if(Regex.IsMatch(before,"(不得|禁止|避免|不要|切勿|严禁)[^，。；！？,.!?\\r\\n]{0,8}[‘’“”\\\"']?$"))return true;if((term=="保证"||term=="保障"||term=="承诺")&&Regex.IsMatch(before,"(并不构成|不构成|不代表|不作|不对此作|不提供|不能|无法)[^，。；！？,.!?\\r\\n]{0,24}$"))return true;
            if(term=="最"&&index+1<text.Length){string tail=text.Substring(index,Math.Min(4,text.Length-index));if(Regex.IsMatch(tail,@"^(最近|最后|最终|最初|最多|最少|最早|最晚)"))return true;}
            if(term=="优"&&index+1<text.Length){string tail=text.Substring(index,Math.Min(3,text.Length-index));if(Regex.IsMatch(tail,@"^(优化|优先)"))return true;}
            return false;
        }

        string[][] DisclosureAnchorGroups(string ruleId)
        {
            switch(ruleId){
                case "BD-W-01":return new[]{new[]{"不能赎回","不能卖出","不可赎回"},new[]{"流动性约束","流动性风险"}};
                case "BD-W-02":return new[]{new[]{"违约风险"},new[]{"信用风险"},new[]{"利率风险"},new[]{"净值波动","本金亏损"}};
                case "BD-W-03":return new[]{new[]{"科创板"},new[]{"流动性风险"},new[]{"退市风险"},new[]{"投资集中度风险"}};
                case "BD-W-04":return new[]{new[]{"港股通","境外证券"},new[]{"汇率风险"},new[]{"特别投资风险","境外证券市场"}};
                case "BD-W-05":return new[]{new[]{"Y类"},new[]{"不代表收益保障","不保本"},new[]{"可能发生亏损","本金亏损"},new[]{"不可领取","移除个人养老金可投名录"}};
                case "BD-W-06":return new[]{new[]{"不能规避","无法规避"},new[]{"不是替代储蓄","非储蓄"}};
                case "BD-W-07":return new[]{new[]{"过往业绩并不预示其未来表现","历史业绩不代表未来表现"},new[]{"不构成基金业绩表现的保证","不构成未来业绩保证"}};
                case "BD-W-08":return new[]{new[]{"不开展任何投资管理业务"},new[]{"业务隔离"}};
                case "BD-W-09":return new[]{new[]{"中证指数有限公司"},new[]{"不对此作任何保证","不作任何保证"},new[]{"指数的任何错误","指数错误"}};
            }return new string[0][];
        }

        bool HasRequiredDisclosure(string text,ComplianceRuleData rule,out string note)
        {
            note="";if(rule==null||String.IsNullOrWhiteSpace(rule.RequiredWarning))return false;string normalized=NormalizeComplianceText(text),required=NormalizeComplianceText(rule.RequiredWarning);if(required.Length>0&&normalized.Contains(required)){note="已包含词库规定的完整风险提示";return true;}var groups=DisclosureAnchorGroups(rule.Id);if(groups.Length==0)return false;int covered=groups.Count(group=>group.Any(anchor=>normalized.Contains(NormalizeComplianceText(anchor))));if(covered==groups.Length){note="已包含对应风险提示的关键要素（"+covered+"/"+groups.Length+"）";return true;}return false;
        }

        List<ComplianceFindingData> ScanComplianceRules(string text)
        {
            var hits=new List<ComplianceFindingData>();if(String.IsNullOrEmpty(text))return hits;foreach(ComplianceRuleData rule in complianceRuleDefinitions.OrderByDescending(x=>x.Term.Length)){string coverageNote="";bool covered=rule.Category=="风险提示词"&&HasRequiredDisclosure(text,rule,out coverageNote);int cursor=0;while(cursor<text.Length){int index=text.IndexOf(rule.Term,cursor,StringComparison.Ordinal);if(index<0)break;cursor=index+Math.Max(1,rule.Term.Length);if(IsNegatedOrExplanatoryUse(text,index,rule.Term))continue;var finding=new ComplianceFindingData{Id=Guid.NewGuid().ToString("N"),RuleId=rule.Id,Text=rule.Term,Start=index,Length=rule.Term.Length,Category=rule.Category,Severity=rule.Severity,Reason=rule.Reason,Suggestion=rule.Suggestion,RequiredWarning=rule.RequiredWarning,Disposition="待确认",CoverageNote=covered?coverageNote:""};if(covered){finding.Disposition="风险提示已覆盖";currentComplianceCoveredFindings.Add(finding);continue;}if(!hits.Any(x=>x.Start==index&&x.Length==rule.Term.Length&&x.RuleId==rule.Id))hits.Add(finding);}}
            return hits.OrderBy(x=>x.Start).ThenByDescending(x=>x.Length).ToList();
        }

        string ComplianceInstruction(IEnumerable<ComplianceFindingData> localHits)
        {
            string source=complianceInput==null?"":complianceInput.Text??"";string local=String.Join("\n",localHits.Select(x=>"hit_id="+x.Id+" | rule_id="+x.RuleId+" | 类别="+x.Category+" | 严重程度="+x.Severity+" | 命中原文=“"+x.Text+"” | 上下文=“"+ComplianceContext(source,x.Start,x.Length)+"” | 规则原因="+x.Reason).ToArray());if(String.IsNullOrWhiteSpace(local))local="无。本轮仍要检查确有依据的遗漏风险，但不要为了凑数新增问题。";string covered=String.Join("\n",currentComplianceCoveredFindings.Select(x=>x.RuleId+" | “"+x.Text+"” | "+x.CoverageNote).Distinct().ToArray());if(String.IsNullOrWhiteSpace(covered))covered="无";
            return "你是公募基金营销素材的内部合规复核助手。任务不是关键词复述，而是对本地初筛逐项做语境复核，并仅补充证据明确的遗漏。\n\n判定原则：\n- keep：表述在当前语境下确有未解决风险。\n- dismiss：只是法规讲解、否定表述、客观名称、引用、普通叙述或其他明显误报。\n- covered：仅限风险提示型规则，且素材中已经出现主题匹配、要素完整的专项风险揭示。一般性‘投资有风险’不能覆盖专项风险。\n- 禁用词、收益承诺、虚假或误导性表述不能被一般免责声明覆盖。\n- 不得因关键词本身存在就判违规，必须阅读前后文；也不得为了显得严格而凑数。\n\n规则版本："+complianceRulebookVersion+"\n\n必须逐项复核的本地命中（每个 hit_id 恰好返回一次）：\n"+local+"\n\n本地已确认由专项风险提示覆盖（不要重复输出）：\n"+covered+"\n\n完整规则：\n"+ComplianceRules()+"\n\n只返回一个 JSON 对象，不要 Markdown、不要代码围栏、不要额外解释。结构必须是：\n{\"conclusion\":\"通过|有风险|不可使用\",\"summary\":\"一句话总体说明\",\"items\":[{\"hit_id\":\"本地编号；新增问题留空\",\"excerpt\":\"逐字复制自素材的最短完整原文\",\"rule_id\":\"规则编号或AI-REVIEW\",\"decision\":\"keep|dismiss|covered\",\"category\":\"禁用词|敏感词|风险提示词|模型补充\",\"severity\":\"禁止|高|提示\",\"reason\":\"结合当前语境的具体原因\",\"suggestion\":\"可直接执行的修改建议\",\"confidence\":0.00}]}\n新增问题仅在 confidence>=0.75 且 excerpt 能逐字定位到素材时输出；没有新增问题不要虚构。";
        }

        string ComplianceContext(string source,int start,int length)
        {
            if(String.IsNullOrEmpty(source)||start<0||start>=source.Length)return "";int begin=Math.Max(0,start-70),end=Math.Min(source.Length,start+Math.Max(1,length)+70);return source.Substring(begin,end-begin).Replace("\r"," ").Replace("\n"," ");
        }

        Brush ComplianceSeverityBrush(string severity){if(severity=="禁止")return new SolidColorBrush(Color.FromRgb(255,196,190));if(severity=="高")return new SolidColorBrush(Color.FromRgb(255,222,184));return new SolidColorBrush(Color.FromRgb(255,241,173));}

        void BuildCompliancePanel()
        {
            compliancePanel=new Window{Title="博道咪合规审核",Width=Math.Max(900,SystemParameters.WorkArea.Width-28),Height=Math.Max(650,SystemParameters.WorkArea.Height-28),MinWidth=900,MinHeight=650,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.CanResize,ShowInTaskbar=false,AllowsTransparency=true,Background=Brushes.Transparent,Topmost=pet.Topmost};Ui.StyleWindow(compliancePanel);var outer=new Border{CornerRadius=new CornerRadius(18),Padding=new Thickness(18)};Ui.StyleCard(outer);var rootGrid=new Grid();rootGrid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});rootGrid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});rootGrid.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});rootGrid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var header=new Grid{Margin=new Thickness(2,0,0,16)};header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var heading=new StackPanel();heading.Children.Add(Ui.Title("合规审核",22));heading.Children.Add(Ui.Subtitle("规则初筛  ·  模型语境复核  ·  人工确认  ·  审计留痕"));header.Children.Add(heading);var close=Ui.MakeCloseButton();close.Click+=delegate{compliancePanel.Hide();};Grid.SetColumn(close,1);header.Children.Add(close);rootGrid.Children.Add(header);AddShelfControl(compliancePanel,header,close);EnableWindowInteraction(compliancePanel,header);
            var top=new Grid{Margin=new Thickness(0,10,0,10)};top.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(2,GridUnitType.Star)});top.ColumnDefinitions.Add(new ColumnDefinition());top.ColumnDefinitions.Add(new ColumnDefinition());top.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var modelStack=new StackPanel();modelStack.Children.Add(new TextBlock{Text="审核模型",FontWeight=FontWeights.Bold});complianceModelBox=new ComboBox{Height=36,Margin=new Thickness(0,4,8,0),Padding=new Thickness(9,5,9,5),IsEditable=true,IsTextSearchEnabled=true,StaysOpenOnEdit=true,ItemsSource=ActiveTextModels()};complianceModelBox.Text=ActiveTextModel()??"";EnableModelSearch(complianceModelBox,delegate{return ActiveTextModels();});modelStack.Children.Add(complianceModelBox);top.Children.Add(modelStack);var reviewerStack=new StackPanel();reviewerStack.Children.Add(new TextBlock{Text="审核人员",FontWeight=FontWeights.Bold});complianceReviewerBox=new TextBox{Height=36,Margin=new Thickness(0,4,8,0),Padding=new Thickness(9,5,9,5),ToolTip="填写人工复核人员姓名或工号"};reviewerStack.Children.Add(complianceReviewerBox);Grid.SetColumn(reviewerStack,1);top.Children.Add(reviewerStack);var referenceStack=new StackPanel();referenceStack.Children.Add(new TextBlock{Text="引用位置",FontWeight=FontWeights.Bold});complianceReferenceBox=new TextBox{Height=36,Margin=new Thickness(0,4,8,0),Padding=new Thickness(9,5,9,5),ToolTip="页码、幻灯片编号、工作表或图片编号"};referenceStack.Children.Add(complianceReferenceBox);Grid.SetColumn(referenceStack,2);top.Children.Add(referenceStack);var settings=MakeButton("⚙ 模型设置",Ui.Neutral);settings.VerticalAlignment=VerticalAlignment.Bottom;settings.Click+=delegate{ShowImageAiSettings();};Grid.SetColumn(settings,3);top.Children.Add(settings);Grid.SetRow(top,1);rootGrid.Children.Add(top);
            var body=new Grid();body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(0.95,GridUnitType.Star)});body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1.35,GridUnitType.Star)});
            var resultPanel=new Grid{Margin=new Thickness(0,0,10,0)};resultPanel.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});resultPanel.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
            var complianceTabs=new Grid();complianceTabs.ColumnDefinitions.Add(new ColumnDefinition());complianceTabs.ColumnDefinitions.Add(new ColumnDefinition());
            complianceLocalTab=MakeButton("本地审核",Ui.AccentSoft);complianceLocalTab.Height=36;complianceLocalTab.Padding=new Thickness(4,5,4,5);complianceLocalTab.Margin=new Thickness(1);complianceLocalTab.FontSize=12.5;complianceLocalTab.ToolTip="词库扫描与模型合并后的命中清单，在此逐条人工处置";complianceLocalTab.Click+=delegate{ShowComplianceReviewTab(false);};complianceTabs.Children.Add(complianceLocalTab);
            complianceModelTab=MakeButton("AI 审核",Ui.Neutral);complianceModelTab.Height=36;complianceModelTab.Padding=new Thickness(4,5,4,5);complianceModelTab.Margin=new Thickness(1);complianceModelTab.FontSize=12.5;complianceModelTab.ToolTip="模型结合语境的复核结论原文，可选中复制";complianceModelTab.Click+=delegate{ShowComplianceReviewTab(true);};Grid.SetColumn(complianceModelTab,1);complianceTabs.Children.Add(complianceModelTab);
            resultPanel.Children.Add(new Border{Background=Ui.Inner,BorderBrush=Ui.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(11),Padding=new Thickness(3),Margin=new Thickness(0,0,0,10),Child=complianceTabs});
            complianceLocalPanel=new Grid();complianceLocalPanel.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});complianceLocalPanel.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});complianceLocalPanel.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});complianceLocalPanel.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});Grid.SetRow(complianceLocalPanel,1);resultPanel.Children.Add(complianceLocalPanel);
            complianceSummary=new TextBlock{Text="尚未审核",FontWeight=FontWeights.Bold,Margin=new Thickness(2,0,2,8),TextWrapping=TextWrapping.Wrap};complianceLocalPanel.Children.Add(complianceSummary);complianceFindingList=new ListBox{BorderBrush=Ui.Line,BorderThickness=new Thickness(1),Background=Ui.Inner,SelectionMode=SelectionMode.Single,MinHeight=180};complianceFindingList.SelectionChanged+=delegate{ShowSelectedComplianceFinding();};Grid.SetRow(complianceFindingList,1);complianceLocalPanel.Children.Add(complianceFindingList);var disposition=new WrapPanel{Margin=new Thickness(0,8,0,8)};var confirm=MakeButton("确认问题",new SolidColorBrush(Color.FromRgb(255,222,184)));confirm.Click+=delegate{SetComplianceDisposition("确认问题");};var falsePositive=MakeButton("标为误报",new SolidColorBrush(Color.FromRgb(231,243,236)));falsePositive.Click+=delegate{SetComplianceDisposition("误报");};var accepted=MakeButton("采纳建议",Ui.Neutral);accepted.Click+=delegate{SetComplianceDisposition("已采纳建议");};disposition.Children.Add(confirm);disposition.Children.Add(falsePositive);disposition.Children.Add(accepted);Grid.SetRow(disposition,2);complianceLocalPanel.Children.Add(disposition);var detailBorder=new Border{Background=Ui.Inner,BorderBrush=Ui.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(8),Padding=new Thickness(10),MaxHeight=190};complianceFindingDetail=new TextBlock{Text="选择命中项查看规则编号、原因和修改建议",TextWrapping=TextWrapping.Wrap,Foreground=Ui.SubInk};detailBorder.Child=new ScrollViewer{Content=complianceFindingDetail,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetRow(detailBorder,3);complianceLocalPanel.Children.Add(detailBorder);
            complianceModelPanel=new Grid{Visibility=Visibility.Collapsed};complianceModelPanel.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});complianceModelPanel.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});Grid.SetRow(complianceModelPanel,1);resultPanel.Children.Add(complianceModelPanel);complianceModelPanel.Children.Add(new TextBlock{Text="模型结合语境的复核结论（可选中复制）",Foreground=Ui.SubInk,FontSize=11.5,Margin=new Thickness(2,0,2,6)});
            complianceResult=new TextBox{IsReadOnly=true,IsReadOnlyCaretVisible=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new Thickness(8),Background=Ui.Inner,BorderBrush=Ui.Line,BorderThickness=new Thickness(1)};complianceResult.Text="尚未开始模型复核；点击下方“开始审核”后，这里会显示完整复核原文。";Grid.SetRow(complianceResult,1);complianceModelPanel.Children.Add(complianceResult);body.Children.Add(resultPanel);
            var sourcePanel=new Grid();sourcePanel.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});sourcePanel.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});sourcePanel.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});sourcePanel.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});complianceSource=new TextBlock{Text="输入或从中转袋带入待审核文本/文件",Foreground=Ui.SubInk,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(2,0,2,6)};sourcePanel.Children.Add(complianceSource);compliancePreview=new Image{Height=108,Stretch=System.Windows.Media.Stretch.Uniform,Visibility=Visibility.Collapsed,Margin=new Thickness(0,0,0,7)};Grid.SetRow(compliancePreview,1);sourcePanel.Children.Add(compliancePreview);complianceInput=new TextBox{AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new Thickness(10),VerticalContentAlignment=VerticalAlignment.Top,MinHeight=170,ToolTip="支持粘贴文本，或从中转袋带入 Word/PPT/Excel/文本/图片"};complianceInput.TextChanged+=delegate{RunLocalComplianceScan();};Grid.SetRow(complianceInput,2);sourcePanel.Children.Add(complianceInput);complianceOriginalView=new RichTextBox{IsReadOnly=true,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new Thickness(10),Background=Ui.Inner,BorderBrush=Ui.Line,BorderThickness=new Thickness(1),Visibility=Visibility.Collapsed,ToolTip="红/橙/黄色底色为命中内容；点击高亮项可回到左侧问题，悬停查看详情"};Grid.SetRow(complianceOriginalView,2);sourcePanel.Children.Add(complianceOriginalView);var sourceActions=new WrapPanel{Margin=new Thickness(0,8,0,0)};var import=MakeButton("带入中转所选",Ui.Neutral);import.Click+=delegate{OpenComplianceReview(SelectedStash());};var editOriginal=MakeButton("编辑原文",Ui.Neutral);editOriginal.Click+=delegate{ShowComplianceEditor();complianceInput.Focus();};var clearImage=MakeButton("移除图片",Brushes.Transparent);clearImage.Click+=delegate{SetComplianceImage(null);};sourceActions.Children.Add(import);sourceActions.Children.Add(editOriginal);sourceActions.Children.Add(clearImage);Grid.SetRow(sourceActions,3);sourcePanel.Children.Add(sourceActions);Grid.SetColumn(sourcePanel,1);body.Children.Add(sourcePanel);Grid.SetRow(body,2);rootGrid.Children.Add(body);
            var bottom=new Grid{Margin=new Thickness(0,11,0,0)};bottom.ColumnDefinitions.Add(new ColumnDefinition());bottom.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});bottom.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});bottom.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});complianceStatus=new TextBlock{Text="本地规则会先扫描；模型只负责结合语境解释和补充",Foreground=Ui.SubInk,VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(2,0,8,0)};bottom.Children.Add(complianceStatus);var history=MakeButton("审核记录",Ui.Neutral);history.Click+=delegate{ShowComplianceHistory();};Grid.SetColumn(history,1);bottom.Children.Add(history);var export=MakeButton("导出报告",Ui.Neutral);export.Click+=delegate{ExportComplianceReport();};Grid.SetColumn(export,2);bottom.Children.Add(export);var audit=MakeButton("开始审核",Ui.Accent);audit.Foreground=Brushes.White;audit.Click+=delegate{RunComplianceAudit();};Grid.SetColumn(audit,3);bottom.Children.Add(audit);Grid.SetRow(bottom,3);rootGrid.Children.Add(bottom);PolishComplianceLayout(rootGrid,top,body,resultPanel,sourcePanel,bottom);outer.Child=rootGrid;compliancePanel.Content=outer;compliancePanel.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(!exiting){e.Cancel=true;compliancePanel.Hide();}};
        }

        void PolishComplianceLayout(Grid rootGrid,Grid top,Grid body,Grid resultPanel,Grid sourcePanel,Grid bottom)
        {
            top.Margin=new Thickness(0);rootGrid.Children.Remove(top);var topCard=Ui.SurfacePanel(top,new Thickness(14,12,14,13),new Thickness(0,0,0,14));Grid.SetRow(topCard,1);rootGrid.Children.Add(topCard);
            body.Children.Remove(resultPanel);body.Children.Remove(sourcePanel);body.ColumnDefinitions.Clear();body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(.68,GridUnitType.Star),MinWidth=360});body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(10)});body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1.72,GridUnitType.Star),MinWidth=560});resultPanel.Margin=new Thickness(0);sourcePanel.Margin=new Thickness(0);var resultCard=Ui.SurfacePanel(resultPanel,new Thickness(14),new Thickness(0));var sourceCard=Ui.SurfacePanel(sourcePanel,new Thickness(14),new Thickness(0));Grid.SetColumn(sourceCard,2);var splitter=new GridSplitter{Width=3,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Stretch,Background=Ui.Line,ResizeBehavior=GridResizeBehavior.PreviousAndNext,ResizeDirection=GridResizeDirection.Columns,ToolTip="左右拖动，调整问题列表与原文区域宽度"};Grid.SetColumn(splitter,1);body.Children.Add(resultCard);body.Children.Add(splitter);body.Children.Add(sourceCard);
            complianceSummary.FontSize=13.5;complianceFindingList.Background=Ui.Card;complianceFindingList.BorderThickness=new Thickness(0);complianceFindingDetail.FontSize=12.5;complianceFindingDetail.LineHeight=20;complianceResult.Background=Ui.Inner;complianceOriginalView.Background=Ui.Card;complianceOriginalView.FontSize=14;complianceOriginalView.Document.FontFamily=new FontFamily("Microsoft YaHei UI");complianceOriginalView.Document.FontSize=14;complianceOriginalView.Document.LineHeight=24;
            bottom.Margin=new Thickness(2,14,0,0);
        }

        void SetComplianceImage(string path)
        {
            complianceImagePath=null;if(compliancePreview!=null){compliancePreview.Source=null;compliancePreview.Visibility=Visibility.Collapsed;}if(String.IsNullOrWhiteSpace(path)||!File.Exists(path)||!IsImageFile(path))return;try{var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelHeight=180;image.UriSource=new Uri(path);image.EndInit();image.Freeze();complianceImagePath=path;compliancePreview.Source=image;compliancePreview.Visibility=Visibility.Visible;}catch{}
        }

        void ShowComplianceEditor(){if(complianceOriginalView!=null)complianceOriginalView.Visibility=Visibility.Collapsed;if(complianceInput!=null)complianceInput.Visibility=Visibility.Visible;}

        void ShowComplianceReviewTab(bool model)
        {
            if(complianceLocalPanel==null||complianceModelPanel==null)return;
            complianceLocalPanel.Visibility=model?Visibility.Collapsed:Visibility.Visible;
            complianceModelPanel.Visibility=model?Visibility.Visible:Visibility.Collapsed;
            if(complianceLocalTab!=null)complianceLocalTab.Background=model?Ui.Neutral:Ui.AccentSoft;
            if(complianceModelTab!=null)complianceModelTab.Background=model?Ui.AccentSoft:Ui.Neutral;
        }

        void RunLocalComplianceScan()
        {
            if(complianceInput==null||complianceFindingList==null)return;currentComplianceFindings.Clear();currentComplianceCoveredFindings.Clear();currentComplianceFindings.AddRange(ScanComplianceRules(complianceInput.Text??""));RefreshComplianceFindings();
            if(complianceOriginalView!=null&&complianceOriginalView.Visibility==Visibility.Visible)RenderComplianceOriginal(complianceInput.Text??"");
        }

        string MarkerValue(string line,params string[] names)
        {
            foreach(string name in names){var match=Regex.Match(line,@"【"+Regex.Escape(name)+@"】\s*([^【\r\n]+)");if(match.Success)return match.Groups[1].Value.Trim();}return "";
        }

        bool ApplyStructuredComplianceReview(string original,string raw,out string display)
        {
            display=raw??"";string cleaned=(raw??"").Trim();int begin=cleaned.IndexOf('{'),end=cleaned.LastIndexOf('}');if(begin<0||end<=begin)return false;ComplianceModelReview review;try{review=json.Deserialize<ComplianceModelReview>(cleaned.Substring(begin,end-begin+1));}catch{return false;}if(review==null||review.items==null)return false;
            var lines=new List<string>();lines.Add("审核结论："+(String.IsNullOrWhiteSpace(review.conclusion)?"需人工复核":review.conclusion));if(!String.IsNullOrWhiteSpace(review.summary))lines.Add(review.summary);int kept=0,dismissed=0,covered=0,added=0;
            foreach(var item in review.items.Where(x=>x!=null))
            {
                string decision=(item.decision??"keep").Trim().ToLowerInvariant();if(decision=="误报"||decision=="排除")decision="dismiss";else if(decision=="已覆盖"||decision=="风险提示已覆盖")decision="covered";else if(decision=="保留"||decision=="确认问题")decision="keep";double confidence=item.confidence>1?item.confidence/100.0:item.confidence;ComplianceFindingData hit=currentComplianceFindings.FirstOrDefault(x=>!String.IsNullOrWhiteSpace(item.hit_id)&&x.Id==item.hit_id);int locatedStart=-1,locatedLength=0;if(hit==null&&!String.IsNullOrWhiteSpace(item.excerpt)){TryLocateCompliancePhrase(original,item.excerpt,out locatedStart,out locatedLength);hit=currentComplianceFindings.FirstOrDefault(x=>(locatedStart>=0&&x.Start==locatedStart)||(!String.IsNullOrWhiteSpace(item.rule_id)&&x.RuleId==item.rule_id&&NormalizeComplianceText(x.Text)==NormalizeComplianceText(item.excerpt)));}
                if(hit!=null)
                {
                    if(!String.IsNullOrWhiteSpace(item.reason))hit.ModelComment=item.reason;if(!String.IsNullOrWhiteSpace(item.suggestion))hit.Suggestion=item.suggestion;if(!String.IsNullOrWhiteSpace(item.severity))hit.Severity=item.severity;
                    bool maySuppress=confidence>=0.65&&(decision=="dismiss"||decision=="covered");if(maySuppress)
                    {
                        var suppressed=CloneComplianceFindings(new[]{hit})[0];suppressed.Disposition=decision=="covered"?"风险提示已覆盖":"模型判定误报";suppressed.CoverageNote=(item.reason??"")+(confidence>0?"（置信度 "+confidence.ToString("0.00")+"）":"");if(!currentComplianceCoveredFindings.Any(x=>x.Id==suppressed.Id))currentComplianceCoveredFindings.Add(suppressed);currentComplianceFindings.Remove(hit);if(decision=="covered")covered++;else dismissed++;lines.Add((decision=="covered"?"[已覆盖] ":"[排除误报] ")+(item.excerpt??hit.Text)+" — "+(item.reason??""));continue;
                    }
                    kept++;lines.Add("[保留] "+(item.excerpt??hit.Text)+" — "+(item.reason??hit.Reason));continue;
                }
                if(decision!="keep"||confidence<0.75||String.IsNullOrWhiteSpace(item.excerpt)||!TryLocateCompliancePhrase(original,item.excerpt,out locatedStart,out locatedLength))continue;
                currentComplianceFindings.Add(new ComplianceFindingData{Id=Guid.NewGuid().ToString("N"),RuleId=String.IsNullOrWhiteSpace(item.rule_id)?"AI-REVIEW":item.rule_id,Text=original.Substring(locatedStart,locatedLength),Start=locatedStart,Length=locatedLength,Category=String.IsNullOrWhiteSpace(item.category)?"模型补充":item.category,Severity=String.IsNullOrWhiteSpace(item.severity)?"高":item.severity,Reason=item.reason??"模型结合语境发现潜在风险",Suggestion=item.suggestion??"请交合规人员确认并改为客观、可核验表述。",Disposition="待确认",ModelComment=item.reason});added++;lines.Add("[模型补充] "+item.excerpt+" — "+(item.reason??""));
            }
            currentComplianceFindings.Sort((a,b)=>a.Start==b.Start?b.Length.CompareTo(a.Length):a.Start.CompareTo(b.Start));lines.Add("");lines.Add("复核统计：保留 "+kept+" · 排除误报 "+dismissed+" · 风险提示覆盖 "+covered+" · 模型补充 "+added);lines.Add("本结果仅作内部初筛，最终以合规人员意见为准。");display=String.Join(Environment.NewLine,lines.ToArray());return true;
        }

        void MergeModelFindings(string original,string result)
        {
            foreach(Match match in Regex.Matches(result??"",@"【命中原文】\s*([^【\r\n]+)([^\r\n]*)")){string text=match.Groups[1].Value.Trim(),tail=match.Groups[2].Value;if(String.IsNullOrWhiteSpace(text))continue;string ruleId=MarkerValue(tail,"规则ID","规则编号"),category=MarkerValue(tail,"类别","规则类别"),severity=MarkerValue(tail,"严重程度"),reason=MarkerValue(tail,"原因","违规或风险原因"),suggestion=MarkerValue(tail,"建议","可执行修改建议");int locatedStart,locatedLength;if(!TryLocateCompliancePhrase(original,text,out locatedStart,out locatedLength))continue;var coveredRule=complianceRuleDefinitions.FirstOrDefault(x=>x.Id==ruleId&&x.Category=="风险提示词");string coverageNote;if(coveredRule!=null&&HasRequiredDisclosure(original,coveredRule,out coverageNote))continue;if(IsNegatedOrExplanatoryUse(original,locatedStart,original.Substring(locatedStart,locatedLength)))continue;var existing=currentComplianceFindings.Where(x=>x.Start==locatedStart||(NormalizeComplianceText(x.Text)==NormalizeComplianceText(text))).ToList();if(existing.Count>0){foreach(var hit in existing){hit.Start=locatedStart;hit.Length=locatedLength;if(!String.IsNullOrWhiteSpace(reason))hit.ModelComment=reason;if(!String.IsNullOrWhiteSpace(suggestion))hit.Suggestion=suggestion;if(!String.IsNullOrWhiteSpace(severity))hit.Severity=severity;}}else{currentComplianceFindings.Add(new ComplianceFindingData{Id=Guid.NewGuid().ToString("N"),RuleId=String.IsNullOrWhiteSpace(ruleId)?"AI-REVIEW":ruleId,Text=original.Substring(locatedStart,locatedLength),Start=locatedStart,Length=locatedLength,Category=String.IsNullOrWhiteSpace(category)?"模型补充":category,Severity=String.IsNullOrWhiteSpace(severity)?"高":severity,Reason=reason,Suggestion=suggestion,Disposition="待确认",ModelComment=reason});}}
            currentComplianceFindings.Sort((a,b)=>a.Start==b.Start?b.Length.CompareTo(a.Length):a.Start.CompareTo(b.Start));
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

        void OpenComplianceReview(StashItem item=null)
        {
            if(compliancePanel==null)BuildCompliancePanel();if(RestoreShelvedIfNeeded(compliancePanel)){if(item==null)return;}if(complianceModelBox!=null){complianceModelBox.ItemsSource=null;complianceModelBox.ItemsSource=ActiveTextModels();complianceModelBox.Text=ActiveTextModel()??"";}if(item!=null){ShowComplianceEditor();complianceSourceName=item.Name??"中转资料";if(item.Kind=="image"&&File.Exists(item.Value)){complianceInput.Clear();SetComplianceImage(item.Value);complianceSource.Text="已带入图片："+complianceSourceName;complianceReferenceBox.Text="图片 1";}else{string reference;string extracted=ExtractStashPreviewText(item,out reference);complianceInput.Text=extracted;SetComplianceImage(null);complianceSource.Text="已带入中转资料："+complianceSourceName;complianceReferenceBox.Text=reference;}}if(!compliancePanel.IsVisible){var work=SystemParameters.WorkArea;compliancePanel.Width=Math.Max(900,work.Width-28);compliancePanel.Height=Math.Max(650,work.Height-28);compliancePanel.Left=work.Left+14;compliancePanel.Top=work.Top+14;compliancePanel.Show();}compliancePanel.Activate();if(item==null)complianceInput.Focus();RunLocalComplianceScan();if(item!=null&&item.Kind!="image"&&!String.IsNullOrWhiteSpace(complianceInput.Text))RenderComplianceOriginal(complianceInput.Text);
        }

        string ComplianceHash(string text,string imagePath)
        {
            using(var sha=SHA256.Create())using(var memory=new MemoryStream()){byte[] textBytes=Encoding.UTF8.GetBytes(text??"");memory.Write(textBytes,0,textBytes.Length);if(!String.IsNullOrWhiteSpace(imagePath)&&File.Exists(imagePath)){byte[] imageBytes=File.ReadAllBytes(imagePath);memory.Write(imageBytes,0,imageBytes.Length);}return String.Concat(sha.ComputeHash(memory.ToArray()).Select(x=>x.ToString("x2")));}
        }

        string ComplianceConclusion(){if(currentComplianceFindings.Any(x=>x.Severity=="禁止"&&x.Disposition!="误报"))return "不可使用";if(currentComplianceFindings.Any(x=>x.Disposition!="误报"))return "有风险";return "通过（仍需人工复核）";}

        List<ComplianceFindingData> CloneComplianceFindings(IEnumerable<ComplianceFindingData> source){return source.Select(x=>new ComplianceFindingData{Id=x.Id,RuleId=x.RuleId,Text=x.Text,Start=x.Start,Length=x.Length,Category=x.Category,Severity=x.Severity,Reason=x.Reason,Suggestion=x.Suggestion,RequiredWarning=x.RequiredWarning,ModelComment=x.ModelComment,Disposition=x.Disposition,CoverageNote=x.CoverageNote}).ToList();}

        void SyncCurrentComplianceAudit()
        {
            if(currentComplianceAudit==null)return;currentComplianceAudit.Reviewer=(complianceReviewerBox.Text??"").Trim();currentComplianceAudit.SourceReference=(complianceReferenceBox.Text??"").Trim();currentComplianceAudit.Findings=CloneComplianceFindings(currentComplianceFindings);currentComplianceAudit.CoveredFindings=CloneComplianceFindings(currentComplianceCoveredFindings);currentComplianceAudit.Conclusion=ComplianceConclusion();
        }

        void RunComplianceAudit()
        {
            if(imageAiBusy){complianceStatus.Text="上一项 AI 请求仍在进行";return;}string rawText=complianceInput.Text??"",text=rawText.Trim(),model=(complianceModelBox.Text??"").Trim(),key=LoadActiveTextKey();if(String.IsNullOrWhiteSpace(model))model=ActiveTextModel()??"";if(String.IsNullOrWhiteSpace(text)&&String.IsNullOrWhiteSpace(complianceImagePath)){complianceStatus.Text="请先输入文案，或从中转袋带入资料";return;}RunLocalComplianceScan();if(String.IsNullOrWhiteSpace(ActiveTextBaseUrl())||String.IsNullOrWhiteSpace(model)||String.IsNullOrWhiteSpace(key)){complianceStatus.Text="本地规则扫描已完成；配置文本模型后可进行语境复核";RenderComplianceOriginal(rawText);return;}var textProfile=ActiveTextProfile();if(textProfile!=null)textProfile.Model=model;imageAiConfig.TextModel=model;if(!ActiveTextModels().Contains(model))ActiveTextModels().Add(model);SaveImageAiConfig();imageAiBusy=true;complianceStatus.Text="本地命中 "+currentComplianceFindings.Count+" 处；模型正在结合语境复核…";var request=new Dictionary<string,object>{{"mode","audit"},{"provider",TextUsesToApis()?"toapis":"official"},{"base_url",ActiveTextBaseUrl()},{"model",model},{"instructions",ComplianceInstruction(currentComplianceFindings)},{"content",text},{"image_path",complianceImagePath??""}};
            RunImageHelper(request,key,null,delegate(Dictionary<string,object> response){imageAiBusy=false;bool ok=response!=null&&response.ContainsKey("ok")&&Convert.ToBoolean(response["ok"]);string result=ok?Convert.ToString(response["text"]):"模型复核失败："+(response!=null&&response.ContainsKey("error")?Convert.ToString(response["error"]):"未知错误"),displayed=result;bool structured=false;if(ok){structured=ApplyStructuredComplianceReview(rawText,result,out displayed);if(!structured)MergeModelFindings(rawText,result);}complianceResult.Text=displayed;currentComplianceAudit=new ComplianceAuditRecord{Id=Guid.NewGuid().ToString("N"),ContentHash=ComplianceHash(text,complianceImagePath),RulebookVersion=complianceRulebookVersion,Model=model,Provider=(TextUsesToApis()?"ToApis":"OpenAI兼容")+" / "+ProviderHost(),PromptVersion=CompliancePromptVersion,ReviewedAt=DateTime.Now.ToString("o"),Reviewer=(complianceReviewerBox.Text??"").Trim(),SourceName=complianceSourceName,SourceReference=(complianceReferenceBox.Text??"").Trim(),OriginalText=rawText,ModelResult=displayed,Findings=CloneComplianceFindings(currentComplianceFindings),CoveredFindings=CloneComplianceFindings(currentComplianceCoveredFindings),Conclusion=ComplianceConclusion()};complianceAudits.Insert(0,currentComplianceAudit);while(complianceAudits.Count>500)complianceAudits.RemoveAt(complianceAudits.Count-1);SaveComplianceAudits();RefreshComplianceFindings();RenderComplianceOriginal(rawText);complianceStatus.Text=ok?(structured?"审核完成 · AI 已逐项复核本地命中并排除高置信误报":"审核完成 · 模型未返回结构化结果，已使用兼容解析"):("本地规则审核已完成；"+result);});
        }

        string ProviderHost(){try{return new Uri(ActiveTextBaseUrl()).Host;}catch{return "自定义接口";}}
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
