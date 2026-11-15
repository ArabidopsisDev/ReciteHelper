using ReciteHelper.Core.DTOs;
using ReciteHelper.Wpf.Controls.ExamPaper;
using ReciteHelper.Wpf.Models;
using System.Windows;

namespace ReciteHelper.Wpf.Views;

public partial class RuleReviewPreviewWindow : Window
{
    public RuleReviewPreviewWindow(RuleReviewPreview preview)
    {
        InitializeComponent();
        Title = $"确认预览题目 · {preview.Name}";
        IntroductionText.Text = $"以下为根据你填写的“{preview.Name}”出题要求生成的两道简答题，请检查提问范围和详细程度。";
        PlanSummaryText.Text = $"AI 根据考查范围安排了 {preview.QuestionCount} 道题，本次预览其中的 {preview.Questions.Count} 道。";
        var remaining = preview.QuestionCount - preview.Questions.Count;
        AcceptHintText.Text = remaining > 0
            ? $"接受后将保留这两道题，并按当前要求生成本类其余 {remaining} 道题。需要调整时，请返回修改提示词。"
            : "接受后将保留这两道题，完成本类出题。需要调整时，请返回修改提示词。";
        if (remaining == 0) AcceptButton.Content = "接受本类题目";

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
