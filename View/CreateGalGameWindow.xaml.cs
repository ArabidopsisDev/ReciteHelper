using AquaAvgFramework.StoryLineComponents;
using Microsoft.Win32;
using ReciteHelper.Model;
using ReciteHelper.Utils;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace ReciteHelper.View;

public partial class CreateGalGameWindow : Window, INotifyPropertyChanged
{
    public CreateGalGameWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    public string SelectedFilePath
    {
        get => field;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
            }
        }
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var openFileDialog = new OpenFileDialog
        {
            Filter = "复习项目文件 (*.rhproj)|*.rhproj|所有文件 (*.*)|*.*",
            Title = "选择复习项目文件",
            Multiselect = false,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            CheckFileExists = true,
            CheckPathExists = true,
        };

        if (openFileDialog.ShowDialog() == true)
        {
            SelectedFilePath = openFileDialog.FileName;

            if (!Path.GetExtension(SelectedFilePath).Equals(".rhproj", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("请选择 .rhproj 格式的文件", "文件格式错误",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                SelectedFilePath = null;
            }
        }
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SelectedFilePath))
        {
            MessageBox.Show("请先选择复习项目文件", "未选择文件",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!File.Exists(SelectedFilePath))
        {
            MessageBox.Show("选择的文件不存在，请重新选择", "文件不存在",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var result = MessageBox.Show(
            $"确定要使用 {Path.GetFileName(SelectedFilePath)} 创建 GalGame 吗？\n\n" +
            "此过程可能需要一些时间，请耐心等待...",
            "确认创建",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            var path = SelectedFilePath;
            var text = File.ReadAllText(path);
            var project = JsonSerializer.Deserialize<Project>(text)!;

            var chapterQuestions = new Dictionary<string, StringBuilder>();
            var chapterNames = new StringBuilder();

            foreach (var chapter in project.Chapters!)
            {
                var singleChapter = new StringBuilder();

                foreach (var question in chapter.Questions!)
                {
                    singleChapter.AppendLine($"问题：{question.Text} 答案：{question.CorrectAnswer}");
                }

                chapterNames.AppendLine(chapter.Name);
                chapterQuestions.Add(chapter.Name!, singleChapter);
            }

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var clusterPrompt = File.ReadAllText(Path.Combine(baseDir, "Images", "Prompts", "ReClustering.txt"));
            var agent = CreateProjectWindow.BuildAgent("You are a writer who excels at creating moving and touching screenplays.");

            var clusterRespose = await agent.Run($"{clusterPrompt}\n{chapterNames}");
            var clusterJson = clusterRespose.Messages.Last().Content!.Replace("`", "").Replace("json", "").Trim();
            var clusterResult = JsonSerializer.Deserialize<List<ChapterCluster>>(clusterJson);

            chapterNames.Clear();
            clusterResult!.ForEach(x => chapterNames.Append($"{x.UnifiedName}/"));

            var prompt = File.ReadAllText(Path.Combine(baseDir, "Images", "Prompts", "GenerateOutline.txt"));
            var response = await agent.Run($"{prompt}\n{chapterNames}");
            var jsonString = response.Messages.Last().Content!.Replace("`", "").Replace("json", "").Trim();
            var chapterList = JsonSerializer.Deserialize<List<GameChapter>>(jsonString)!;

            var galPrompt = File.ReadAllText(Path.Combine(baseDir, "Images", "Prompts", "GenerateGal.txt"));
            var combined = chapterList.Zip(clusterResult!, (first, second) => (first, second));

            var storyLines = new ConcurrentBag<StoryLine>();

            // This code is highly likely to run, but highly unlikely to run
            await Parallel.ForEachAsync(combined, async (it, cts) =>
            {
                var chapter = it.first;
                var cluster = it.second;
                var builder = new StringBuilder();

                foreach (var item in cluster.Chapters!)
                    builder.AppendLine(chapterQuestions[item].ToString());

                var currentPrompt = galPrompt;
                currentPrompt += $"{chapter.GameChapterOutline}\n" +
                                 "This is the content the user needs to review (but don't explicitly label the learning points in the story; let the user feel like they are learning naturally)." +
                                 $"{builder}";

                var galResponse = await agent.Run(currentPrompt);
                var galCode = galResponse.Messages.Last().Content!.Replace("`", "").Replace("json", "").Trim();

                var mainStoryLine = await Parser.CompileStoryAsync(galCode);
                storyLines.Add(mainStoryLine);
            });

            var localPath = project.StoragePath;
            var savePath = Path.Combine(localPath!, "game.rhgal");
            var saveString = JsonSerializer.Serialize(storyLines);

            File.WriteAllText(savePath, saveString);
            MessageBox.Show("游戏文件创建成功，您可以在章节界面的菜单中加载了", "创建成功", MessageBoxButton.OK, MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}