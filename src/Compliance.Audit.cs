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

        void InitializeCompliancePaths(){complianceAuditFile=Path.Combine(dataDir,"compliance-audits.json");LoadComplianceRuleDefinitions();}

        void LoadComplianceAudits(){try{if(File.Exists(complianceAuditFile)){var loaded=json.Deserialize<List<ComplianceAuditRecord>>(File.ReadAllText(complianceAuditFile,Encoding.UTF8));if(loaded!=null)complianceAudits.AddRange(loaded.Where(x=>x!=null));}}catch{try{File.Copy(complianceAuditFile,complianceAuditFile+".bak",true);}catch{}}}

        void SaveComplianceAudits(){try{Directory.CreateDirectory(dataDir);string temp=complianceAuditFile+".tmp";File.WriteAllText(temp,json.Serialize(complianceAudits),new UTF8Encoding(false));File.Copy(temp,complianceAuditFile,true);File.Delete(temp);}catch(Exception ex){if(complianceStatus!=null)complianceStatus.Text="审核记录保存失败："+ex.Message;}}

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
}
}
