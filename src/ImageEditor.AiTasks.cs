using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MomoPetApp
{
    public partial class PetController
    {

        void TrackImageTask(Dictionary<string,object> request,bool layers)
        {
            lastImageTaskRequest=new Dictionary<string,object>(request);lastImageTaskLayers=layers;lastImageTaskOutput=request.ContainsKey("output_path")?Convert.ToString(request["output_path"]):null;lastImageTaskInputs.Clear();
            if(request.ContainsKey("image_path"))lastImageTaskInputs.Add(Convert.ToString(request["image_path"]));if(request.ContainsKey("reference_paths")){var paths=request["reference_paths"] as System.Collections.IEnumerable;if(paths!=null)foreach(object path in paths)lastImageTaskInputs.Add(Convert.ToString(path));}
            if(imageRetryButton!=null)imageRetryButton.IsEnabled=false;if(imageTaskSummary!=null)imageTaskSummary.Text="任务进行中：可继续查看画布；完成后结果会保留在这里。";
        }

        void FinishImageTask(bool success,string message)
        {
            if(imageTaskSummary!=null)imageTaskSummary.Text=(success?"任务完成：":"任务失败：")+message;if(imageRetryButton!=null)imageRetryButton.IsEnabled=!success&&lastImageTaskRequest!=null;
            if(success){foreach(string input in lastImageTaskInputs.Where(x=>x!=null&&x.StartsWith(imageTempDir,StringComparison.OrdinalIgnoreCase)).ToList())DeleteTemporaryImageFile(input);lastImageTaskInputs.Clear();}
        }

        void RetryLastImageTask()
        {
            if(lastImageTaskRequest==null){imageStatus.Text="没有可重试的图像任务";return;}if(imageAiBusy)return;
            foreach(string input in lastImageTaskInputs)if(!String.IsNullOrWhiteSpace(input)&&!File.Exists(input)){imageStatus.Text="原始输入已不在临时工作区，无法安全重试；请重新提交。";return;}
            imageAiBusy=true;if(imageRetryButton!=null)imageRetryButton.IsEnabled=false;imageStatus.Text="正在按原请求重试，不会改变图片尺寸或内容…";string mode=Convert.ToString(lastImageTaskRequest["mode"]);
            RunImageHelper(lastImageTaskRequest,null,LoadActiveImageKey(),delegate(Dictionary<string,object> response){if(mode=="precision")HandlePrecisionImageResponse(response,lastImageTaskLayers);else HandleGeneratedImageResponse(response,lastImageTaskOutput);});
        }

        void RunImageGenerate(bool useReference)
        {
            if(imageAiBusy||!ValidateImageAi(false))return;string prompt=(redrawPromptBox.Text??"").Trim();if(String.IsNullOrEmpty(prompt)){imageStatus.Text="请先填写生成提示词";return;}
            if(useReference&&workingBitmap==null&&extraReferenceImages.Count==0){imageStatus.Text="参考图生图需要画布上有图片，或先上传附加参考图";return;}
            RememberCurrentImagePrompt();bool toApis=ImageUsesToApis();imageAiBusy=true;string output=Path.Combine(imageTempDir,"result-"+Guid.NewGuid().ToString("N")+".png"),input=null;string aspect=toApis?imageAiConfig.ToApisAspectRatio:NearestImageAiAspect(workingBitmap),resolution=toApis?imageAiConfig.ToApisResolution:ImageAiResolution(aspect),quality=toApis?imageAiConfig.ToApisQuality:imageAiConfig.ImageQuality;var request=new Dictionary<string,object>{{"provider","auto"},{"protocol",ActiveImageProtocol()},{"base_url",ActiveImageBaseUrl()},{"model",ActiveImageModel()},{"output_path",output},{"prompt",prompt},{"aspect_ratio",aspect},{"resolution",resolution},{"size",toApis?"auto":imageAiConfig.ImageSize},{"transparent_background",imageAiConfig.TransparentBackground},{"quality",quality}};
            if(useReference){
                var referencePaths=new List<string>();
                if(workingBitmap!=null){input=PrepareWorkingImageFile();referencePaths.Add(input);}
                referencePaths.AddRange(extraReferenceImages);
                request["mode"]="edit";request["image_path"]=referencePaths[0];
                if(referencePaths.Count>1)request["reference_paths"]=referencePaths.Skip(1).ToArray();
                request["prompt"]="以输入图片作为主要视觉参考，在保留关键主体特征的基础上生成新图片。用户要求：\n"+prompt;
            }else request["mode"]="generate";
            imageStatus.Text=(useReference?(workingBitmap!=null?"正在参考画布图片"+(extraReferenceImages.Count>0?"和 "+extraReferenceImages.Count+" 张附加参考图":"")+"生成新图":"正在参考 "+extraReferenceImages.Count+" 张附加参考图生成新图"):"正在根据文字生成图片")+(toApis?(" · ToApis · "+aspect+" · "+resolution+" · "+ToApisCostShortText()):(" · "+imageAiConfig.ImageSize+" · 清晰度 "+quality+(imageAiConfig.TransparentBackground?" · 透明 PNG":"")))+"…";
            TrackImageTask(request,false);RunImageHelper(request,null,LoadActiveImageKey(),delegate(Dictionary<string,object> response){if(input!=null&&response!=null&&response.ContainsKey("ok")&&Convert.ToBoolean(response["ok"]))DeleteTemporaryImageFile(input);HandleGeneratedImageResponse(response,output);});
        }

        void RunImageEdit(bool replaceText)
        {
            if(workingBitmap==null){imageStatus.Text="请先打开或生成一张图片";return;}if(imageAiBusy||!ValidateImageAi(false))return;string prompt=replaceText?(recognizedTextBox.Text??"").Trim():(redrawPromptBox.Text??"").Trim();if(String.IsNullOrEmpty(prompt)){imageStatus.Text=replaceText?"请先识别或填写要替换的文字":"请填写重绘提示词";return;}if(replaceText)prompt="保留图片构图、人物、颜色和非文字元素不变，只重新绘制图片中的文字。请让所有可见文字严格变为以下内容，并保持清晰自然：\n"+prompt;
            if(!replaceText)RememberCurrentImagePrompt();bool toApis=ImageUsesToApis();imageAiBusy=true;string input=PrepareWorkingImageFile(),output=Path.Combine(imageTempDir,"result-"+Guid.NewGuid().ToString("N")+".png");string aspect=toApis?imageAiConfig.ToApisAspectRatio:NearestImageAiAspect(workingBitmap),resolution=toApis?imageAiConfig.ToApisResolution:ImageAiResolution(aspect),quality=toApis?imageAiConfig.ToApisQuality:imageAiConfig.ImageQuality;imageStatus.Text="正在通过当前来源创建图像编辑任务…";var request=new Dictionary<string,object>{{"mode","edit"},{"provider","auto"},{"protocol",ActiveImageProtocol()},{"base_url",ActiveImageBaseUrl()},{"model",ActiveImageModel()},{"image_path",input},{"output_path",output},{"prompt",prompt},{"aspect_ratio",aspect},{"resolution",resolution},{"size",toApis?"auto":imageAiConfig.ImageSize},{"transparent_background",imageAiConfig.TransparentBackground},{"quality",quality}};if(!replaceText&&extraReferenceImages.Count>0)request["reference_paths"]=extraReferenceImages.ToArray();
            TrackImageTask(request,false);RunImageHelper(request,null,LoadActiveImageKey(),delegate(Dictionary<string,object> response){if(response!=null&&response.ContainsKey("ok")&&Convert.ToBoolean(response["ok"]))DeleteTemporaryImageFile(input);HandleGeneratedImageResponse(response,output);});
        }

        string ToApisCostShortText(){string model=(ActiveImageModel()??"").Trim(),resolution=imageAiConfig.ToApisResolution??"auto";if(resolution=="auto")return "自动尺寸 · 积分以实际结果为准";if(!String.Equals(model,"gpt-image-2",StringComparison.OrdinalIgnoreCase))return "积分以账户计费为准";return "预计 "+(resolution=="4K"?5:resolution=="2K"?4:3)+" 积分/张";}

        string NearestImageAiAspect(BitmapSource bitmap)
        {
            double ratio=1;if(bitmap!=null&&bitmap.PixelHeight>0)ratio=(double)bitmap.PixelWidth/bitmap.PixelHeight;else{int width,height;if(Int32.TryParse(widthBox==null?"":widthBox.Text,out width)&&Int32.TryParse(heightBox==null?"":heightBox.Text,out height)&&height>0)ratio=(double)width/height;}
            string[] labels={"1:1","3:2","2:3","4:3","3:4","5:4","4:5","16:9","9:16","2:1","1:2","21:9","9:21"};double[] values={1,1.5,2.0/3,4.0/3,3.0/4,1.25,.8,16.0/9,9.0/16,2,.5,21.0/9,9.0/21};int best=0;double distance=Double.MaxValue;
            for(int i=0;i<values.Length;i++){double current=Math.Abs(Math.Log(Math.Max(.01,ratio)/values[i]));if(current<distance){distance=current;best=i;}}return labels[best];
        }

        string ImageAiResolution(string aspect)
        {
            return aspect=="1:1"||aspect=="3:2"||aspect=="2:3"?"1K":"2K";
        }

        void HandleGeneratedImageResponse(Dictionary<string,object> response,string output)
        {
            imageAiBusy=false;if(!ImageResponseOk(response)){try{File.Delete(output);}catch{}return;}try{workingEncodedBytes=File.ReadAllBytes(output);workingBitmap=LoadEditorBytes(workingEncodedBytes);ClearCompressionCandidate();ClearImageTextLayer();ExitCropMode();editorImage.Source=workingBitmap;widthBox.Text=workingBitmap.PixelWidth.ToString();heightBox.Text=workingBitmap.PixelHeight.ToString();outputFormatBox.SelectedItem="PNG";ScheduleCompressionEstimate();PushImageState();UpdateImageFacts();imageStatus.Text=editingImageItem==null?"AI 图片已生成；可继续改字、重绘、裁切或压缩，确认后保存到中转袋":"AI 图片已返回预览；可继续编辑，满意后确认保存";FinishImageTask(true,"结果已载入画布。按住“对比原图”可随时核对，再决定是否保存。");}catch(Exception ex){imageStatus.Text="AI 图片读取失败："+ex.Message;FinishImageTask(false,imageStatus.Text);}finally{try{File.Delete(output);}catch{}}
        }

        bool ImageResponseOk(Dictionary<string,object> response){bool ok=response!=null&&response.ContainsKey("ok")&&Convert.ToBoolean(response["ok"]);if(!ok){imageAiBusy=false;if(imageStatus!=null)imageStatus.Text=response!=null&&response.ContainsKey("error")?Convert.ToString(response["error"]):"图像 AI 调用失败";FinishImageTask(false,imageStatus==null?"图像 AI 调用失败":imageStatus.Text);}return ok;}

        string DetectLocalAiProxy()
        {
            foreach(int port in new[]{7890,7897,10809,10808})try{using(var client=new System.Net.Sockets.TcpClient()){var pending=client.BeginConnect("127.0.0.1",port,null,null);if(pending.AsyncWaitHandle.WaitOne(80)&&client.Connected){client.EndConnect(pending);return "http://127.0.0.1:"+port;}}}catch{}
            return null;
        }

        void RunImageHelper(Dictionary<string,object> request,string textKey,string imageKey,Action<Dictionary<string,object>> completed)
        {
            string helper=EmbeddedRuntime.ResolveFile(Path.Combine("wind_bridge","image_ai.mjs"),root),requestPath=Path.Combine(imageTempDir,"request-"+Guid.NewGuid().ToString("N")+".json"),localProxy=DetectLocalAiProxy();ThreadPool.QueueUserWorkItem(delegate{try{File.WriteAllText(requestPath,json.Serialize(request),new UTF8Encoding(false));var psi=new ProcessStartInfo{WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,Arguments="\""+helper+"\" \""+requestPath+"\""};EmbeddedRuntime.UseBundledNode(psi,root);psi.EnvironmentVariables["TEXT_LLM_API_KEY"]=textKey??"";psi.EnvironmentVariables["IMAGE_LLM_API_KEY"]=imageKey??"";if(!String.IsNullOrEmpty(localProxy)){psi.EnvironmentVariables["NODE_USE_ENV_PROXY"]="1";psi.EnvironmentVariables["HTTP_PROXY"]=localProxy;psi.EnvironmentVariables["HTTPS_PROXY"]=localProxy;}string output,errors;using(var process=Process.Start(psi)){imageAiProcess=process;output=process.StandardOutput.ReadToEnd();errors=process.StandardError.ReadToEnd();process.WaitForExit();}imageAiProcess=null;Dictionary<string,object> parsed=null;try{parsed=json.Deserialize<Dictionary<string,object>>(output);}catch{}if(parsed==null)parsed=new Dictionary<string,object>{{"ok",false},{"error",String.IsNullOrWhiteSpace(errors)?"图像 AI 返回无法解析":errors.Trim()}};app.Dispatcher.BeginInvoke(new Action(delegate{try{completed(parsed);}catch(Exception ex){imageAiBusy=false;if(imageStatus!=null)imageStatus.Text="图像处理结果无法显示："+ex.Message;}}));}catch(Exception ex){app.Dispatcher.BeginInvoke(new Action(delegate{try{completed(new Dictionary<string,object>{{"ok",false},{"error",ex.Message}});}catch{imageAiBusy=false;if(imageStatus!=null)imageStatus.Text="图像处理被安全终止："+ex.Message;}}));}finally{textKey=null;imageKey=null;try{File.Delete(requestPath);}catch{}}});
        }

        void ConfirmImageOverwrite()
        {
            if(workingBitmap==null){imageStatus.Text="当前没有可以保存的图片";return;}try{string format=Convert.ToString(outputFormatBox.SelectedItem)??"PNG";BitmapSource saveBitmap=layerCompositionActive?RenderPrecisionLayerComposition():workingBitmap;byte[] data=layerCompositionActive?EncodeBitmap(saveBitmap,format,92):(workingEncodedBytes??EncodeBitmap(workingBitmap,format,92));string desiredExt=format=="JPEG"?".jpg":".png";
                if(editingImageItem==null){string generated=Path.Combine(stashDir,DateTime.Now.ToString("yyyyMMdd-HHmmss-")+Guid.NewGuid().ToString("N").Substring(0,6)+desiredExt);File.WriteAllBytes(generated,data);editingImageItem=new StashItem{Id=Guid.NewGuid().ToString("N"),Kind="image",Name=Path.GetFileName(generated),Value=generated,Owned=true,Created=DateTime.Now.ToString("o"),SourceApp="AI 图片"};if(!AddStashItemSmart(editingImageItem))editingImageItem=stashItems.FirstOrDefault(x=>x.ContentHash==editingImageItem.ContentHash)??editingImageItem;SaveStash();RefreshStash();originalBitmap=LoadEditorBitmap(editingImageItem.Value);workingBitmap=originalBitmap;workingEncodedBytes=null;ClearCompressionCandidate();editorImage.Source=workingBitmap;ScheduleCompressionEstimate();ResetImageHistory();imageStatus.Text="已保存到中转袋："+editingImageItem.Name;React("新图片放进中转袋啦～",true);return;}
                string original=editingImageItem.Value,originalExt=Path.GetExtension(original).ToLowerInvariant();Directory.CreateDirectory(imageBackupDir);string backup=Path.Combine(imageBackupDir,DateTime.Now.ToString("yyyyMMdd-HHmmss-")+Guid.NewGuid().ToString("N").Substring(0,6)+"-"+Path.GetFileName(original));File.Copy(original,backup,true);string target=original;
                if((desiredExt==".jpg"&&(originalExt!=".jpg"&&originalExt!=".jpeg"))||(desiredExt==".png"&&originalExt!=".png")){target=Path.Combine(stashDir,Path.GetFileNameWithoutExtension(original)+"-edited-"+DateTime.Now.ToString("HHmmss")+desiredExt);editingImageItem.Owned=true;}
                string temp=target+".momopet.tmp";File.WriteAllBytes(temp,data);File.Copy(temp,target,true);File.Delete(temp);editingImageItem.Value=target;editingImageItem.Name=Path.GetFileName(target);SaveStash();RefreshStash();originalBitmap=LoadEditorBitmap(target);workingBitmap=originalBitmap;workingEncodedBytes=null;ClearCompressionCandidate();editorImage.Source=workingBitmap;ScheduleCompressionEstimate();ResetImageHistory();imageStatus.Text="已保存。原图备份："+backup;React("图片改好并保存啦～",true);
            }catch(Exception ex){imageStatus.Text="保存失败："+ex.Message;}
        }
}
}
