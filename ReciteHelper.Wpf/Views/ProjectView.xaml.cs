using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Application.Services;
using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Scheduling;
using ReciteHelper.Wpf.Models;
using ReciteHelper.Wpf.ViewModels;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ReciteHelper.Wpf.Views;

public partial class ProjectView : UserControl
{
    private readonly IProjectFileService _projectFileService;
    private readonly IExamAnswerService _examAnswerService;
    private readonly IExamPaperService _examPaperService;
    private readonly IExamSettingsService _examSettingsService;
    private readonly IExamSetImportService _examSetImportService;
    private readonly IExamSetRepository _examSetRepository;
    private readonly IProjectCreationService _projectCreationService;
    private readonly IRuleReviewService? _ruleReviewService;
    private readonly QuickReviewService? _quickReviewService;
    private readonly Project _currentProject;
    private List<ChapterViewModel> _chapters = [];

    internal Project Project => _currentProject;
    internal event EventHandler? HomeRequested;
    internal event EventHandler? KnowledgeRequested;
    internal event EventHandler? KnowledgeSourcesChanged;
    internal event EventHandler<Chapter>? ChapterPracticeRequested;
    internal event EventHandler? SmartPracticeRequested;

    public ProjectView(
        Project project,
        IProjectFileService projectFileService,
        IExamAnswerService examAnswerService,
        IExamPaperService examPaperService,
        IExamSettingsService examSettingsService,
        IExamSetImportService examSetImportService,
        IExamSetRepository examSetRepository,
        IProjectCreationService projectCreationService,
        IRuleReviewService? ruleReviewService = null,
        QuickReviewService? quickReviewService = null)
    {
        _projectFileService = projectFileService;
        _examAnswerService = examAnswerService;
        _examPaperService = examPaperService;
        _examSettingsService = examSettingsService;
        _examSetImportService = examSetImportService;
        _examSetRepository = examSetRepository;
        _projectCreationService = projectCreationService;
        _ruleReviewService = ruleReviewService;
        _quickReviewService = quickReviewService;

        InitializeComponent();
        _currentProject = project;
        ManageRuleQuestionsButton.Visibility = project.TemplateType == Core.Enums.ProjectTemplateType.RuleBasedReview ? Visibility.Visible : Visibility.Collapsed;

        RefreshChapters();
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
                    MasteryLevel = CalculateMasteryLevel(chapter, _currentProject.SchedulerParameters)
                };
                _chapters.Add(chapterVM);
            }
        }

        ChaptersItemsControl.ItemsSource = _chapters;
    }

    internal static double CalculateMasteryLevel(Chapter chapter, SchedulerParameters? parameters = null)
    {
        // Calculate the mastery level of the chapter.
        if (chapter.Questions == null || chapter.Questions.Count == 0)
            return 0;

        var now = DateTime.Now;
        return chapter.Questions.Average(question => question.ReviewFacets.Count > 0
            ? FacetMemoryProjection.Mastery(question, now, parameters ?? SchedulerParameters.Default)
            : question.LastAiAssessment is { } assessment && question.Memory is not null
                ? assessment.ScoreRatio * FacetMemoryProjection.Recall(question.Memory, now, parameters ?? SchedulerParameters.Default)
            : question.Status is true ? 1d : 0d) * 100d;
    }

    internal void RefreshChapters()
    {
        InitializeData();
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        // Update project information
        ProjectNameText.Text = _currentProject?.ProjectName ?? "未命名项目";
        ProjectPathText.Text = _currentProject?.StoragePath ?? "路径不可用";

        // Update last access time
        if (_currentProject?.LastAccessed is { } lastAccessed && lastAccessed != default)
        {
            LastAccessedText.Text = $"最后访问：{lastAccessed:yyyy-MM-dd HH:mm}";
        }
        else LastAccessedText.Text = "尚无访问记录";

        // Update statistics
        var chapterCount = _chapters?.Count ?? 0;
        var totalQuestions = _chapters?.Sum(c => c.QuestionCount) ?? 0;
        var archivedQuestions = ReviewQuestionCatalog.Roots(_currentProject).Count(question => question.IsArchived);
        ChapterStatsText.Text = archivedQuestions == 0 ? $"共 {chapterCount} 个章节，{totalQuestions} 道题目"
            : $"共 {chapterCount} 个章节，{totalQuestions} 道题目，已归档 {archivedQuestions} 道";

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

        ChapterPracticeRequested?.Invoke(this, chapter);
    }

    private void SimulateButton_Click(object sender, RoutedEventArgs e)
    {
        var examWindow = new ExamSettingWindow(
            _currentProject,
            _examAnswerService,
            _examPaperService,
            _examSettingsService,
            _examSetRepository,
            _projectCreationService,
            _projectFileService,
            _quickReviewService)
        { Owner = Window.GetWindow(this) };

        examWindow.Show();
    }

    private void KnowledgeButton_Click(object sender, RoutedEventArgs e)
    {
        KnowledgeRequested?.Invoke(this, EventArgs.Empty);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        HomeRequested?.Invoke(this, EventArgs.Empty);
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
        if (dialog.ShowDialog(Window.GetWindow(this)) is not true)
            return;

        var progressWindow = new ProgressWindow(ProgressWindowMode.ExamSetImport)
        {
            Owner = Window.GetWindow(this)
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
        if (dialog.ShowDialog(Window.GetWindow(this)) is not true)
            return;

        var progressWindow = new ProgressWindow(ProgressWindowMode.ProjectContentImport)
        {
            Owner = Window.GetWindow(this)
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

            RefreshChapters();
            KnowledgeSourcesChanged?.Invoke(this, EventArgs.Empty);
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
        SmartReviewButton.IsEnabled = !isImporting;
        ManageRuleQuestionsButton.IsEnabled = !isImporting;
        ExportButton.Content = isImporting ? "正在导入…" : "导入与导出";
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

        if (saveFileDialog.ShowDialog(Window.GetWindow(this)) != true)
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
        SmartPracticeRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ManageRuleQuestions_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProject?.TemplateType != Core.Enums.ProjectTemplateType.RuleBasedReview || _ruleReviewService is null) return;
        new CreateRuleReviewWindow(_ruleReviewService, _projectFileService, _ => Task.CompletedTask, _currentProject)
            { Owner = Window.GetWindow(this) }.ShowDialog();
        RefreshChapters();
    }
}
