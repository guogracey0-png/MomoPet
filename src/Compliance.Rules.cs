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
}
}
