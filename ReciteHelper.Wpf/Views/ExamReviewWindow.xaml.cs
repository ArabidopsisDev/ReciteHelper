using Microsoft.Win32;
using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Wpf.Models;
using ReciteHelper.Wpf.ViewModels;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace ReciteHelper.Wpf.Views;

/// <summary>
/// Interaction logic for ExamReviewWindow.xaml
/// </summary>
public partial class ExamReviewWindow : Window, INotifyPropertyChanged
{
    private readonly IExamAnswerService _examAnswerService;
    private readonly Project? _project;
    private readonly IProjectCreationService? _projectCreationService;
    private readonly IProjectFileService? _projectFileService;
    private List<ExamQuestionItem> _examQuestions;
    private int _totalQuestions;
    private int _correctCount;
    private int _wrongCount;
    private int _earnedScore;
    private int _totalScore;
    private double _accuracy;

    public ExamReviewWindow(
        List<ExamQuestionItem> examQuestions,
        IExamAnswerService examAnswerService,
        Project? project = null,
        IProjectCreationService? projectCreationService = null,
        IProjectFileService? projectFileService = null)
    {
        _examAnswerService = examAnswerService;
        _project = project;
        _projectCreationService = projectCreationService;
        _projectFileService = projectFileService;

        InitializeComponent();
        _examQuestions = examQuestions ?? new List<ExamQuestionItem>();

        CalculateStatistics();
        InitializeReviewItems();
        UpdateDisplay();
        ImportWrongQuestionsButton.Visibility = CanImportWrongQuestions()
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void CalculateStatistics()
    {
        _totalQuestions = _examQuestions.Count;
        _correctCount = _examQuestions.Count(q => _examAnswerService.IsCorrect(q.Question!, q.UserAnswer));
        _wrongCount = _totalQuestions - _correctCount;
        _accuracy = _totalQuestions > 0 ? (_correctCount * 100.0) / _totalQuestions : 0;
        _totalScore = _examQuestions.Sum(question => question.Score);
        _earnedScore = _examQuestions
            .Where(question => _examAnswerService.IsCorrect(question.Question!, question.UserAnswer))
            .Sum(question => question.Score);
    }

    private void InitializeReviewItems()
    {
        var reviewItems = new List<ReviewItemViewModel>();

        for (int i = 0; i < _examQuestions.Count; i++)
        {
            var examQuestion = _examQuestions[i];
            var reviewItem = new ReviewItemViewModel
            {
                QuestionNumber = i + 1,
                QuestionContent = examQuestion.Question?.Text ?? "题目内容缺失",
                UserAnswer = FormatUserAnswer(examQuestion),
                CorrectAnswer = examQuestion.Question?.GetCorrectAnswerText() ?? "正确答案缺失",
                Explanation = string.IsNullOrWhiteSpace(examQuestion.Explanation)
                    ? "无解析"
                    : examQuestion.Explanation,
                IsCorrect = _examAnswerService.IsCorrect(examQuestion.Question!, examQuestion.UserAnswer),
                ItemStyle = _examAnswerService.IsCorrect(examQuestion.Question!, examQuestion.UserAnswer) ?
                    (Style)FindResource("CorrectAnswerStyle") :
                    (Style)FindResource("WrongAnswerStyle")
            };

            reviewItems.Add(reviewItem);
        }

        ReviewItemsControl.ItemsSource = reviewItems;
    }

    private void UpdateDisplay()
    {
        // Update exam information
        ExamInfoText.Text = $"模拟考试 - 共{_totalQuestions}题";
        ScoreSummaryText.Text = $"得分：{_earnedScore}/{_totalScore}";

        // Update statistics
        TotalQuestionsText.Text = _totalQuestions.ToString();
        CorrectCountText.Text = _correctCount.ToString();
        WrongCountText.Text = _wrongCount.ToString();
        AccuracyText.Text = $"{_accuracy:F1}%";
    }

    private void ExportReportButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var saveFileDialog = new SaveFileDialog
            {
                Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                FileName = $"考试报告_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
                DefaultExt = ".txt"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                ExportReportToFile(saveFileDialog.FileName);
                MessageBox.Show("报告导出成功！", "导出成功",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出报告失败：{ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportReportToFile(string filePath)
    {
        using var writer = new StreamWriter(filePath);

        writer.WriteLine("=== 模拟考试报告 ===");
        writer.WriteLine($"生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        writer.WriteLine($"总题数：{_totalQuestions}");
        writer.WriteLine($"答对题数：{_correctCount}");
        writer.WriteLine($"答错题数：{_wrongCount}");
        writer.WriteLine($"正确率：{_accuracy:F1}%");
        writer.WriteLine($"得分：{_earnedScore}/{_totalScore}");
        writer.WriteLine();
        writer.WriteLine("=== 题目详情 ===");
        writer.WriteLine();

        for (int i = 0; i < _examQuestions.Count; i++)
        {
            var question = _examQuestions[i];
            var isCorrect = _examAnswerService.IsCorrect(question.Question!, question.UserAnswer);

            writer.WriteLine($"第{i + 1}题 {(isCorrect ? "✓" : "✗")}");
            writer.WriteLine($"题目：{question.Question?.Text}");
            writer.WriteLine($"您的答案：{FormatUserAnswer(question)}");
            writer.WriteLine($"正确答案：{question.Question?.GetCorrectAnswerText()}");


            writer.WriteLine($"解析：{(string.IsNullOrWhiteSpace(question.Explanation) ? "暂无解析" : question.Explanation)}");


            writer.WriteLine(new string('-', 50));
            writer.WriteLine();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void ImportWrongQuestionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanImportWrongQuestions() || _project is null || _projectCreationService is null)
            return;

        if (!HasTextGenerationAccess())
        {
            MessageBox.Show("尚未配置可用的文本生成服务，无法把错题归并到已有章节。请先完成模型服务配置。", "无法导入", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var candidates = BuildWrongQuestionCandidates();
        if (candidates.Count == 0)
        {
            MessageBox.Show("本次考试没有可导入的错题。", "无需导入", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var selectionWindow = new WrongQuestionImportWindow(candidates)
        {
            Owner = this
        };
        if (selectionWindow.ShowDialog() is not true)
            return;

        var selectedQuestions = selectionWindow.SelectedCandidates
            .Select(candidate => candidate.Question)
            .ToList();
        var progressWindow = new ProgressWindow(ProgressWindowMode.ProjectContentImport)
        {
            Owner = this
        };

        try
        {
            IsEnabled = false;
            progressWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.Background);

            var progress = new Progress<ProjectCreationProgress>(progressWindow.ApplyProgress);
            await Task.Run(() => _projectCreationService.ImportQuestionsAsync(
                _project,
                selectedQuestions,
                Config.Configure.DeepSeekKey,
                progress));

            if (progressWindow.IsVisible)
                progressWindow.Close();

            MessageBox.Show($"已导入 {selectedQuestions.Count} 道错题，并更新知识库。", "导入完成", MessageBoxButton.OK, MessageBoxImage.Information);
            ImportWrongQuestionsButton.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            if (progressWindow.IsVisible)
                progressWindow.Close();
            MessageBox.Show($"导入错题失败：{ex.Message}", "导入失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (progressWindow.IsVisible)
                progressWindow.Close();
            IsEnabled = true;
        }
    }

    private static bool HasTextGenerationAccess()
    {
        return ReciteHelper.Core.Configuration.ModelAccess.HasTextGeneration(Config.Configure);
    }

    private bool CanImportWrongQuestions()
    {
        return _project is { Chapters.Count: > 0 } &&
               _projectCreationService is not null &&
               _examQuestions.Any(question => question.Question is not null &&
                   !_examAnswerService.IsCorrect(question.Question, question.UserAnswer));
    }

    private List<WrongQuestionImportCandidate> BuildWrongQuestionCandidates()
    {
        var existingQuestions = _project?.ExportQuestions() ?? [];
        return _examQuestions
            .Where(item => item.Question is not null && !_examAnswerService.IsCorrect(item.Question, item.UserAnswer))
            .Select(item =>
            {
                var maxSimilarity = existingQuestions
                    .Where(existing => !ReferenceEquals(existing, item.Question))
                    .Select(existing => CalculateQuestionSimilarity(existing, item.Question!))
                    .DefaultIfEmpty(0d)
                    .Max();
                var hasSimilar = maxSimilarity >= 0.82d;
                return new WrongQuestionImportCandidate
                {
                    Number = item.Number,
                    Question = item.Question!,
                    UserAnswer = FormatUserAnswer(item),
                    CorrectAnswer = item.Question!.GetCorrectAnswerText(),
                    HasSimilarQuestion = hasSimilar,
                    Similarity = maxSimilarity,
                    IsSelected = !hasSimilar
                };
            })
            .ToList();
    }

    private static double CalculateQuestionSimilarity(Question left, Question right)
    {
        var leftText = NormalizeForSimilarity($"{left.Text} {left.GetCorrectAnswerText()}");
        var rightText = NormalizeForSimilarity($"{right.Text} {right.GetCorrectAnswerText()}");
        if (leftText.Length == 0 || rightText.Length == 0)
            return 0d;

        var distance = LevenshteinDistance(leftText, rightText);
        return 1d - distance / (double)Math.Max(leftText.Length, rightText.Length);
    }

    private static string NormalizeForSimilarity(string text)
    {
        return new string((text ?? string.Empty)
            .Where(character => !char.IsWhiteSpace(character) && !char.IsPunctuation(character))
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private static int LevenshteinDistance(string left, string right)
    {
        var costs = new int[right.Length + 1];
        for (var j = 0; j <= right.Length; j++)
            costs[j] = j;

        for (var i = 1; i <= left.Length; i++)
        {
            var previous = costs[0];
            costs[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                var current = costs[j];
                costs[j] = left[i - 1] == right[j - 1]
                    ? previous
                    : Math.Min(Math.Min(costs[j - 1], costs[j]), previous) + 1;
                previous = current;
            }
        }

        return costs[right.Length];
    }

    private static string FormatUserAnswer(ExamQuestionItem item)
    {
        if (!item.IsFillBlank)
            return string.IsNullOrWhiteSpace(item.UserAnswer) ? "未作答" : item.UserAnswer;

        var answers = Question.SplitBlankAnswers(item.UserAnswer);
        return answers.Count == 0
            ? "未作答"
            : string.Join("；", answers.Select((answer, index) => $"{index + 1}. {answer}"));
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected virtual void OnPropertyChanged(string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
