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

        string ComplianceInstruction(IEnumerable<ComplianceFindingData> localHits)
        {
            string source=complianceInput==null?"":complianceInput.Text??"";string local=String.Join("\n",localHits.Select(x=>"hit_id="+x.Id+" | rule_id="+x.RuleId+" | 类别="+x.Category+" | 严重程度="+x.Severity+" | 命中原文=“"+x.Text+"” | 上下文=“"+ComplianceContext(source,x.Start,x.Length)+"” | 规则原因="+x.Reason).ToArray());if(String.IsNullOrWhiteSpace(local))local="无。本轮仍要检查确有依据的遗漏风险，但不要为了凑数新增问题。";string covered=String.Join("\n",currentComplianceCoveredFindings.Select(x=>x.RuleId+" | “"+x.Text+"” | "+x.CoverageNote).Distinct().ToArray());if(String.IsNullOrWhiteSpace(covered))covered="无";
            return "你是公募基金营销素材的内部合规复核助手。任务不是关键词复述，而是对本地初筛逐项做语境复核，并仅补充证据明确的遗漏。\n\n判定原则：\n- keep：表述在当前语境下确有未解决风险。\n- dismiss：只是法规讲解、否定表述、客观名称、引用、普通叙述或其他明显误报。\n- covered：仅限风险提示型规则，且素材中已经出现主题匹配、要素完整的专项风险揭示。一般性‘投资有风险’不能覆盖专项风险。\n- 禁用词、收益承诺、虚假或误导性表述不能被一般免责声明覆盖。\n- 不得因关键词本身存在就判违规，必须阅读前后文；也不得为了显得严格而凑数。\n\n规则版本："+complianceRulebookVersion+"\n\n必须逐项复核的本地命中（每个 hit_id 恰好返回一次）：\n"+local+"\n\n本地已确认由专项风险提示覆盖（不要重复输出）：\n"+covered+"\n\n完整规则：\n"+ComplianceRules()+"\n\n只返回一个 JSON 对象，不要 Markdown、不要代码围栏、不要额外解释。结构必须是：\n{\"conclusion\":\"通过|有风险|不可使用\",\"summary\":\"一句话总体说明\",\"items\":[{\"hit_id\":\"本地编号；新增问题留空\",\"excerpt\":\"逐字复制自素材的最短完整原文\",\"rule_id\":\"规则编号或AI-REVIEW\",\"decision\":\"keep|dismiss|covered\",\"category\":\"禁用词|敏感词|风险提示词|模型补充\",\"severity\":\"禁止|高|提示\",\"reason\":\"结合当前语境的具体原因\",\"suggestion\":\"可直接执行的修改建议\",\"confidence\":0.00}]}\n新增问题仅在 confidence>=0.75 且 excerpt 能逐字定位到素材时输出；没有新增问题不要虚构。";
        }

        string ComplianceContext(string source,int start,int length)
        {
            if(String.IsNullOrEmpty(source)||start<0||start>=source.Length)return "";int begin=Math.Max(0,start-70),end=Math.Min(source.Length,start+Math.Max(1,length)+70);return source.Substring(begin,end-begin).Replace("\r"," ").Replace("\n"," ");
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

        string ProviderHost(){try{return new Uri(ActiveTextBaseUrl()).Host;}catch{return "自定义接口";}}
}
}
