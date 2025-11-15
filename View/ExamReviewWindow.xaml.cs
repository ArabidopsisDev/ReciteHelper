using Microsoft.Win32;
using ReciteHelper.Model;
using ReciteHelper.Utils;
using ReciteHelper.ViewModel;
using System.ComponentModel;
using System.IO;
using System.Windows;

namespace ReciteHelper.View;

/// <summary>
/// Interaction logic for ExamReviewWindow.xaml
/// </summary>
public partial class ExamReviewWindow : Window, INotifyPropertyChanged
{
    private List<ExamQuestionItem> _examQuestions;
    private int _totalQuestions;
    private int _correctCount;
    private int _wrongCount;
    private double _accuracy;

    public ExamReviewWindow(List<ExamQuestionItem> examQuestions)
    {
        InitializeComponent();
        _examQuestions = examQuestions ?? new List<ExamQuestionItem>();

        CalculateStatistics();
        InitializeReviewItems();
        UpdateDisplay();
    }

    private void CalculateStatistics()
    {
        _totalQuestions = _examQuestions.Count;
        _correctCount = _examQuestions.Count(q => JudgeAnswer.Run(q));
        _wrongCount = _totalQuestions - _correctCount;
        _accuracy = _totalQuestions > 0 ? (_correctCount * 100.0) / _totalQuestions : 0;
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
                UserAnswer = examQuestion.UserAnswer ?? "未作答",
                CorrectAnswer = examQuestion.Question?.CorrectAnswer ?? "正确答案缺失",
                Explanation = "无解析",
                IsCorrect = JudgeAnswer.Run(examQuestion),
                ItemStyle = JudgeAnswer.Run(examQuestion) ?
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
        var score = (double)_correctCount / _totalQuestions * 100d;
        ExamInfoText.Text = $"模拟考试 - 共{_totalQuestions}题";
        ScoreSummaryText.Text = $"得分：{score:F0}/100";

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
        writer.WriteLine();
        writer.WriteLine("=== 题目详情 ===");
        writer.WriteLine();

        for (int i = 0; i < _examQuestions.Count; i++)
        {
            var question = _examQuestions[i];
            var isCorrect = JudgeAnswer.Run(question);

            writer.WriteLine($"第{i + 1}题 {(isCorrect ? "✓" : "✗")}");
            writer.WriteLine($"题目：{question.Question?.Text}");
            writer.WriteLine($"您的答案：{question.UserAnswer}");
            writer.WriteLine($"正确答案：{question.Question?.CorrectAnswer}");


            writer.WriteLine($"解析：暂无解析");


            writer.WriteLine(new string('-', 50));
            writer.WriteLine();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected virtual void OnPropertyChanged(string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
