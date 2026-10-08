using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.DTOs;
using ReciteHelper.Wpf.Models;
using ReciteHelper.Wpf.ViewModels;
using Microsoft.Win32;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ReciteHelper.Wpf.Views;

public partial class SelectChapterWindow : Window, INotifyPropertyChanged
{
    private readonly IProjectFileService _projectFileService;
    private readonly IQuizService _quizService;
    private readonly IReviewGenerator _reviewGenerator;
    private readonly IExamAnswerService _examAnswerService;
    private readonly IExamPaperService _examPaperService;
    private readonly IExamSettingsService _examSettingsService;
    private readonly IExamSetImportService _examSetImportService;
    private readonly IExamSetRepository _examSetRepository;
    private readonly IProjectCreationService _projectCreationService;
    private readonly IQuestionHelpService _questionHelpService;
    private readonly IReviewPersonalizationService _reviewPersonalizationService;
    private Project? _currentProject;
    private DispatcherTimer _clockTimer;
    private List<ChapterViewModel> _chapters;

    public SelectChapterWindow(
        Project project,
        IProjectFileService projectFileService,
        IQuizService quizService,
        IReviewGenerator reviewGenerator,
        IExamAnswerService examAnswerService,
        IExamPaperService examPaperService,
        IExamSettingsService examSettingsService,
        IExamSetImportService examSetImportService,
        IExamSetRepository examSetRepository,
        IProjectCreationService projectCreationService,
        IQuestionHelpService questionHelpService,
        IReviewPersonalizationService reviewPersonalizationService)
    {
        _reviewPersonalizationService = reviewPersonalizationService;
        _projectFileService = projectFileService;
        _quizService = quizService;
        _reviewGenerator = reviewGenerator;
        _examAnswerService = examAnswerService;
        _examPaperService = examPaperService;
        _examSettingsService = examSettingsService;
        _examSetImportService = examSetImportService;
        _examSetRepository = examSetRepository;
        _projectCreationService = projectCreationService;
        _questionHelpService = questionHelpService;

        InitializeComponent();
        _currentProject = project;

        InitializeData();
        InitializeClock();
        UpdateDisplay();
    }

    private void InitializeData()
    {
        _chapters = new List<ChapterViewModel>();

        if (_currentProject?.Chapters != null)
        {
            foreach (var chapter in _currentProject.Chapters)
            {
                var chapterVM = new ChapterViewModel(chapter)
                {
                    MasteryLevel = CalculateMasteryLevel(chapter)
                };
                _chapters.Add(chapterVM);
            }
        }

        ChaptersItemsControl.ItemsSource = _chapters;
    }

    internal static double CalculateMasteryLevel(Chapter chapter)
    {
        // Calculate the mastery level of the chapter.
        if (chapter.Questions == null || chapter.Questions.Count == 0)
            return 0;

        int count = 0, sum = chapter.Questions.Count;
        foreach (var question in chapter.Questions)
            if (question.Status == true) count++;

        return (double)count / sum * 100d;
    }

    private void InitializeClock()
    {
        _clockTimer = new DispatcherTimer();
        _clockTimer.Interval = TimeSpan.FromSeconds(1);
        _clockTimer.Tick += ClockTimer_Tick;
        _clockTimer.Start();
    }

    private void ClockTimer_Tick(object? sender, EventArgs e)
    {
        CurrentTimeText.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }

    private void UpdateDisplay()
    {
        // Update project information
        ProjectNameText.Text = _currentProject?.ProjectName ?? "未命名项目";
        ProjectPathText.Text = _currentProject?.StoragePath ?? "路径不可用";

        // Update last access time
        if (_currentProject?.LastAccessed != null)
        {
            LastAccessedText.Text = $"最后访问：{_currentProject.LastAccessed:yyyy-MM-dd HH:mm}";
        }

        // Update statistics
        var chapterCount = _chapters?.Count ?? 0;
        var totalQuestions = _chapters?.Sum(c => c.QuestionCount) ?? 0;
        ChapterStatsText.Text = $"共 {chapterCount} 个章节，{totalQuestions} 道题目";

        // Show/hide empty state
        EmptyStatePanel.Visibility = chapterCount == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ChapterButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is ChapterViewModel chapterVM)
        {
            NavigateToChapterQuiz(chapterVM.Chapter);
        }
    }

    private void NavigateToChapterQuiz(Chapter chapter)
    {
        if (chapter?.Questions == null || chapter.Questions.Count == 0)
        {
            MessageBox.Show("该章节暂无题目", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var quizWindow = new QuizWindow(
            _currentProject,
            chapter.Name!,
            _quizService,
            _projectFileService,
            _questionHelpService,
            _reviewPersonalizationService)
        {
            Title = $"{_currentProject.ProjectName} - {chapter.Name}",
            Owner = this
        };

        quizWindow.ShowDialog();
        RefreshMasteryLevels();
    }

    private void SimulateButton_Click(object sender, RoutedEventArgs e)
    {
        var random = Random.Shared;
        var examWindow = new ExamSettingWindow(
            _currentProject,
            _examAnswerService,
            _examPaperService,
            _examSettingsService,
            _examSetRepository,
            _projectCreationService,
            _projectFileService);

        examWindow.Show();
        Close();
    }

    private void RefreshMasteryLevels()
    {
        foreach (var chapterVM in _chapters)
        {
            chapterVM.MasteryLevel = CalculateMasteryLevel(chapterVM.Chapter);
        }

        ChaptersItemsControl.Items.Refresh();
    }

    private void KnowledgeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProject != null)
        {
            var knowledgeWindow = new KnowledgePointWindow(_currentProject, _projectFileService);
            knowledgeWindow.ShowDialog();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        FunctionMenu.PlacementTarget = ExportButton;
        FunctionMenu.IsOpen = true;
    }

    private async void ImportExamMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProject is null)
            return;

        if (!HasTextGenerationAccess())
        {
            MessageBox.Show("尚未配置可用的文本生成服务，无法抽取套卷。请先完成模型服务配置。", "无法导入", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "选择要导入的试卷",
            Filter = "试卷文件 (*.pdf;*.txt;*.html;*.htm;*.mhtml;*.mht)|*.pdf;*.txt;*.html;*.htm;*.mhtml;*.mht|学堂在线网页 (*.html;*.htm;*.mhtml;*.mht)|*.html;*.htm;*.mhtml;*.mht|PDF 文件 (*.pdf)|*.pdf|文本文件 (*.txt)|*.txt",
            Multiselect = false,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) is not true)
            return;

        var progressWindow = new ProgressWindow(ProgressWindowMode.ExamSetImport)
        {
            Owner = this
        };
        SetExamImportState(true);
        try
        {
            progressWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.Background);

            var progress = new Progress<ExamSetImportProgress>(progressWindow.ApplyProgress);
            var imported = await Task.Run(() => _examSetImportService.ImportAsync(
                    _currentProject,
                    dialog.FileName,
                    Config.Configure.DeepSeekKey,
                    progress));

            if (progressWindow.IsVisible)
                progressWindow.Close();
            MessageBox.Show(
                $"已从“{Path.GetFileName(dialog.FileName)}”中识别并保存 {imported.Count} 套试卷。\n可在“模拟考试”中选择加载套卷。",
                "导入完成",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            if (progressWindow.IsVisible)
                progressWindow.Close();
            MessageBox.Show($"导入试卷失败：{ex.Message}", "导入失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (progressWindow.IsVisible)
                progressWindow.Close();
            SetExamImportState(false);
            UpdateDisplay();
        }
    }

    private async void AddQuestionSourceMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProject is null)
            return;

        if (!HasTextGenerationAccess())
        {
            MessageBox.Show("尚未配置可用的文本生成服务，无法从新资料中生成题目。请先完成模型服务配置。", "无法导入", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "选择要增加的题目来源",
            Filter = "学习资料 (*.pdf;*.meg)|*.pdf;*.meg|PDF 文件 (*.pdf)|*.pdf|合并文件 (*.meg)|*.meg",
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) is not true)
            return;

        var progressWindow = new ProgressWindow(ProgressWindowMode.ProjectContentImport)
        {
            Owner = this
        };
        SetExamImportState(true);
        try
        {
            progressWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.Background);

            var progress = new Progress<ProjectCreationProgress>(progressWindow.ApplyProgress);
            await Task.Run(() => _projectCreationService.AppendSourcesAsync(
                _currentProject,
                dialog.FileNames,
                Config.Configure.DeepSeekKey,
                Config.Configure.Strategy,
                progress));

            if (progressWindow.IsVisible)
                progressWindow.Close();

            InitializeData();
            UpdateDisplay();
            ChaptersItemsControl.ItemsSource = _chapters;
            ChaptersItemsControl.Items.Refresh();
            MessageBox.Show("新的题目来源已经导入项目，知识库也已更新。", "导入完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            if (progressWindow.IsVisible)
                progressWindow.Close();
            MessageBox.Show($"增加题目来源失败：{ex.Message}", "导入失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (progressWindow.IsVisible)
                progressWindow.Close();
            SetExamImportState(false);
        }
    }

    private static bool HasTextGenerationAccess()
    {
        return ReciteHelper.Core.Configuration.ModelAccess.HasTextGeneration(Config.Configure);
    }

    private void SetExamImportState(bool isImporting)
    {
        ExportButton.IsEnabled = !isImporting;
        SimulateButton.IsEnabled = !isImporting;
        ExportButton.Content = isImporting ? "正在导入..." : "功能菜单";
        IsEnabled = !isImporting;
    }

    private async void ExportMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProject is null)
            return;

        var saveFileDialog = new SaveFileDialog
        {
            Title = "导出 ReciteHelper 项目包",
            Filter = "ReciteHelper项目包 (*.rhp)|*.rhp",
            FileName = $"{_currentProject.ProjectName}.rhp",
            AddExtension = true,
            DefaultExt = ".rhp",
            OverwritePrompt = true
        };

        if (saveFileDialog.ShowDialog() != true)
            return;

        try
        {
            var archivePath = await _projectFileService.ExportProjectArchiveAsync(
                _currentProject,
                saveFileDialog.FileName,
                Config.Configure?.Version);
            var folderPath = Path.GetDirectoryName(archivePath);

            if (folderPath is not null)
                System.Diagnostics.Process.Start("explorer.exe", folderPath);

            MessageBox.Show($"项目已导出：\n{archivePath}", "导出完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出失败：{ex.Message}", "导出失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ReviewMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var questionList = _reviewGenerator.GenerateReview(_currentProject, 30);

        var quizWindow = new QuizWindow(
            _currentProject,
            questionList,
            _quizService,
            _projectFileService,
            _questionHelpService,
            _reviewPersonalizationService)
        { Owner = System.Windows.Application.Current.MainWindow };
        quizWindow.Show();
        Close();
    }

    public Project CurrentProject
    {
        get => _currentProject;
        set
        {
            _currentProject = value;
            OnPropertyChanged(nameof(CurrentProject));
            InitializeData();
            UpdateDisplay();
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected virtual void OnPropertyChanged(string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
