using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace MomoPetApp
{
    public class PromptTemplate
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Content { get; set; }
        public string Created { get; set; }
        public override string ToString() { return Name; }
    }

    public class LlmConfig
    {
        public string BaseUrl { get; set; }
        public string Model { get; set; }
        public List<string> Models { get; set; }
    }

    public partial class PetController
    {
        string llmConfigFile, llmKeyFile, templatesFile;
        Window aiSearchPanel, aiSettingsPanel;
        TextBox aiQuestionBox, templateNameBox, llmBaseUrlBox;
        TextBlock aiStatusText;
        RichTextBox aiResultBox;
        TextBlock llmSettingsStatus;
        PasswordBox llmKeyBox;
        ComboBox aiModelCombo, settingsModelCombo, templateCombo;
        CheckBox keepAsTemplate;
        readonly List<PromptTemplate> promptTemplates = new List<PromptTemplate>();
        LlmConfig llmConfig = new LlmConfig { Models = new List<string>() };
        bool aiBusy;
        Process aiProcess;

        void InitializeAiSearchPaths()
        {
            llmConfigFile = Path.Combine(dataDir, "llm-settings.json");
            llmKeyFile = Path.Combine(dataDir, "llm-key.dat");
            templatesFile = Path.Combine(dataDir, "wind-search-templates.json");
        }

        void LoadAiData()
        {
            try {
                if(File.Exists(llmConfigFile)) llmConfig=json.Deserialize<LlmConfig>(File.ReadAllText(llmConfigFile,Encoding.UTF8));
            } catch { llmConfig=null; }
            if(llmConfig==null) llmConfig=new LlmConfig();
            if(llmConfig.Models==null) llmConfig.Models=new List<string>();
            try {
                if(File.Exists(templatesFile)) {
                    var saved=json.Deserialize<List<PromptTemplate>>(File.ReadAllText(templatesFile,Encoding.UTF8));
                    if(saved!=null) promptTemplates.AddRange(saved.Where(x=>x!=null && !String.IsNullOrWhiteSpace(x.Content)));
                }
            } catch { }
        }

        void BuildAiSearchPanel()
        {
            LoadAiData();
            aiSearchPanel=new Window { Title="Wind AI 搜索",Width=720,Height=760,WindowStyle=WindowStyle.None,
                ResizeMode=ResizeMode.CanResize,MinWidth=560,MinHeight=600,ShowInTaskbar=false,
                AllowsTransparency=true,Background=Brushes.Transparent,Topmost=true };
            var outer=new Border { CornerRadius=new CornerRadius(18),Padding=new Thickness(22) };
            Ui.StyleCard(outer); Ui.StyleWindow(aiSearchPanel);
            var grid=new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });

            var header=new Grid { Margin=new Thickness(2,0,0,18) }; header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
            var headStack=new StackPanel();
            headStack.Children.Add(Ui.Title("Wind AI 搜索",22));
            headStack.Children.Add(Ui.Subtitle("用自然语言提问，由 Wind MCP 返回可核验的金融数据"));
            header.Children.Add(headStack);
            var close=Ui.MakeCloseButton(); close.Click+=delegate { aiSearchPanel.Hide(); };
            Grid.SetColumn(close,1); header.Children.Add(close); Grid.SetRow(header,0); grid.Children.Add(header);AddShelfControl(aiSearchPanel,header,close);EnableWindowInteraction(aiSearchPanel,header);

            var modelRow=new Grid();
            modelRow.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto }); modelRow.ColumnDefinitions.Add(new ColumnDefinition()); modelRow.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
            modelRow.Children.Add(new TextBlock { Text="模型",VerticalAlignment=VerticalAlignment.Center,FontWeight=FontWeights.Bold,Margin=new Thickness(0,0,8,0) });
            aiModelCombo=new ComboBox { IsEditable=true,IsTextSearchEnabled=true,StaysOpenOnEdit=true,Height=34,Padding=new Thickness(8,4,8,4),ItemsSource=llmConfig.Models };
            aiModelCombo.Text=llmConfig.Model??"";EnableModelSearch(aiModelCombo,delegate{return llmConfig.Models;}); Grid.SetColumn(aiModelCombo,1); modelRow.Children.Add(aiModelCombo);
            var settings=MakeButton("⚙ 设置",new SolidColorBrush(Color.FromRgb(235,229,222))); settings.Height=34; settings.Margin=new Thickness(8,0,0,0); settings.Click+=delegate { ShowAiSettings(); };
            Grid.SetColumn(settings,2); modelRow.Children.Add(settings); var modelCard=Ui.SurfacePanel(modelRow,new Thickness(14,12,14,12),new Thickness(0,0,0,12));Grid.SetRow(modelCard,1); grid.Children.Add(modelCard);

            aiQuestionBox=new TextBox { Height=104,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
                FontSize=14,Padding=new Thickness(12),VerticalContentAlignment=VerticalAlignment.Top,ToolTip="直接输入自然语言问题，Ctrl+Enter 搜索" };
            aiQuestionBox.PreviewKeyDown+=delegate(object s,System.Windows.Input.KeyEventArgs e) { if(e.Key==System.Windows.Input.Key.Enter && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control)!=0) { StartAiSearch(); e.Handled=true; } };
            var questionArea=new StackPanel();questionArea.Children.Add(Ui.SectionLabel("你想查询什么"));aiQuestionBox.Margin=new Thickness(0,7,0,0);questionArea.Children.Add(aiQuestionBox);var questionCard=Ui.SurfacePanel(questionArea,new Thickness(14,12,14,14),new Thickness(0,0,0,0));Grid.SetRow(questionCard,2); grid.Children.Add(questionCard);

            var actionRow=new WrapPanel { Margin=new Thickness(0,10,0,12) };
            var search=MakeButton("搜索 Wind",new SolidColorBrush(Color.FromRgb(255,126,115))); search.Foreground=Brushes.White; search.Click+=delegate { StartAiSearch(); };
            keepAsTemplate=new CheckBox { Content="同时保存为模板",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(10,0,0,0) };
            actionRow.Children.Add(search); actionRow.Children.Add(keepAsTemplate); Grid.SetRow(actionRow,3); grid.Children.Add(actionRow);

            var templateArea=new StackPanel();templateArea.Children.Add(Ui.SectionLabel("常用查询模板"));
            var templateRow=new Grid(); templateRow.ColumnDefinitions.Add(new ColumnDefinition()); templateRow.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto }); templateRow.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
            templateCombo=new ComboBox { Height=36,Margin=new Thickness(0,7,0,0),Padding=new Thickness(8,4,8,4),ItemsSource=promptTemplates,ToolTip="选择已经保存的调用语言模板" };
            var apply=MakeButton("套用",new SolidColorBrush(Color.FromRgb(235,229,222))); apply.Height=33; apply.Click+=delegate { ApplySelectedTemplate(); }; Grid.SetColumn(apply,1);
            var remove=MakeButton("删除",Brushes.Transparent); remove.Height=33; remove.Click+=delegate { DeleteSelectedTemplate(); }; Grid.SetColumn(remove,2);
            templateRow.Children.Add(templateCombo); templateRow.Children.Add(apply); templateRow.Children.Add(remove); templateArea.Children.Add(templateRow);
            var saveRow=new Grid { Margin=new Thickness(0,6,0,0) }; saveRow.ColumnDefinitions.Add(new ColumnDefinition()); saveRow.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
            templateNameBox=new TextBox { Height=31,Padding=new Thickness(8,4,8,4),ToolTip="模板名称（可留空，自动取问题开头）" };
            var saveTemplate=MakeButton("保存当前语句",new SolidColorBrush(Color.FromRgb(245,239,233))); saveTemplate.Height=31; saveTemplate.Click+=delegate { AddCurrentTemplate(); }; Grid.SetColumn(saveTemplate,1);
            saveRow.Children.Add(templateNameBox); saveRow.Children.Add(saveTemplate); templateArea.Children.Add(saveRow);var templateCard=Ui.SurfacePanel(templateArea,new Thickness(14,11,14,13),new Thickness(0,0,0,12));Grid.SetRow(templateCard,4); grid.Children.Add(templateCard);

            var resultGrid=new Grid(); resultGrid.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto }); resultGrid.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
            aiStatusText=new TextBlock { Text="准备就绪 · 输入问题后开始搜索",Foreground=Ui.SubInk,FontSize=12.5,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(3,0,0,8) };
            aiResultBox=new RichTextBox { IsReadOnly=true,IsReadOnlyCaretVisible=false,
                VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,
                Padding=new Thickness(18),FontSize=13.5,BorderBrush=Ui.Line,BorderThickness=new Thickness(1),Background=Ui.Card,Foreground=Ui.Ink,
                ToolTip="结果可框选，按 Ctrl+C 或右键复制" };
            aiResultBox.Document=new FlowDocument { PagePadding=new Thickness(0),FontFamily=new FontFamily("Microsoft YaHei UI"),FontSize=13.5,Foreground=Ui.Ink,LineHeight=23 };
            resultGrid.Children.Add(aiStatusText); Grid.SetRow(aiResultBox,1); resultGrid.Children.Add(aiResultBox); Grid.SetRow(resultGrid,5); grid.Children.Add(resultGrid);
            outer.Child=grid; aiSearchPanel.Content=outer;
            aiSearchPanel.Closing+=delegate(object s,System.ComponentModel.CancelEventArgs e) { if(!exiting){e.Cancel=true;aiSearchPanel.Hide();} };
        }

        void BuildAiSettingsPanel()
        {
            aiSettingsPanel=new Window { Title="大模型设置",Width=520,Height=450,MinWidth=440,MinHeight=400,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.CanResize,
                ShowInTaskbar=false,AllowsTransparency=true,Background=Brushes.Transparent,Topmost=true };
            var outer=new Border { CornerRadius=new CornerRadius(18),Padding=new Thickness(22) };
            Ui.StyleCard(outer); Ui.StyleWindow(aiSettingsPanel);
            var stack=new StackPanel();
            var header=new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
            header.Children.Add(Ui.Title("大模型设置",21));
            var close=Ui.MakeCloseButton(); close.Click+=delegate { aiSettingsPanel.Hide(); }; Grid.SetColumn(close,1); header.Children.Add(close); stack.Children.Add(header);AddShelfControl(aiSettingsPanel,header,close);EnableWindowInteraction(aiSettingsPanel,header);
            var settingsHint=Ui.Subtitle("支持 OpenAI 兼容接口 · Base URL 与模型完全由你指定"); settingsHint.Margin=new Thickness(0,3,0,18); stack.Children.Add(settingsHint);
            stack.Children.Add(new TextBlock { Text="Base URL",FontWeight=FontWeights.Bold });
            llmBaseUrlBox=new TextBox { Height=36,Padding=new Thickness(9,6,9,6),Text=llmConfig.BaseUrl??"",Margin=new Thickness(0,5,0,10),ToolTip="例如 https://api.openai.com/v1" }; stack.Children.Add(llmBaseUrlBox);
            stack.Children.Add(new TextBlock { Text="API Key",FontWeight=FontWeights.Bold });
            llmKeyBox=new PasswordBox { Height=36,Padding=new Thickness(9,6,9,6),Margin=new Thickness(0,5,0,4),ToolTip="留空表示不修改已保存的 Key" }; stack.Children.Add(llmKeyBox);
            llmSettingsStatus=new TextBlock { Text=File.Exists(llmKeyFile)?"Key 已加密保存（留空表示不修改）":"尚未保存 Key；可先用当前输入刷新模型",FontSize=11,Foreground=Ui.SubInk,Margin=new Thickness(0,0,0,10),TextWrapping=TextWrapping.Wrap };stack.Children.Add(llmSettingsStatus);
            stack.Children.Add(new TextBlock { Text="模型",FontWeight=FontWeights.Bold });
            var modelRow=new Grid { Margin=new Thickness(0,5,0,10) }; modelRow.ColumnDefinitions.Add(new ColumnDefinition()); modelRow.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
            settingsModelCombo=new ComboBox { Height=36,Padding=new Thickness(9,5,9,5),IsEditable=true,IsTextSearchEnabled=true,StaysOpenOnEdit=true,ItemsSource=llmConfig.Models };
            settingsModelCombo.Text=llmConfig.Model??"";EnableModelSearch(settingsModelCombo,delegate{return llmConfig.Models;}); modelRow.Children.Add(settingsModelCombo);
            var refresh=MakeButton("刷新模型",new SolidColorBrush(Color.FromRgb(235,229,222))); refresh.Height=36; refresh.Click+=delegate { RefreshModels(); }; Grid.SetColumn(refresh,1); modelRow.Children.Add(refresh); stack.Children.Add(modelRow);
            var actions=new WrapPanel(); var save=MakeButton("保存设置",new SolidColorBrush(Color.FromRgb(255,126,115))); save.Foreground=Brushes.White; save.Click+=delegate { SaveLlmSettings(); };
            var delete=MakeButton("删除模型 Key",Brushes.Transparent); delete.Click+=delegate { DeleteLlmKey(); }; actions.Children.Add(save); actions.Children.Add(delete); stack.Children.Add(actions);
            outer.Child=stack; aiSettingsPanel.Content=outer;
            aiSettingsPanel.Closing+=delegate(object s,System.ComponentModel.CancelEventArgs e) { if(!exiting){e.Cancel=true;aiSettingsPanel.Hide();} };
        }

        void ToggleAiSearchPanel()
        {
            if(aiSearchPanel==null)BuildAiSearchPanel();
            if(RestoreShelvedIfNeeded(aiSearchPanel))return;
            if(aiSearchPanel.IsVisible) aiSearchPanel.Hide();
            else { PositionAiPanel(); aiSearchPanel.Show(); aiSearchPanel.Activate(); }
        }

        void PositionAiPanel()
        {
            if(manuallyPlacedWindows.Contains(aiSearchPanel)||shelvedWindows.ContainsKey(aiSearchPanel)||aiSearchPanel.WindowState!=WindowState.Normal)return;
            var work=SystemParameters.WorkArea; double left=pet.Left-aiSearchPanel.Width-12;
            if(left<work.Left) left=pet.Left+pet.Width+12;
            aiSearchPanel.Left=Math.Max(work.Left+8,Math.Min(left,work.Right-aiSearchPanel.Width-8));
            aiSearchPanel.Top=Math.Max(work.Top+8,Math.Min(pet.Top,work.Bottom-aiSearchPanel.Height-8));
        }

        void ShowAiSettings()
        {
            if(aiSearchPanel==null)BuildAiSearchPanel();
            if(aiSettingsPanel==null) BuildAiSettingsPanel();
            RestoreShelvedIfNeeded(aiSettingsPanel);
            llmBaseUrlBox.Text=llmConfig.BaseUrl??""; settingsModelCombo.ItemsSource=null; settingsModelCombo.ItemsSource=llmConfig.Models; settingsModelCombo.Text=llmConfig.Model??"";
            if(aiSearchPanel.IsVisible&&!shelvedWindows.ContainsKey(aiSearchPanel))
            {
                aiSettingsPanel.Left=aiSearchPanel.Left+Math.Max(0,(aiSearchPanel.Width-aiSettingsPanel.Width)/2);
                aiSettingsPanel.Top=aiSearchPanel.Top+60;
            }
            else
            {
                var work=SystemParameters.WorkArea;
                aiSettingsPanel.Left=work.Left+Math.Max(8,(work.Width-aiSettingsPanel.Width)/2);
                aiSettingsPanel.Top=work.Top+Math.Max(8,(work.Height-aiSettingsPanel.Height)/2);
            }
            aiSettingsPanel.Show(); aiSettingsPanel.Activate();
        }

        byte[] LlmEntropy() { return Encoding.UTF8.GetBytes("MomoPet.Llm.ApiKey.v1"); }

        string LoadLlmKey()
        {
            try { if(!File.Exists(llmKeyFile)) return null; return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(llmKeyFile),LlmEntropy(),DataProtectionScope.CurrentUser)); }
            catch { return null; }
        }

        void SaveProtectedLlmKey(string key)
        {
            byte[] plain=Encoding.UTF8.GetBytes(key); try { File.WriteAllBytes(llmKeyFile,ProtectedData.Protect(plain,LlmEntropy(),DataProtectionScope.CurrentUser)); }
            finally { Array.Clear(plain,0,plain.Length); }
        }

        void SaveLlmConfig()
        {
            File.WriteAllText(llmConfigFile,json.Serialize(llmConfig),Encoding.UTF8);
        }

        void SaveLlmSettings()
        {
            string baseUrl=(llmBaseUrlBox.Text??"").Trim(); string model=(settingsModelCombo.Text??"").Trim(); string key=(llmKeyBox.Password??"").Trim();
            if(String.IsNullOrEmpty(baseUrl)) { aiStatusText.Text="请填写 Base URL"; return; }
            if(!String.IsNullOrEmpty(key)) { SaveProtectedLlmKey(key); llmKeyBox.Clear(); }
            if(!File.Exists(llmKeyFile)) { aiStatusText.Text="请填写大模型 API Key"; return; }
            llmConfig.BaseUrl=baseUrl; llmConfig.Model=model;
            if(!String.IsNullOrEmpty(model) && !llmConfig.Models.Contains(model)) llmConfig.Models.Add(model);
            SaveLlmConfig(); SyncModelCombos(); aiStatusText.Text="大模型设置已保存"; aiSettingsPanel.Hide();
        }

        void DeleteLlmKey()
        {
            try { if(File.Exists(llmKeyFile)) File.Delete(llmKeyFile); } catch { }
            llmKeyBox.Clear(); aiStatusText.Text="大模型 Key 已删除";
        }

        void SyncModelCombos()
        {
            aiModelCombo.ItemsSource=null; aiModelCombo.ItemsSource=llmConfig.Models; aiModelCombo.Text=llmConfig.Model??"";
            if(settingsModelCombo!=null) { settingsModelCombo.ItemsSource=null; settingsModelCombo.ItemsSource=llmConfig.Models; settingsModelCombo.Text=llmConfig.Model??""; }
        }

        void RefreshModels()
        {
            string baseUrl=(llmBaseUrlBox.Text??"").Trim(); string key=(llmKeyBox.Password??"").Trim(); if(String.IsNullOrEmpty(key)) key=LoadLlmKey();
            if(String.IsNullOrEmpty(baseUrl) || String.IsNullOrEmpty(key)) { aiStatusText.Text="刷新模型前请填写 Base URL 和 API Key";llmSettingsStatus.Text=aiStatusText.Text; return; }
            aiStatusText.Text="正在刷新可用模型…";llmSettingsStatus.Text="正在请求 "+baseUrl.TrimEnd('/')+"/models …";
            var request=new Dictionary<string,object>(); request["mode"]="models"; request["base_url"]=baseUrl;
            RunAiHelper(request,key,null,delegate(Dictionary<string,object> response) {
                if(!ResponseOk(response)){llmSettingsStatus.Text=aiStatusText.Text;return;}
                llmConfig.Models=ModelNamesFromResponse(response);
                llmConfig.BaseUrl=baseUrl; if(!String.IsNullOrEmpty((settingsModelCombo.Text??"").Trim())) llmConfig.Model=settingsModelCombo.Text.Trim();
                SyncModelCombos();if(llmConfig.Models.Count>0&&String.IsNullOrWhiteSpace(settingsModelCombo.Text)){settingsModelCombo.SelectedIndex=0;settingsModelCombo.Text=llmConfig.Models[0];}settingsModelCombo.IsDropDownOpen=llmConfig.Models.Count>0;aiStatusText.Text="已刷新 "+llmConfig.Models.Count+" 个模型；请选择后保存";llmSettingsStatus.Text=aiStatusText.Text;
            });
        }

        void StartAiSearch()
        {
            if(aiBusy) { aiStatusText.Text="上一条搜索仍在运行，请稍候"; return; }
            string question=(aiQuestionBox.Text??"").Trim(); string model=(aiModelCombo.Text??"").Trim(); string llmKey=LoadLlmKey(); string windKey=LoadWindKey();
            if(String.IsNullOrEmpty(question)) { aiStatusText.Text="请先输入问题"; return; }
            if(String.IsNullOrEmpty(llmConfig.BaseUrl) || String.IsNullOrEmpty(model) || String.IsNullOrEmpty(llmKey)) { aiStatusText.Text="请先在设置中配置 Base URL、模型 Key 和模型"; ShowAiSettings(); return; }
            if(String.IsNullOrEmpty(windKey)) { aiStatusText.Text="请先在“大盘监控”中配置 Wind API Key"; return; }
            llmConfig.Model=model; if(!llmConfig.Models.Contains(model)) llmConfig.Models.Add(model); SaveLlmConfig();
            if(keepAsTemplate.IsChecked==true) AddTemplate(question,(templateNameBox.Text??"").Trim(),false);
            aiBusy=true; SetAiResult(""); aiStatusText.Text="AI 正在理解问题并调用 Wind；复杂研究可能需要数分钟，请勿重复提交…";
            var request=new Dictionary<string,object>(); request["mode"]="search"; request["base_url"]=llmConfig.BaseUrl; request["model"]=model; request["question"]=question;
            RunAiHelper(request,llmKey,windKey,delegate(Dictionary<string,object> response) {
                aiBusy=false; if(!ResponseOk(response)) return;
                SetAiResult(Convert.ToString(response["result"])); string source=response.ContainsKey("source")?Convert.ToString(response["source"]):"Wind";
                aiStatusText.Text="完成 · "+source+" · 结果可选中复制"; React("Wind 搜到结果啦～",true);
            });
        }

        bool ResponseOk(Dictionary<string,object> response)
        {
            bool ok=response!=null && response.ContainsKey("ok") && Convert.ToBoolean(response["ok"]);
            if(!ok) { string error=response!=null && response.ContainsKey("error")?Convert.ToString(response["error"]):"未知错误"; aiStatusText.Text=error; SetAiResult("搜索失败："+error); aiBusy=false; }
            return ok;
        }

        List<string> MarkdownTableCells(string line)
        {
            string value=(line??"").Trim();if(value.StartsWith("|"))value=value.Substring(1);if(value.EndsWith("|"))value=value.Substring(0,value.Length-1);
            return value.Split('|').Select(x=>x.Trim()).ToList();
        }

        bool IsMarkdownSeparator(string line,int expectedColumns)
        {
            var cells=MarkdownTableCells(line);if(cells.Count!=expectedColumns||cells.Count<2)return false;
            foreach(string cell in cells){string marker=cell.Trim().Trim(':');if(marker.Length<3||marker.Any(ch=>ch!='-'))return false;}return true;
        }

        bool StartsMarkdownTable(string[] lines,int index)
        {
            if(index<0||index+1>=lines.Length||lines[index].IndexOf('|')<0)return false;var cells=MarkdownTableCells(lines[index]);return cells.Count>=2&&IsMarkdownSeparator(lines[index+1],cells.Count);
        }

        Brush AiResultValueBrush(string value)
        {
            string text=(value??"").Trim();if(text.IndexOf('%')<0)return Ui.Ink;double number;string cleaned=text.Replace("%","").Replace(",","").Trim();return Double.TryParse(cleaned,out number)?(number>0?Ui.Up:(number<0?Ui.Down:Ui.SubInk)):Ui.Ink;
        }

        TableCell AiTableCell(string value,bool header,int column,int columnCount)
        {
            Brush foreground=header?Ui.Ink:AiResultValueBrush(value);var run=new Run(value??"") { Foreground=foreground,FontWeight=header?FontWeights.Bold:(foreground==Ui.Ink?FontWeights.Normal:FontWeights.SemiBold) };
            var paragraph=new Paragraph(run) { Margin=new Thickness(0),TextAlignment=column==0?TextAlignment.Left:TextAlignment.Right };
            return new TableCell(paragraph) { Padding=new Thickness(10,8,10,8),BorderBrush=Ui.Line,BorderThickness=new Thickness(0,0,column==columnCount-1?0:1,1) };
        }

        void AddAiMarkdownTable(FlowDocument document,List<string> headers,List<List<string>> rows)
        {
            var table=new Table { CellSpacing=0,Margin=new Thickness(0,8,0,16),BorderBrush=Ui.Line,BorderThickness=new Thickness(1),Background=Ui.Card };
            for(int i=0;i<headers.Count;i++)table.Columns.Add(new TableColumn { Width=new GridLength(1,GridUnitType.Star) });
            var group=new TableRowGroup();var headerRow=new TableRow { Background=Ui.AccentSoft };for(int i=0;i<headers.Count;i++)headerRow.Cells.Add(AiTableCell(headers[i],true,i,headers.Count));group.Rows.Add(headerRow);
            for(int rowIndex=0;rowIndex<rows.Count;rowIndex++){
                var row=new TableRow { Background=rowIndex%2==0?Ui.Card:Ui.Inner };List<string> values=rows[rowIndex];
                for(int column=0;column<headers.Count;column++)row.Cells.Add(AiTableCell(column<values.Count?values[column]:"",false,column,headers.Count));group.Rows.Add(row);
            }
            table.RowGroups.Add(group);document.Blocks.Add(table);
        }

        void AddAiInlineFormatting(Paragraph paragraph,string value)
        {
            string text=value??"";int cursor=0;
            foreach(Match match in Regex.Matches(text,@"(?:\*\*|__)(.+?)(?:\*\*|__)|`([^`]+)`"))
            {
                if(match.Index>cursor)paragraph.Inlines.Add(new Run(text.Substring(cursor,match.Index-cursor)));
                string bold=match.Groups[1].Success?match.Groups[1].Value:null,code=match.Groups[2].Success?match.Groups[2].Value:null;
                if(bold!=null)paragraph.Inlines.Add(new Run(bold){FontWeight=FontWeights.SemiBold,Foreground=Ui.Ink});
                else paragraph.Inlines.Add(new Run(code??""){FontFamily=new FontFamily("Consolas"),Background=Ui.Neutral,Foreground=Ui.AccentDeep});
                cursor=match.Index+match.Length;
            }
            if(cursor<text.Length)paragraph.Inlines.Add(new Run(text.Substring(cursor)));
        }

        void SetAiResult(string value)
        {
            if(aiResultBox==null)return;string normalized=(value??"").Replace("\r\n","\n").Replace('\r','\n');string[] lines=normalized.Split('\n');
            var document=new FlowDocument { PagePadding=new Thickness(1),FontFamily=new FontFamily("Microsoft YaHei UI"),FontSize=13.5,Foreground=Ui.Ink,LineHeight=23 };
            for(int i=0;i<lines.Length;){
                if(StartsMarkdownTable(lines,i)){
                    List<string> headers=MarkdownTableCells(lines[i]);var rows=new List<List<string>>();i+=2;
                    while(i<lines.Length&&!String.IsNullOrWhiteSpace(lines[i])&&lines[i].IndexOf('|')>=0){List<string> cells=MarkdownTableCells(lines[i]);if(cells.Count>=2)rows.Add(cells);i++;}
                    AddAiMarkdownTable(document,headers,rows);continue;
                }
                string line=lines[i++].Trim();if(String.IsNullOrEmpty(line))continue;int heading=0;while(heading<line.Length&&line[heading]=='#')heading++;if(heading>0)line=line.Substring(heading).Trim();
                bool isHeading=heading>0;Match bullet=Regex.Match(line,@"^[-*+]\s+(.+)$"),number=Regex.Match(line,@"^(\d+)[\.、]\s*(.+)$");string prefix="";
                if(bullet.Success){line=bullet.Groups[1].Value;prefix="•  ";}else if(number.Success){line=number.Groups[2].Value;prefix=number.Groups[1].Value+".  ";}
                bool sourceLine=Regex.IsMatch(line,@"^(数据来源|来源|数据口径|统计口径|截至日期|注)[：:]");
                var paragraph=new Paragraph{Margin=new Thickness(isHeading?0:2,isHeading?10:0,2,isHeading?8:9),FontSize=isHeading?(heading<=1?18:15.5):(sourceLine?12.5:13.5),FontWeight=isHeading?FontWeights.Bold:FontWeights.Normal,Foreground=sourceLine?Ui.SubInk:Ui.Ink};
                if(sourceLine){paragraph.Background=Ui.Inner;paragraph.Padding=new Thickness(10,7,10,7);paragraph.BorderBrush=Ui.Line;paragraph.BorderThickness=new Thickness(1);}
                if(!String.IsNullOrEmpty(prefix))paragraph.Inlines.Add(new Run(prefix){Foreground=Ui.AccentDeep,FontWeight=FontWeights.Bold});AddAiInlineFormatting(paragraph,line);document.Blocks.Add(paragraph);
            }
            if(document.Blocks.Count==0)document.Blocks.Add(new Paragraph(new Run("结果会显示在这里")){Foreground=Ui.SubInk,Margin=new Thickness(2)});
            aiResultBox.Document=document;
        }

        void RunAiHelper(Dictionary<string,object> request,string llmKey,string windKey,Action<Dictionary<string,object>> completed)
        {
            string helper=EmbeddedRuntime.ResolveFile(Path.Combine("wind_bridge","wind_ai_search.mjs"),root); string requestPath=Path.Combine(dataDir,"ai-request-"+Guid.NewGuid().ToString("N")+".json");
            ThreadPool.QueueUserWorkItem(delegate {
                try {
                    File.WriteAllText(requestPath,json.Serialize(request),new UTF8Encoding(false));
                    var psi=new ProcessStartInfo { FileName="node",WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,
                        RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,
                        Arguments="\""+helper+"\" \""+requestPath+"\"" };
                    psi.EnvironmentVariables["LLM_API_KEY"]=llmKey??""; psi.EnvironmentVariables["WIND_API_KEY"]=windKey??"";
                    string output,errors; int code;
                    using(var process=Process.Start(psi)) { aiProcess=process; output=process.StandardOutput.ReadToEnd(); errors=process.StandardError.ReadToEnd(); process.WaitForExit(); code=process.ExitCode; }
                    aiProcess=null;
                    Dictionary<string,object> parsed=null; try { parsed=json.Deserialize<Dictionary<string,object>>(output); } catch { }
                    if(parsed==null) parsed=new Dictionary<string,object> { {"ok",false},{"error",String.IsNullOrWhiteSpace(errors)?"AI 搜索返回无法解析":errors.Trim()} };
                    app.Dispatcher.BeginInvoke(new Action(delegate { completed(parsed); }));
                } catch(Exception ex) { app.Dispatcher.BeginInvoke(new Action(delegate { completed(new Dictionary<string,object>{{"ok",false},{"error",ex.Message}}); })); }
                finally { llmKey=null; windKey=null; try { if(File.Exists(requestPath)) File.Delete(requestPath); } catch { } }
            });
        }

        void AddCurrentTemplate()
        {
            string content=(aiQuestionBox.Text??"").Trim(); if(String.IsNullOrEmpty(content)) { aiStatusText.Text="没有可保存的语句"; return; }
            AddTemplate(content,(templateNameBox.Text??"").Trim(),true);
        }

        void AddTemplate(string content,string name,bool announce)
        {
            if(promptTemplates.Any(x=>x.Content==content)) { if(announce) aiStatusText.Text="这条模板已经保存过了"; return; }
            if(String.IsNullOrEmpty(name)) { name=content.Replace("\r"," ").Replace("\n"," ").Trim(); if(name.Length>26) name=name.Substring(0,26)+"…"; }
            string baseName=name; int suffix=2; while(promptTemplates.Any(x=>x.Name==name)) name=baseName+" ("+(suffix++)+")";
            promptTemplates.Insert(0,new PromptTemplate { Id=Guid.NewGuid().ToString("N"),Name=name,Content=content,Created=DateTime.Now.ToString("o") });
            SaveTemplates(); RefreshTemplateCombo(); if(announce) aiStatusText.Text="已保存模板：“"+name+"”";
        }

        void SaveTemplates() { try { File.WriteAllText(templatesFile,json.Serialize(promptTemplates),Encoding.UTF8); } catch { } }
        void RefreshTemplateCombo() { templateCombo.ItemsSource=null; templateCombo.ItemsSource=promptTemplates; if(promptTemplates.Count>0) templateCombo.SelectedIndex=0; }
        void ApplySelectedTemplate() { var item=templateCombo.SelectedItem as PromptTemplate; if(item!=null) { aiQuestionBox.Text=item.Content; templateNameBox.Text=item.Name; aiQuestionBox.Focus(); } }
        void DeleteSelectedTemplate() { var item=templateCombo.SelectedItem as PromptTemplate; if(item==null) return; promptTemplates.Remove(item); SaveTemplates(); RefreshTemplateCombo(); aiStatusText.Text="模板已删除"; }

        void SetAiTopmost(bool value) { if(aiSearchPanel!=null) aiSearchPanel.Topmost=value; if(aiSettingsPanel!=null) aiSettingsPanel.Topmost=value; SetImageEditorTopmost(value); }
        void CloseAiWindows()
        {
            try { if(aiProcess!=null && !aiProcess.HasExited) aiProcess.Kill(); } catch { }
            CloseImageEditorWindows();
            if(aiSettingsPanel!=null) aiSettingsPanel.Close(); if(aiSearchPanel!=null) aiSearchPanel.Close();
        }
    }
}
