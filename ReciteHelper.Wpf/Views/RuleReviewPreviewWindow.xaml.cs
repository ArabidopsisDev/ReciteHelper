using ReciteHelper.Core.DTOs;
using ReciteHelper.Wpf.Controls.ExamPaper;
using ReciteHelper.Wpf.Models;
using ReciteHelper.Wpf.Utilities;
using System.Windows;
using System.Windows.Controls;

namespace ReciteHelper.Wpf.Views;

public partial class RuleReviewPreviewWindow : Window
{
    public RuleReviewPreviewWindow(RuleReviewPreview preview)
    {
        InitializeComponent();
        CoverageNoteText.Text = preview.CoverageNote;
        CoverageNoteText.Visibility = string.IsNullOrWhiteSpace(preview.CoverageNote) ? Visibility.Collapsed : Visibility.Visible;
        Title = $"确认预览题目：{preview.Name}";
        IntroductionText.Text = $"以下为根据你填写的“{preview.Name}”出题要求生成的 {preview.Questions.Count} 道简答题，请检查提问范围和详细程度。";
        PlanSummaryText.Text = $"AI 根据考查范围安排了 {preview.QuestionCount} 道题，本次预览其中的 {preview.Questions.Count} 道。";
        var remaining = preview.QuestionCount - preview.Questions.Count;
        AcceptHintText.Text = remaining > 0
            ? $"接受后将保留预览题，并按当前要求生成本类其余 {remaining} 道题。需要调整时，请返回修改提示词。"
            : "接受后将保留预览题，完成本类出题。需要调整时，请返回修改提示词。";
        if (remaining == 0) AcceptButton.Content = "接受本类题目";
        if (preview.IsAppending)
        {
            Title = $"确认追加题目：{preview.Name}";
            IntroductionText.Text = $"以下为根据你填写的“{preview.Name}”本次追加要求生成的 {preview.Questions.Count} 道简答题，请检查新增内容的提问范围和详细程度。";
            PlanSummaryText.Text = $"AI 为本次追加安排了 {preview.QuestionCount} 道题，预览其中的 {preview.Questions.Count} 道。";
            AcceptHintText.Text = $"确认后完成本次 {preview.QuestionCount} 道新题，并追加保存到“{preview.Name}”。需要调整时，请返回修改提示词。";
            AcceptButton.Content = "接受并追加本类题目";
        }

        var score = preview.Questions.Sum(q => q.DefaultExamScore);
        var section = new ExamSectionHeaderControl();
        section.SetContent("一、简答题", $"本大题共 {preview.Questions.Count} 小题，每小题 {preview.Questions[0].DefaultExamScore} 分，共 {score} 分。");
        QuestionsPanel.Children.Add(section);
        for (var index = 0; index < preview.Questions.Count; index++)
        {
            var question = preview.Questions[index];
            var control = new EssayQuestionControl();
            control.SetQuestion(new ExamQuestionItem { Number = index + 1, Score = question.DefaultExamScore, Question = question }, false, false);
            QuestionsPanel.Children.Add(control);
            QuestionsPanel.Children.Add(new Expander { Header = "查看评分要点与资料依据", FontSize = 13, Margin = new Thickness(18, 0, 0, 12),
                Content = new TextBlock { Text = question.GetCorrectAnswerText() + (question.SourceExcerpts.Count > 0
                    ? "\n\n资料原文：\n" + string.Join("\n\n", question.SourceExcerpts) : "\n\n未提供资料，按本科课程通常要求评分。"),
                    FontFamily = AppFonts.Serif, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 12, 0), LineHeight = 24 } });
        }

        MaxHeight = Math.Max(300, SystemParameters.WorkArea.Height - 64);
        UpdateScrollLimit();
        SizeChanged += (_, _) => UpdateScrollLimit();
    }

    private void UpdateScrollLimit()
    {
        var contentWidth = Math.Max(480, (ActualWidth > 0 ? ActualWidth : Width) - 64);
        IntroductionPanel.Measure(new Size(contentWidth, double.PositiveInfinity));
        ConfirmationPanel.Measure(new Size(contentWidth, double.PositiveInfinity));
        // Leave room for the window frame, outer margin, and paper padding.
        PreviewScroll.MaxHeight = Math.Max(100, MaxHeight - IntroductionPanel.DesiredSize.Height - ConfirmationPanel.DesiredSize.Height - 116);
    }

    private void Accept_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Revise_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
