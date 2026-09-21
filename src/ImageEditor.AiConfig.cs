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

        void InitializeImageEditorPaths()
        {
            imageAiConfigFile=Path.Combine(dataDir,"image-ai-settings.json");
            imageTextKeyFile=Path.Combine(dataDir,"image-text-key.dat");
            imageModelKeyFile=Path.Combine(dataDir,"image-model-key.dat");
            toApisTextKeyFile=Path.Combine(dataDir,"toapis-text-key.dat");
            toApisImageKeyFile=Path.Combine(dataDir,"toapis-image-key.dat");
            precisionImageKeyFile=Path.Combine(dataDir,"precision-image-key.dat");
            imageBackupDir=Path.Combine(dataDir,"ImageBackups");
            imageTempDir=Path.Combine(dataDir,"ImageAiTemp");
            imageOcrCacheDir=Path.Combine(dataDir,"ImageOcrCache");
            try { Directory.CreateDirectory(imageBackupDir); Directory.CreateDirectory(imageTempDir); Directory.CreateDirectory(imageOcrCacheDir); } catch { }
            try { if(File.Exists(imageAiConfigFile)) imageAiConfig=json.Deserialize<ImageAiConfig>(File.ReadAllText(imageAiConfigFile,Encoding.UTF8)); } catch { imageAiConfig=null; }
            if(imageAiConfig==null) imageAiConfig=new ImageAiConfig();
            if(imageAiConfig.TextModels==null) imageAiConfig.TextModels=new List<string>();
            if(imageAiConfig.ImageModels==null) imageAiConfig.ImageModels=new List<string>();
            if(imageAiConfig.ToApisTextModels==null) imageAiConfig.ToApisTextModels=new List<string>();
            if(imageAiConfig.ToApisImageModels==null) imageAiConfig.ToApisImageModels=new List<string>();
            if(imageAiConfig.PrecisionModels==null) imageAiConfig.PrecisionModels=new List<string>();
            if(imageAiConfig.PromptLibrary==null) imageAiConfig.PromptLibrary=new List<string>();
            if(imageAiConfig.ImageProfiles==null) imageAiConfig.ImageProfiles=new List<ImageProviderProfile>();
            if(imageAiConfig.TextProfiles==null) imageAiConfig.TextProfiles=new List<ImageProviderProfile>();
            if(String.IsNullOrWhiteSpace(imageAiConfig.TextProvider)) imageAiConfig.TextProvider="官方 API";
            if(String.IsNullOrWhiteSpace(imageAiConfig.ImageProvider)) imageAiConfig.ImageProvider="官方 API";
            if(String.IsNullOrWhiteSpace(imageAiConfig.ToApisTextBaseUrl)) imageAiConfig.ToApisTextBaseUrl="https://toapis.com/v1";
            if(String.IsNullOrWhiteSpace(imageAiConfig.ToApisImageBaseUrl)) imageAiConfig.ToApisImageBaseUrl="https://toapis.com/v1";
            if(String.IsNullOrWhiteSpace(imageAiConfig.ImageQuality)) imageAiConfig.ImageQuality="auto";
            if(String.IsNullOrWhiteSpace(imageAiConfig.ImageSize)) imageAiConfig.ImageSize="auto";
            if(String.IsNullOrWhiteSpace(imageAiConfig.ToApisAspectRatio)) imageAiConfig.ToApisAspectRatio="auto";
            if(String.IsNullOrWhiteSpace(imageAiConfig.ToApisResolution)) imageAiConfig.ToApisResolution="auto";
            if(String.IsNullOrWhiteSpace(imageAiConfig.ToApisQuality)) imageAiConfig.ToApisQuality="low";
            // 新建精确编辑配置默认直连火山方舟；已有 ToApis 配置不自动改写，避免覆盖用户现有通道。
            if(String.IsNullOrWhiteSpace(imageAiConfig.PrecisionBaseUrl)) imageAiConfig.PrecisionBaseUrl="https://ark.cn-beijing.volces.com/api/v3";
            if(String.IsNullOrWhiteSpace(imageAiConfig.PrecisionModel)) imageAiConfig.PrecisionModel="doubao-seedream-5-0-pro-260628";
            if(!imageAiConfig.PrecisionModels.Contains(imageAiConfig.PrecisionModel)) imageAiConfig.PrecisionModels.Add(imageAiConfig.PrecisionModel);
            if(!imageAiConfig.RememberImagePrompt.HasValue) imageAiConfig.RememberImagePrompt=true;
            // 兼容上一版错误拆分的“官方 / ToApis”配置：迁回一套文本、一套图像，之后只按 Base URL 自动识别。
            if(String.Equals(imageAiConfig.TextProvider,"ToApis",StringComparison.OrdinalIgnoreCase)){
                if(!String.IsNullOrWhiteSpace(imageAiConfig.ToApisTextBaseUrl))imageAiConfig.TextBaseUrl=imageAiConfig.ToApisTextBaseUrl;
                if(!String.IsNullOrWhiteSpace(imageAiConfig.ToApisTextModel))imageAiConfig.TextModel=imageAiConfig.ToApisTextModel;
                if(imageAiConfig.ToApisTextModels.Count>0)imageAiConfig.TextModels=imageAiConfig.ToApisTextModels.ToList();
                string migrated=LoadImageAiKey(true,true);if(!String.IsNullOrWhiteSpace(migrated))SaveImageAiKey(true,migrated,false);
            }
            if(String.Equals(imageAiConfig.ImageProvider,"ToApis",StringComparison.OrdinalIgnoreCase)){
                if(!String.IsNullOrWhiteSpace(imageAiConfig.ToApisImageBaseUrl))imageAiConfig.ImageBaseUrl=imageAiConfig.ToApisImageBaseUrl;
                if(!String.IsNullOrWhiteSpace(imageAiConfig.ToApisImageModel))imageAiConfig.ImageModel=imageAiConfig.ToApisImageModel;
                if(imageAiConfig.ToApisImageModels.Count>0)imageAiConfig.ImageModels=imageAiConfig.ToApisImageModels.ToList();
                string migrated=LoadImageAiKey(false,true);if(!String.IsNullOrWhiteSpace(migrated))SaveImageAiKey(false,migrated,false);
            }
            imageAiConfig.TextProvider="自动";imageAiConfig.ImageProvider="自动";SaveImageAiConfig();
            MigrateImageProfiles();
            MigrateTextProfiles();
        }

        void MigrateImageProfiles()
        {
            if(imageAiConfig.ImageProfiles==null)imageAiConfig.ImageProfiles=new List<ImageProviderProfile>();
            if(imageAiConfig.ImageProfiles.Count==0){
                var legacy=new ImageProviderProfile{Id=Guid.NewGuid().ToString("N"),Name=UrlUsesToApis(imageAiConfig.ImageBaseUrl)?"我的 ToApis":"默认图像来源",BaseUrl=imageAiConfig.ImageBaseUrl??"",Model=imageAiConfig.ImageModel??"",Models=(imageAiConfig.ImageModels??new List<string>()).ToList(),Protocol="auto",SupportsEditing=true,SupportsTransparency=true};
                imageAiConfig.ImageProfiles.Add(legacy);imageAiConfig.ActiveImageProfileId=legacy.Id;
                string legacyKey=LoadImageAiKey(false);if(!String.IsNullOrWhiteSpace(legacyKey))SaveProfileKey(legacy.Id,legacyKey);
            }
            foreach(var profile in imageAiConfig.ImageProfiles){if(String.IsNullOrWhiteSpace(profile.Id))profile.Id=Guid.NewGuid().ToString("N");if(profile.Models==null)profile.Models=new List<string>();if(String.IsNullOrWhiteSpace(profile.Protocol))profile.Protocol="auto";if(!profile.SupportsEditing)profile.SupportsEditing=true;}
            if(ActiveImageProfile()==null)imageAiConfig.ActiveImageProfileId=imageAiConfig.ImageProfiles[0].Id;
            SaveImageAiConfig();
        }

        ImageProviderProfile ActiveImageProfile(){return imageAiConfig==null||imageAiConfig.ImageProfiles==null?null:imageAiConfig.ImageProfiles.FirstOrDefault(x=>x.Id==imageAiConfig.ActiveImageProfileId);}

        string ProfileKeyFile(string id){return Path.Combine(dataDir,"image-profile-"+id+".key");}

        string LoadProfileKey(string id){try{string file=ProfileKeyFile(id);return File.Exists(file)?Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(file),ImageKeyEntropy("Profile."+id),DataProtectionScope.CurrentUser)):null;}catch{return null;}}

        void SaveProfileKey(string id,string key){byte[] plain=Encoding.UTF8.GetBytes(key);try{File.WriteAllBytes(ProfileKeyFile(id),ProtectedData.Protect(plain,ImageKeyEntropy("Profile."+id),DataProtectionScope.CurrentUser));}finally{Array.Clear(plain,0,plain.Length);}}

        string LoadActiveImageKey(){var profile=ActiveImageProfile();return profile==null?LoadImageAiKey(false):(LoadProfileKey(profile.Id)??LoadImageAiKey(false));}

        void MigrateTextProfiles()
        {
            if(imageAiConfig.TextProfiles==null)imageAiConfig.TextProfiles=new List<ImageProviderProfile>();
            if(imageAiConfig.TextProfiles.Count==0){var legacy=new ImageProviderProfile{Id=Guid.NewGuid().ToString("N"),Name=UrlUsesToApis(imageAiConfig.TextBaseUrl)?"我的 ToApis 文本":"默认文本来源",BaseUrl=imageAiConfig.TextBaseUrl??"",Model=imageAiConfig.TextModel??"",Models=(imageAiConfig.TextModels??new List<string>()).ToList(),Protocol="auto"};imageAiConfig.TextProfiles.Add(legacy);imageAiConfig.ActiveTextProfileId=legacy.Id;string key=LoadImageAiKey(true);if(!String.IsNullOrWhiteSpace(key))SaveProfileKey(legacy.Id,key);}
            foreach(var profile in imageAiConfig.TextProfiles){if(String.IsNullOrWhiteSpace(profile.Id))profile.Id=Guid.NewGuid().ToString("N");if(profile.Models==null)profile.Models=new List<string>();if(String.IsNullOrWhiteSpace(profile.Protocol))profile.Protocol="auto";}
            if(ActiveTextProfile()==null)imageAiConfig.ActiveTextProfileId=imageAiConfig.TextProfiles[0].Id;SaveImageAiConfig();
        }

        ImageProviderProfile ActiveTextProfile(){return imageAiConfig==null||imageAiConfig.TextProfiles==null?null:imageAiConfig.TextProfiles.FirstOrDefault(x=>x.Id==imageAiConfig.ActiveTextProfileId);}

        string LoadActiveTextKey(){var profile=ActiveTextProfile();return profile==null?LoadImageAiKey(true):(LoadProfileKey(profile.Id)??LoadImageAiKey(true));}

        void SelectTextProfile(ImageProviderProfile profile){if(profile==null)return;imageAiConfig.ActiveTextProfileId=profile.Id;imageAiConfig.TextBaseUrl=profile.BaseUrl;imageAiConfig.TextModel=profile.Model;imageAiConfig.TextModels=profile.Models??new List<string>();SaveImageAiConfig();SyncImageModelCombos();}

        void RememberCurrentImagePrompt(){if(imageAiConfig==null||redrawPromptBox==null||imageAiConfig.RememberImagePrompt!=true)return;imageAiConfig.SavedImagePrompt=redrawPromptBox.Text??"";SaveImageAiConfig();}
        // ToApis 大陆官方域名是 toapis.cn，海外是 toapis.com；必须一并识别，
        // 否则大陆用户会被当作普通 OpenAI 兼容接口，图生图发错端点必然失败。

        bool UrlUsesToApis(string value){try{string host=new Uri((value??"").Trim()).Host;return System.Text.RegularExpressions.Regex.IsMatch(host,@"(^|\.)toapis\.(com|cn|xyz)$",System.Text.RegularExpressions.RegexOptions.IgnoreCase);}catch{return false;}}

        bool TextUsesToApis(){return UrlUsesToApis(imageAiConfig.TextBaseUrl);}

        bool ImageUsesToApis(){var profile=ActiveImageProfile();return UrlUsesToApis(profile==null?imageAiConfig.ImageBaseUrl:profile.BaseUrl);}

        string ActiveTextBaseUrl(){var profile=ActiveTextProfile();return profile==null?imageAiConfig.TextBaseUrl:profile.BaseUrl;}

        string ActiveTextModel(){var profile=ActiveTextProfile();return profile==null?imageAiConfig.TextModel:profile.Model;}

        List<string> ActiveTextModels(){var profile=ActiveTextProfile();return profile==null?imageAiConfig.TextModels:profile.Models;}

        string ActiveImageBaseUrl(){var profile=ActiveImageProfile();return profile==null?imageAiConfig.ImageBaseUrl:profile.BaseUrl;}

        string ActiveImageModel(){var profile=ActiveImageProfile();return profile==null?imageAiConfig.ImageModel:profile.Model;}

        List<string> ActiveImageModels(){var profile=ActiveImageProfile();return profile==null?imageAiConfig.ImageModels:profile.Models;}

        string ActiveImageProtocol(){var profile=ActiveImageProfile();return profile==null?"auto":profile.Protocol??"auto";}

        bool ActiveImageSupportsLayers(){var profile=ActiveImageProfile();return profile!=null&&profile.SupportsLayers;}

        bool ActiveImageSupportsTransparency(){var profile=ActiveImageProfile();return profile==null||profile.SupportsTransparency;}

        void SelectImageProfile(ImageProviderProfile profile)
        {
            if(profile==null)return;imageAiConfig.ActiveImageProfileId=profile.Id;imageAiConfig.ImageBaseUrl=profile.BaseUrl;imageAiConfig.ImageModel=profile.Model;imageAiConfig.ImageModels=profile.Models??new List<string>();SaveImageAiConfig();
            if(imageProfileCombo!=null&&imageProfileCombo.SelectedItem!=profile)imageProfileCombo.SelectedItem=profile;
            if(imageQuickModelCombo!=null){imageQuickModelCombo.ItemsSource=null;imageQuickModelCombo.ItemsSource=profile.Models;imageQuickModelCombo.Text=profile.Model??"";}UpdateImageProviderOptions();
        }

        void UpdateImageProviderOptions()
        {
            if(officialImageOptionsPanel==null)return;bool toApis=ImageUsesToApis();officialImageOptionsPanel.Visibility=toApis?Visibility.Collapsed:Visibility.Visible;toApisImageOptionsPanel.Visibility=toApis?Visibility.Visible:Visibility.Collapsed;
            var active=ActiveImageProfile();string source=active==null?"默认来源":active.Name;
            imageProviderHint.Text="当前来源："+source+" · "+(ActiveImageProtocol()=="ark"?"火山方舟图像协议":toApis?"OpenAI 兼容图像接口":"自动兼容模式（Images → Chat 图片回退）")+(ActiveImageSupportsTransparency()?" · 可请求透明 PNG":" · 未声明透明底能力");
            if(toApis){toApisResolutionBox.SelectedItem=imageAiConfig.ToApisResolution=="auto"?"自动（模型默认）":imageAiConfig.ToApisResolution;toApisQualityBox.SelectedItem=imageAiConfig.ToApisQuality;UpdateToApisAspectOptions();UpdateToApisCostPreview();}
        }

        void UpdateToApisAspectOptions()
        {
            // 比例是用户的构图意图，不应由前端按 1K/2K/4K 擅自裁掉。
            // 兼容来源若不接受某个组合，会由请求层回退到模型默认尺寸。
            if(toApisAspectBox==null)return;string automatic="自动（模型默认）";string[] values=new[]{automatic,"1:1","3:2","2:3","4:3","3:4","5:4","4:5","16:9","9:16","2:1","1:2","21:9","9:21"};
            string selected=imageAiConfig.ToApisAspectRatio??"auto";string display=selected=="auto"?automatic:selected;if(!values.Contains(display)){selected="auto";display=automatic;}imageAiConfig.ToApisAspectRatio=selected;toApisAspectBox.ItemsSource=values;toApisAspectBox.SelectedItem=display;string model=ActiveImageModel()??"";toApisQualityRow.Visibility=(model.IndexOf("official",StringComparison.OrdinalIgnoreCase)>=0||model.IndexOf("vip",StringComparison.OrdinalIgnoreCase)>=0)?Visibility.Visible:Visibility.Collapsed;
        }

        void UpdateToApisCostPreview()
        {
            if(toApisCostText==null)return;string model=(ActiveImageModel()??"").Trim();string resolution=imageAiConfig.ToApisResolution??"auto";if(resolution=="auto"){toApisCostText.Text="自动尺寸：不指定比例和分辨率，由当前模型决定 · 积分以 ToApis 实际计费为准";}else if(String.Equals(model,"gpt-image-2",StringComparison.OrdinalIgnoreCase)){int credits=resolution=="4K"?5:resolution=="2K"?4:3;toApisCostText.Text="预计消耗："+credits+" 积分 / 张 · 提交前提醒，最终以 ToApis 账户计费为准";}else toApisCostText.Text="预计积分：当前模型请以 ToApis 账户价格为准";
        }

        void SetTransparentBackground(bool enabled){imageAiConfig.TransparentBackground=enabled;if(transparentBackgroundBox!=null&&transparentBackgroundBox.IsChecked!=enabled)transparentBackgroundBox.IsChecked=enabled;if(toApisTransparentBackgroundBox!=null&&toApisTransparentBackgroundBox.IsChecked!=enabled)toApisTransparentBackgroundBox.IsChecked=enabled;SaveImageAiConfig();if(enabled&&outputFormatBox!=null)outputFormatBox.SelectedItem="PNG";if(imageStatus!=null)imageStatus.Text=enabled?"透明底图已开启：将请求 background=transparent 与 PNG Alpha 通道":"透明底图已关闭";}

        void SetImageQuality(string quality){string normalized=(quality??"auto").Trim().ToLowerInvariant();if(normalized!="low"&&normalized!="medium"&&normalized!="high")normalized="auto";imageAiConfig.ImageQuality=normalized;SaveImageAiConfig();if(imageStatus!=null)imageStatus.Text="图像生成质量已设为 "+normalized;}

        void SetImageSize(string size){string normalized=(size??"auto").Trim().ToLowerInvariant().Replace('×','x').Replace(" ","");if(normalized=="auto"){imageAiConfig.ImageSize="auto";SaveImageAiConfig();if(imageStatus!=null)imageStatus.Text="图像分辨率已设为自动";return;}string[] parts=normalized.Split('x');int width,height;if(parts.Length!=2||!Int32.TryParse(parts[0],out width)||!Int32.TryParse(parts[1],out height)||width%16!=0||height%16!=0||Math.Max(width,height)>3840||Math.Max(width,height)>Math.Min(width,height)*3||(long)width*height<655360||(long)width*height>8294400){if(imageStatus!=null)imageStatus.Text="分辨率无效：宽高须为 16 的倍数，最长边不超过 3840，比例不超过 3:1，总像素需在官方范围内";if(imageSizeBox!=null)imageSizeBox.Text=imageAiConfig.ImageSize??"auto";return;}imageAiConfig.ImageSize=width+"x"+height;SaveImageAiConfig();if(imageStatus!=null)imageStatus.Text="图像分辨率已设为 "+imageAiConfig.ImageSize;}

        ComboBox BuildPromptLibraryControls(StackPanel parent,TextBox promptBox)
        {
            parent.Children.Add(new TextBlock{Text="提示词库（选中即填入，可存常用提示词）",FontWeight=FontWeights.Bold,Margin=new Thickness(0,8,0,3)});
            var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            var box=new ComboBox{Height=32,Padding=new Thickness(7,4,7,4),ToolTip="保存常用提示词，下次生成时直接调用"};
            box.SelectionChanged+=delegate{string selected=Convert.ToString(box.SelectedItem);if(!String.IsNullOrWhiteSpace(selected))promptBox.Text=selected;};
            var save=MakeButton("存入词库",new SolidColorBrush(Color.FromRgb(235,229,222)));save.Click+=delegate{SavePromptToLibrary(promptBox.Text);};
            var remove=MakeButton("删除",Brushes.Transparent);remove.Click+=delegate{RemovePromptFromLibrary(Convert.ToString(box.SelectedItem));};
            row.Children.Add(box);Grid.SetColumn(save,1);row.Children.Add(save);Grid.SetColumn(remove,2);row.Children.Add(remove);
            parent.Children.Add(row);promptLibraryBoxes.Add(box);return box;
        }

        void RefreshPromptLibraryBoxes()
        {
            var items=(imageAiConfig.PromptLibrary??new List<string>()).Where(x=>!String.IsNullOrWhiteSpace(x)).ToList();
            foreach(var box in promptLibraryBoxes){box.ItemsSource=null;box.ItemsSource=items.ToList();}
        }

        void SavePromptToLibrary(string prompt)
        {
            prompt=(prompt??"").Trim();
            if(String.IsNullOrEmpty(prompt)){imageStatus.Text="提示词是空的，先写点内容再存入词库";return;}
            var library=imageAiConfig.PromptLibrary;
            library.RemoveAll(x=>String.Equals(x,prompt,StringComparison.Ordinal));
            library.Insert(0,prompt);
            if(library.Count>50)library.RemoveRange(50,library.Count-50);
            SaveImageAiConfig();RefreshPromptLibraryBoxes();
            foreach(var box in promptLibraryBoxes)box.SelectedItem=prompt;
            imageStatus.Text="提示词已存入词库；下次生成时在词库里选中即可直接调用";
        }

        void RemovePromptFromLibrary(string prompt)
        {
            if(String.IsNullOrWhiteSpace(prompt)){imageStatus.Text="请先在词库中选中要删除的提示词";return;}
            imageAiConfig.PromptLibrary.RemoveAll(x=>String.Equals(x,prompt,StringComparison.Ordinal));
            SaveImageAiConfig();RefreshPromptLibraryBoxes();
            imageStatus.Text="已从词库删除该提示词";
        }

        void ShowImageAiSettings()
        {
            if(imageAiSettingsPanel==null)BuildImageAiSettings();
            RestoreShelvedIfNeeded(imageAiSettingsPanel);
            LoadTextProfileSettings(ActiveTextProfile());
            LoadProfileSettings(ActiveImageProfile());
            imageSettingsStatus.Text="文本 Key "+(File.Exists(imageTextKeyFile)?"✓":"○")+" · 当前图像来源 Key "+(!String.IsNullOrWhiteSpace(LoadActiveImageKey())?"✓":"○")+"。可保存多个来源，切换时无需重复填写。";
            // 合规页可独立打开，不能假设 AI 图片窗口已经创建。收纳中的胶囊不能做锚点，否则设置窗会贴着标签定位。
            Window anchor=compliancePanel!=null&&compliancePanel.IsVisible&&!shelvedWindows.ContainsKey(compliancePanel)?compliancePanel:(imageEditorPanel!=null&&imageEditorPanel.IsVisible&&!shelvedWindows.ContainsKey(imageEditorPanel)?imageEditorPanel:null);
            // 该设置页也可从桌宠右键独立打开；隐藏窗口不能做 Owner，否则设置窗会随它被隐藏。
            if(!imageAiSettingsPanel.IsVisible)imageAiSettingsPanel.Owner=anchor;
            if(anchor!=null){imageAiSettingsPanel.Left=anchor.Left+Math.Max(0,(anchor.Width-imageAiSettingsPanel.Width)/2);imageAiSettingsPanel.Top=anchor.Top+45;}
            else {var work=SystemParameters.WorkArea;imageAiSettingsPanel.Left=work.Left+Math.Max(8,(work.Width-imageAiSettingsPanel.Width)/2);imageAiSettingsPanel.Top=work.Top+Math.Max(8,(work.Height-imageAiSettingsPanel.Height)/2);}
            imageAiSettingsPanel.Show();imageAiSettingsPanel.Activate();
        }

        void LoadProfileSettings(ImageProviderProfile profile)
        {
            if(profile==null||imageModelBaseBox==null||imageProfileSettingsLoading)return;imageProfileSettingsLoading=true;
            try{if(imageProfileSettingsCombo!=null){imageProfileSettingsCombo.ItemsSource=null;imageProfileSettingsCombo.ItemsSource=imageAiConfig.ImageProfiles;imageProfileSettingsCombo.SelectedItem=profile;}imageProfileNameBox.Text=profile.Name??"";imageModelBaseBox.Text=profile.BaseUrl??"";imageModelCombo.ItemsSource=null;imageModelCombo.ItemsSource=profile.Models;imageModelCombo.Text=profile.Model??"";imageModelKeyBox.Clear();if(imageProtocolCombo!=null)imageProtocolCombo.SelectedItem=profile.Protocol??"auto";if(imageProfileEditBox!=null)imageProfileEditBox.IsChecked=profile.SupportsEditing;if(imageProfileLayerBox!=null)imageProfileLayerBox.IsChecked=profile.SupportsLayers;if(imageProfileTransparentBox!=null)imageProfileTransparentBox.IsChecked=profile.SupportsTransparency;}finally{imageProfileSettingsLoading=false;}
        }

        void LoadTextProfileSettings(ImageProviderProfile profile)
        {
            if(profile==null||imageTextBaseBox==null||textProfileSettingsLoading)return;textProfileSettingsLoading=true;
            try{if(textProfileSettingsCombo!=null){textProfileSettingsCombo.ItemsSource=null;textProfileSettingsCombo.ItemsSource=imageAiConfig.TextProfiles;textProfileSettingsCombo.SelectedItem=profile;}textProfileNameBox.Text=profile.Name??"";imageTextBaseBox.Text=profile.BaseUrl??"";imageTextModelCombo.ItemsSource=null;imageTextModelCombo.ItemsSource=profile.Models;imageTextModelCombo.Text=profile.Model??"";imageTextKeyBox.Clear();}finally{textProfileSettingsLoading=false;}
        }

        ImageProviderProfile SaveTextProfileSettings(bool switchActive)
        {
            var profile=textProfileSettingsCombo==null?ActiveTextProfile():textProfileSettingsCombo.SelectedItem as ImageProviderProfile;if(profile==null)return null;profile.Name=String.IsNullOrWhiteSpace(textProfileNameBox.Text)?"未命名文本来源":textProfileNameBox.Text.Trim();profile.BaseUrl=(imageTextBaseBox.Text??"").Trim();profile.Model=(imageTextModelCombo.Text??"").Trim();if(profile.Models==null)profile.Models=new List<string>();if(!String.IsNullOrEmpty(profile.Model)&&!profile.Models.Contains(profile.Model))profile.Models.Add(profile.Model);string key=(imageTextKeyBox.Password??"").Trim();if(!String.IsNullOrEmpty(key))SaveProfileKey(profile.Id,key);imageTextKeyBox.Clear();if(switchActive)SelectTextProfile(profile);SaveImageAiConfig();return profile;
        }

        ImageProviderProfile SaveProfileSettings(bool switchActive)
        {
            var profile=imageProfileSettingsCombo==null?ActiveImageProfile():imageProfileSettingsCombo.SelectedItem as ImageProviderProfile;if(profile==null)return null;
            profile.Name=String.IsNullOrWhiteSpace(imageProfileNameBox.Text)?"未命名来源":imageProfileNameBox.Text.Trim();profile.BaseUrl=(imageModelBaseBox.Text??"").Trim();profile.Model=(imageModelCombo.Text??"").Trim();if(profile.Models==null)profile.Models=new List<string>();if(!String.IsNullOrEmpty(profile.Model)&&!profile.Models.Contains(profile.Model))profile.Models.Add(profile.Model);profile.Protocol=Convert.ToString(imageProtocolCombo.SelectedItem)??"auto";profile.SupportsEditing=imageProfileEditBox.IsChecked==true;profile.SupportsLayers=imageProfileLayerBox.IsChecked==true;profile.SupportsTransparency=imageProfileTransparentBox.IsChecked==true;string key=(imageModelKeyBox.Password??"").Trim();if(!String.IsNullOrEmpty(key))SaveProfileKey(profile.Id,key);imageModelKeyBox.Clear();if(switchActive)SelectImageProfile(profile);SaveImageAiConfig();return profile;
        }

        void BuildImageAiSettings()
        {
            imageAiSettingsPanel=new Window{Title="图片 AI 设置",Width=620,Height=760,MinWidth=500,MinHeight=600,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.CanResize,ShowInTaskbar=false,AllowsTransparency=true,Background=Brushes.Transparent,Topmost=true};
            var outer=new Border{CornerRadius=new CornerRadius(18),Padding=new Thickness(22)};
            Ui.StyleCard(outer); Ui.StyleWindow(imageAiSettingsPanel);
            var stack=new StackPanel();var settingsScroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=stack};
            var header=new Grid{Margin=new Thickness(0,0,0,12)};header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var heading=new StackPanel();heading.Children.Add(Ui.Title("文本与图像模型",21));heading.Children.Add(Ui.Subtitle("接口地址、Key 与模型独立配置，保存方式保持不变"));header.Children.Add(heading);var close=Ui.MakeCloseButton();close.Click+=delegate{imageAiSettingsPanel.Hide();};Grid.SetColumn(close,1);header.Children.Add(close);stack.Children.Add(header);AddShelfControl(imageAiSettingsPanel,header,close);EnableWindowInteraction(imageAiSettingsPanel,header);
            stack.Children.Add(new TextBlock{Text="文本来源（识字 / 对话 / 合规）",FontSize=15,FontWeight=FontWeights.Bold,Margin=new Thickness(0,12,0,5)});var textSourcePicker=new Grid();textSourcePicker.ColumnDefinitions.Add(new ColumnDefinition());textSourcePicker.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});textProfileSettingsCombo=new ComboBox{Height=40,DisplayMemberPath="Name",Padding=new Thickness(10,7,10,7)};textProfileSettingsCombo.SelectionChanged+=delegate{if(textProfileSettingsLoading)return;var selected=textProfileSettingsCombo.SelectedItem as ImageProviderProfile;if(selected!=null)LoadTextProfileSettings(selected);};textSourcePicker.Children.Add(textProfileSettingsCombo);var newTextSource=MakeButton("＋ 新来源",Ui.Neutral);newTextSource.Height=40;newTextSource.Click+=delegate{var profile=new ImageProviderProfile{Id=Guid.NewGuid().ToString("N"),Name="新文本来源",Models=new List<string>(),Protocol="auto"};imageAiConfig.TextProfiles.Add(profile);SaveImageAiConfig();LoadTextProfileSettings(profile);};Grid.SetColumn(newTextSource,1);textSourcePicker.Children.Add(newTextSource);stack.Children.Add(textSourcePicker);textProfileNameBox=new TextBox{Height=40,Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,6,0,0),ToolTip="文本来源名称"};stack.Children.Add(textProfileNameBox);imageTextBaseBox=new TextBox{Height=40,Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,5,0,0),ToolTip="文本模型 Base URL，例如 https://toapis.com/v1"};stack.Children.Add(imageTextBaseBox);stack.Children.Add(new TextBlock{Text="API Key（留空沿用此来源已保存值）",FontSize=11,Foreground=Ui.SubInk,Margin=new Thickness(0,4,0,2)});imageTextKeyBox=new PasswordBox{Height=40,Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,0,5)};stack.Children.Add(imageTextKeyBox);
            var textModels=new Grid();textModels.ColumnDefinitions.Add(new ColumnDefinition());textModels.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});imageTextModelCombo=new ComboBox{Height=40,IsEditable=true,IsTextSearchEnabled=true,StaysOpenOnEdit=true,Padding=new Thickness(10,7,10,7)};EnableModelSearch(imageTextModelCombo,delegate{return imageAiConfig.TextModels;});textModels.Children.Add(imageTextModelCombo);var refreshText=MakeButton("刷新模型",new SolidColorBrush(Color.FromRgb(235,229,222)));refreshText.Height=40;refreshText.Click+=delegate{RefreshImageModels(true);};Grid.SetColumn(refreshText,1);textModels.Children.Add(refreshText);stack.Children.Add(textModels);
            stack.Children.Add(new TextBlock{Text="图像来源（生成 / 重绘 / 精确编辑）",FontSize=15,FontWeight=FontWeights.Bold,Margin=new Thickness(0,16,0,5)});
            var sourcePicker=new Grid();sourcePicker.ColumnDefinitions.Add(new ColumnDefinition());sourcePicker.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});imageProfileSettingsCombo=new ComboBox{Height=40,DisplayMemberPath="Name",Padding=new Thickness(10,7,10,7)};imageProfileSettingsCombo.SelectionChanged+=delegate{if(imageProfileSettingsLoading)return;var selected=imageProfileSettingsCombo.SelectedItem as ImageProviderProfile;if(selected!=null)LoadProfileSettings(selected);};sourcePicker.Children.Add(imageProfileSettingsCombo);var newSource=MakeButton("＋ 新来源",Ui.Neutral);newSource.Height=40;newSource.Click+=delegate{var profile=new ImageProviderProfile{Id=Guid.NewGuid().ToString("N"),Name="新图像来源",Models=new List<string>(),Protocol="auto",SupportsEditing=true,SupportsTransparency=true};imageAiConfig.ImageProfiles.Add(profile);SaveImageAiConfig();LoadProfileSettings(profile);};Grid.SetColumn(newSource,1);sourcePicker.Children.Add(newSource);stack.Children.Add(sourcePicker);
            imageProfileNameBox=new TextBox{Height=40,Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,6,0,0),ToolTip="给这个来源起一个自己看得懂的名字，例如 公司网关、ToApis、本地 ComfyUI"};stack.Children.Add(imageProfileNameBox);
            imageModelBaseBox=new TextBox{Height=40,Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,5,0,0),ToolTip="此来源的 Base URL；每个来源只需填写并保存一次"};stack.Children.Add(imageModelBaseBox);stack.Children.Add(new TextBlock{Text="API Key（留空沿用该来源已保存值）",FontSize=11,Foreground=Ui.SubInk,Margin=new Thickness(0,4,0,2)});imageModelKeyBox=new PasswordBox{Height=40,Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,0,5)};stack.Children.Add(imageModelKeyBox);
            var imageModels=new Grid();imageModels.ColumnDefinitions.Add(new ColumnDefinition());imageModels.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});imageModelCombo=new ComboBox{Height=40,IsEditable=true,IsTextSearchEnabled=true,StaysOpenOnEdit=true,Padding=new Thickness(10,7,10,7)};EnableModelSearch(imageModelCombo,delegate{return imageAiConfig.ImageModels;});imageModels.Children.Add(imageModelCombo);var refreshImage=MakeButton("刷新模型",new SolidColorBrush(Color.FromRgb(235,229,222)));refreshImage.Height=40;refreshImage.Click+=delegate{RefreshImageModels(false);};Grid.SetColumn(refreshImage,1);imageModels.Children.Add(refreshImage);stack.Children.Add(imageModels);
            imageProtocolCombo=new ComboBox{Height=34,Padding=new Thickness(9,5,9,5),ItemsSource=new[]{"auto","openai-images","chat-image","ark"},ToolTip="auto：优先标准 Images API，失败后使用聊天图片回退；ark：火山方舟原生图像协议（支持已开启的专属能力）"};stack.Children.Add(imageProtocolCombo);var capabilityRow=new WrapPanel{Margin=new Thickness(1,5,1,2)};imageProfileEditBox=new CheckBox{Content="支持参考图编辑",Margin=new Thickness(0,0,12,0),IsChecked=true};imageProfileLayerBox=new CheckBox{Content="支持原生图层拆分",Margin=new Thickness(0,0,12,0)};imageProfileTransparentBox=new CheckBox{Content="支持透明 PNG",IsChecked=true};capabilityRow.Children.Add(imageProfileEditBox);capabilityRow.Children.Add(imageProfileLayerBox);capabilityRow.Children.Add(imageProfileTransparentBox);stack.Children.Add(capabilityRow);
            imageSettingsStatus=new TextBlock{Text="“自动”会优先尝试最通用的 OpenAI Images 契约，再回退聊天图片返回。只有来源明确支持时，才开启原生图层拆分。",FontSize=11,Foreground=Ui.SubInk,Margin=new Thickness(0,12,0,8),TextWrapping=TextWrapping.Wrap};stack.Children.Add(imageSettingsStatus);var actions=new WrapPanel();var save=MakeButton("保存并使用此来源",new SolidColorBrush(Color.FromRgb(255,126,115)));save.Foreground=Brushes.White;save.Click+=delegate{SaveImageAiSettings();};var remove=MakeButton("移除当前来源",Brushes.Transparent);remove.Click+=delegate{var profile=imageProfileSettingsCombo.SelectedItem as ImageProviderProfile;if(profile!=null&&imageAiConfig.ImageProfiles.Count>1){imageAiConfig.ImageProfiles.Remove(profile);SelectImageProfile(imageAiConfig.ImageProfiles[0]);SaveImageAiConfig();LoadProfileSettings(ActiveImageProfile());}};var delete=MakeButton("删除所有图像 Key",Brushes.Transparent);delete.Click+=delegate{DeleteImageAiKeys();};actions.Children.Add(save);actions.Children.Add(remove);actions.Children.Add(delete);stack.Children.Add(actions);outer.Child=settingsScroll;imageAiSettingsPanel.Content=outer;imageAiSettingsPanel.Closing+=delegate(object s,System.ComponentModel.CancelEventArgs e){if(!exiting){e.Cancel=true;imageAiSettingsPanel.Hide();}};
        }

        byte[] ImageKeyEntropy(string kind){return Encoding.UTF8.GetBytes("MomoPet.ImageAi."+kind+".v1");}

        string LoadImageAiKey(bool text){return LoadImageAiKey(text,false);}

        string LoadImageAiKey(bool text,bool toApis){string file=toApis?(text?toApisTextKeyFile:toApisImageKeyFile):(text?imageTextKeyFile:imageModelKeyFile);string kind=toApis?"ToApis"+(text?"Text":"Image"):(text?"Text":"Image");try{if(!File.Exists(file))return null;return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(file),ImageKeyEntropy(kind),DataProtectionScope.CurrentUser));}catch{return null;}}

        void SaveImageAiKey(bool text,string key,bool toApis){string file=toApis?(text?toApisTextKeyFile:toApisImageKeyFile):(text?imageTextKeyFile:imageModelKeyFile);string kind=toApis?"ToApis"+(text?"Text":"Image"):(text?"Text":"Image");byte[] plain=Encoding.UTF8.GetBytes(key);try{File.WriteAllBytes(file,ProtectedData.Protect(plain,ImageKeyEntropy(kind),DataProtectionScope.CurrentUser));}finally{Array.Clear(plain,0,plain.Length);}}

        void SaveImageAiConfig(){MomoStorage.WriteTextAtomic(imageAiConfigFile,json.Serialize(imageAiConfig),Encoding.UTF8);}

        List<string> SettingsModels(bool text){bool toApis=String.Equals(text?settingsTextProvider:settingsImageProvider,"ToApis",StringComparison.OrdinalIgnoreCase);return text?(toApis?imageAiConfig.ToApisTextModels:imageAiConfig.TextModels):(toApis?imageAiConfig.ToApisImageModels:imageAiConfig.ImageModels);}

        void CaptureSettingsChannel(bool text)
        {
            bool toApis=String.Equals(text?settingsTextProvider:settingsImageProvider,"ToApis",StringComparison.OrdinalIgnoreCase);TextBox baseBox=text?imageTextBaseBox:imageModelBaseBox;ComboBox combo=text?imageTextModelCombo:imageModelCombo;PasswordBox keyBox=text?imageTextKeyBox:imageModelKeyBox;string baseUrl=(baseBox.Text??"").Trim(),model=(combo.Text??"").Trim(),key=(keyBox.Password??"").Trim();if(!String.IsNullOrEmpty(key))SaveImageAiKey(text,key,toApis);keyBox.Clear();
            if(text){if(toApis){imageAiConfig.ToApisTextBaseUrl=baseUrl;imageAiConfig.ToApisTextModel=model;}else{imageAiConfig.TextBaseUrl=baseUrl;imageAiConfig.TextModel=model;}}else{if(toApis){imageAiConfig.ToApisImageBaseUrl=baseUrl;imageAiConfig.ToApisImageModel=model;}else{imageAiConfig.ImageBaseUrl=baseUrl;imageAiConfig.ImageModel=model;}}
            List<string> list=SettingsModels(text);if(!String.IsNullOrEmpty(model)&&!list.Contains(model))list.Add(model);
        }

        void LoadSettingsChannel(bool text)
        {
            bool toApis=String.Equals(text?settingsTextProvider:settingsImageProvider,"ToApis",StringComparison.OrdinalIgnoreCase);TextBox baseBox=text?imageTextBaseBox:imageModelBaseBox;ComboBox combo=text?imageTextModelCombo:imageModelCombo;PasswordBox keyBox=text?imageTextKeyBox:imageModelKeyBox;baseBox.Text=text?(toApis?imageAiConfig.ToApisTextBaseUrl:imageAiConfig.TextBaseUrl):(toApis?imageAiConfig.ToApisImageBaseUrl:imageAiConfig.ImageBaseUrl);combo.ItemsSource=null;combo.ItemsSource=SettingsModels(text);combo.Text=text?(toApis?imageAiConfig.ToApisTextModel:imageAiConfig.TextModel):(toApis?imageAiConfig.ToApisImageModel:imageAiConfig.ImageModel);keyBox.Clear();keyBox.ToolTip=(toApis?"ToApis":"官方")+(text?"文本":"图像")+" Key；留空沿用已保存值";
        }

        void SaveImageAiSettings(){SaveTextProfileSettings(true);SaveProfileSettings(true);imageAiConfig.TextProvider="自动";imageAiConfig.ImageProvider="自动";SaveImageAiConfig();SyncImageModelCombos();string message="文本与图像来源已保存；切换来源不会重复要求填写 Key";if(imageStatus!=null)imageStatus.Text=message;if(pocketChatStatus!=null)pocketChatStatus.Text=message;if(complianceStatus!=null)complianceStatus.Text=message;imageAiSettingsPanel.Hide();}

        void DeleteImageAiKeys(){try{foreach(string file in new[]{imageTextKeyFile,imageModelKeyFile,toApisTextKeyFile,toApisImageKeyFile})if(File.Exists(file))File.Delete(file);foreach(var profile in imageAiConfig.ImageProfiles){string file=ProfileKeyFile(profile.Id);if(File.Exists(file))File.Delete(file);}}catch{}imageStatus.Text="文本与图像来源 Key 已删除";if(imageSettingsStatus!=null)imageSettingsStatus.Text=imageStatus.Text;}

        void SyncImageModelCombos(){if(imageTextModelCombo!=null){imageTextModelCombo.ItemsSource=null;imageTextModelCombo.ItemsSource=ActiveTextModels();imageTextModelCombo.Text=ActiveTextModel()??"";}var profile=ActiveImageProfile();if(imageQuickModelCombo!=null&&profile!=null){imageProfileCombo.ItemsSource=null;imageProfileCombo.ItemsSource=imageAiConfig.ImageProfiles;imageProfileCombo.SelectedItem=profile;imageQuickModelCombo.ItemsSource=null;imageQuickModelCombo.ItemsSource=profile.Models;imageQuickModelCombo.Text=profile.Model??"";}if(pocketChatSourceCombo!=null){pocketChatSourceCombo.ItemsSource=null;pocketChatSourceCombo.ItemsSource=imageAiConfig.TextProfiles;pocketChatSourceCombo.SelectedItem=ActiveTextProfile();}if(pocketChatModelCombo!=null){pocketChatModelCombo.ItemsSource=null;pocketChatModelCombo.ItemsSource=ActiveTextModels();pocketChatModelCombo.Text=ActiveTextModel()??"";}UpdateImageProviderOptions();}

        void RefreshImageModels(bool text)
        {
            string baseUrl=((text?imageTextBaseBox.Text:imageModelBaseBox.Text)??"").Trim();string typed=text?imageTextKeyBox.Password:imageModelKeyBox.Password;string key=String.IsNullOrWhiteSpace(typed)?(text?LoadActiveTextKey():LoadActiveImageKey()):typed.Trim();if(String.IsNullOrEmpty(baseUrl)||String.IsNullOrEmpty(key)){imageStatus.Text="刷新前请填写 Base URL 和 API Key";imageSettingsStatus.Text=imageStatus.Text;return;}bool toApis=UrlUsesToApis(baseUrl);
            string loading="正在刷新"+(text?"文本":"图像")+"模型…";if(imageStatus!=null)imageStatus.Text=loading;imageSettingsStatus.Text="正在请求 "+baseUrl.TrimEnd('/')+"/models …";var request=new Dictionary<string,object>{{"mode","models"},{"base_url",baseUrl},{"key_type",text?"text":"image"},{"provider",toApis?"toapis":"official"}};
            RunImageHelper(request,text?key:null,text?null:key,delegate(Dictionary<string,object> response){if(!ImageResponseOk(response)){imageSettingsStatus.Text=response!=null&&response.ContainsKey("error")?Convert.ToString(response["error"]):"模型刷新失败";return;}var list=ModelNamesFromResponse(response);if(text){var profile=textProfileSettingsCombo.SelectedItem as ImageProviderProfile??ActiveTextProfile();profile.Models=list;profile.BaseUrl=baseUrl;if(list.Count>0&&String.IsNullOrWhiteSpace(profile.Model))profile.Model=list[0];}else{var profile=imageProfileSettingsCombo.SelectedItem as ImageProviderProfile??ActiveImageProfile();profile.Models=list;profile.BaseUrl=baseUrl;if(list.Count>0&&String.IsNullOrWhiteSpace(profile.Model))profile.Model=list[0];}ComboBox combo=text?imageTextModelCombo:imageModelCombo;combo.ItemsSource=null;combo.ItemsSource=list;if(list.Count>0&&String.IsNullOrWhiteSpace(combo.Text)){combo.SelectedIndex=0;combo.Text=list[0];}combo.IsDropDownOpen=list.Count>0;string message="已刷新 "+list.Count+" 个"+(text?"文本":"图像")+"模型，选择后请保存";if(imageStatus!=null)imageStatus.Text=message;imageSettingsStatus.Text=message;});
        }

        byte[] PrecisionKeyEntropy(){return Encoding.UTF8.GetBytes("MomoPet.PrecisionImage.v1");}

        string LoadPrecisionImageKey(){try{return File.Exists(precisionImageKeyFile)?Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(precisionImageKeyFile),PrecisionKeyEntropy(),DataProtectionScope.CurrentUser)):null;}catch{return null;}}

        void SavePrecisionImageKey(string key){byte[] plain=Encoding.UTF8.GetBytes(key);try{File.WriteAllBytes(precisionImageKeyFile,ProtectedData.Protect(plain,PrecisionKeyEntropy(),DataProtectionScope.CurrentUser));}finally{Array.Clear(plain,0,plain.Length);}}

        void ShowPrecisionAiSettings()
        {
            if(precisionAiSettingsPanel==null)BuildPrecisionAiSettings();RestoreShelvedIfNeeded(precisionAiSettingsPanel);precisionBaseBox.Text=imageAiConfig.PrecisionBaseUrl??"";precisionModelCombo.ItemsSource=null;precisionModelCombo.ItemsSource=imageAiConfig.PrecisionModels;precisionModelCombo.Text=imageAiConfig.PrecisionModel??"";precisionKeyBox.Clear();precisionSettingsStatus.Text=File.Exists(precisionImageKeyFile)?"Key 已加密保存；此处的设置只用于精确编辑与图层拆分。":"请填写可调用 doubao-seedream-5-0-pro 的 Base URL 和 API Key。";precisionAiSettingsPanel.Left=imageEditorPanel.Left+Math.Max(0,(imageEditorPanel.Width-precisionAiSettingsPanel.Width)/2);precisionAiSettingsPanel.Top=imageEditorPanel.Top+55;precisionAiSettingsPanel.Show();precisionAiSettingsPanel.Activate();
        }

        void BuildPrecisionAiSettings()
        {
            precisionAiSettingsPanel=new Window{Title="火山方舟精确编辑设置",Width=560,Height=500,MinWidth=460,MinHeight=420,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.CanResize,ShowInTaskbar=false,AllowsTransparency=true,Background=Brushes.Transparent,Topmost=true,Owner=imageEditorPanel};var outer=new Border{CornerRadius=new CornerRadius(18),Padding=new Thickness(22)};Ui.StyleCard(outer);Ui.StyleWindow(precisionAiSettingsPanel);var stack=new StackPanel();var header=new Grid{Margin=new Thickness(0,0,0,10)};header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var heading=new StackPanel();heading.Children.Add(Ui.Title("火山方舟精确编辑",21));heading.Children.Add(Ui.Subtitle("独立配置 Seedream 精确编辑与图层拆分通道"));header.Children.Add(heading);var close=Ui.MakeCloseButton();close.Click+=delegate{precisionAiSettingsPanel.Hide();};Grid.SetColumn(close,1);header.Children.Add(close);stack.Children.Add(header);AddShelfControl(precisionAiSettingsPanel,header,close);EnableWindowInteraction(precisionAiSettingsPanel,header);
            stack.Children.Add(new TextBlock{Text="直连火山方舟图片 API。Base URL 填 https://ark.cn-beijing.volces.com/api/v3；模型可填推理接入点 ID 或 doubao-seedream-5-0-pro-260628。",Foreground=Ui.SubInk,FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,9,0,8)});stack.Children.Add(new TextBlock{Text="Base URL",FontWeight=FontWeights.Bold});precisionBaseBox=new TextBox{Height=42,Padding=new Thickness(12,5,12,5),VerticalContentAlignment=VerticalAlignment.Center,ToolTip="https://ark.cn-beijing.volces.com/api/v3"};stack.Children.Add(precisionBaseBox);stack.Children.Add(new TextBlock{Text="API Key（留空沿用已保存值）",Foreground=Ui.SubInk,FontSize=11,Margin=new Thickness(0,5,0,2)});precisionKeyBox=new PasswordBox{Height=42,Padding=new Thickness(12,5,12,5),VerticalContentAlignment=VerticalAlignment.Center};stack.Children.Add(precisionKeyBox);stack.Children.Add(new TextBlock{Text="模型 / 推理接入点",FontWeight=FontWeights.Bold,Margin=new Thickness(0,10,0,3)});
            var models=new Grid();models.ColumnDefinitions.Add(new ColumnDefinition());models.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});precisionModelCombo=new ComboBox{Height=40,IsEditable=true,IsTextSearchEnabled=true,StaysOpenOnEdit=true,Padding=new Thickness(10,7,10,7)};EnableModelSearch(precisionModelCombo,delegate{return imageAiConfig.PrecisionModels;});models.Children.Add(precisionModelCombo);var refresh=MakeButton("刷新模型",new SolidColorBrush(Color.FromRgb(235,229,222)));refresh.Height=40;refresh.Click+=delegate{RefreshPrecisionModels();};Grid.SetColumn(refresh,1);models.Children.Add(refresh);stack.Children.Add(models);precisionSettingsStatus=new TextBlock{Foreground=Ui.SubInk,FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,8)};stack.Children.Add(precisionSettingsStatus);var actions=new WrapPanel();var save=MakeButton("保存设置",new SolidColorBrush(Color.FromRgb(255,126,115)));save.Foreground=Brushes.White;save.Click+=delegate{SavePrecisionAiSettings();};var remove=MakeButton("删除此 Key",Brushes.Transparent);remove.Click+=delegate{try{if(File.Exists(precisionImageKeyFile))File.Delete(precisionImageKeyFile);}catch{}precisionSettingsStatus.Text="精确编辑 Key 已删除";};actions.Children.Add(save);actions.Children.Add(remove);stack.Children.Add(actions);outer.Child=stack;precisionAiSettingsPanel.Content=outer;precisionAiSettingsPanel.Closing+=delegate(object s,System.ComponentModel.CancelEventArgs e){if(!exiting){e.Cancel=true;precisionAiSettingsPanel.Hide();}};
        }

        void SavePrecisionAiSettings()
        {
            string baseUrl=(precisionBaseBox.Text??"").Trim(),model=(precisionModelCombo.Text??"").Trim(),key=(precisionKeyBox.Password??"").Trim();if(!String.IsNullOrEmpty(key))SavePrecisionImageKey(key);imageAiConfig.PrecisionBaseUrl=baseUrl;imageAiConfig.PrecisionModel=model;if(!String.IsNullOrEmpty(model)&&!imageAiConfig.PrecisionModels.Contains(model))imageAiConfig.PrecisionModels.Add(model);SaveImageAiConfig();if(precisionStatus!=null)precisionStatus.Text="精确编辑设置已保存。";precisionAiSettingsPanel.Hide();
        }

        void RefreshPrecisionModels()
        {
            string baseUrl=(precisionBaseBox.Text??"").Trim(),typed=(precisionKeyBox.Password??"").Trim(),key=String.IsNullOrEmpty(typed)?LoadPrecisionImageKey():typed;if(String.IsNullOrEmpty(baseUrl)||String.IsNullOrEmpty(key)){precisionSettingsStatus.Text="刷新前请填写 Base URL 和 API Key";return;}precisionSettingsStatus.Text="正在刷新模型…";RunImageHelper(new Dictionary<string,object>{{"mode","models"},{"base_url",baseUrl},{"key_type","image"}},null,key,delegate(Dictionary<string,object> response){if(!ImageResponseOk(response)){precisionSettingsStatus.Text=imageStatus.Text;return;}imageAiConfig.PrecisionModels=ModelNamesFromResponse(response);precisionModelCombo.ItemsSource=null;precisionModelCombo.ItemsSource=imageAiConfig.PrecisionModels;if(imageAiConfig.PrecisionModels.Count>0){precisionModelCombo.Text=imageAiConfig.PrecisionModels[0];precisionModelCombo.IsDropDownOpen=true;}precisionSettingsStatus.Text="已刷新 "+imageAiConfig.PrecisionModels.Count+" 个模型，选择后点击保存";});
        }

        bool ValidatePrecisionAi()
        {
            if(String.IsNullOrWhiteSpace(ActiveImageBaseUrl())||String.IsNullOrWhiteSpace(ActiveImageModel())||String.IsNullOrWhiteSpace(LoadActiveImageKey())){imageStatus.Text="请先选择并配置图像来源、API Key 和模型";ShowImageAiSettings();return false;}return true;
        }

        // 方舟的 background: transparent 用于精确编辑时，要求参考 PNG 里本来就有至少一个真实的 Alpha 像素。
        // 提交前在本地拦截，避免用户等到接口返回 400 才知道条件不满足。

        bool ValidateImageAi(bool text){string baseUrl=text?ActiveTextBaseUrl():ActiveImageBaseUrl(),model=text?ActiveTextModel():ActiveImageModel(),key=text?LoadActiveTextKey():LoadActiveImageKey();if(String.IsNullOrEmpty(baseUrl)||String.IsNullOrEmpty(model)||String.IsNullOrEmpty(key)){imageStatus.Text="请先配置"+(text?"文本":"图像")+"来源的 Base URL、API Key 和模型";ShowImageAiSettings();return false;}return true;}
}
}
