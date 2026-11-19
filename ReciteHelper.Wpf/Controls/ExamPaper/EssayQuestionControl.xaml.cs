using ReciteHelper.Wpf.Models;
using System.Windows.Controls;
using ReciteHelper.Core.Services;
using System.Windows;

namespace ReciteHelper.Wpf.Controls.ExamPaper;

public partial class EssayQuestionControl : UserControl
{
    private ExamQuestionItem? _item;
    private bool _isLoading;
    private MultipartAnswerEditor? _multipart;

    public EssayQuestionControl()
    {
        InitializeComponent();
    }

    public void SetQuestion(ExamQuestionItem item, bool isExamActive, bool showAnswer = true)
    {
        _item = item;
        AnswerPanel.Visibility = showAnswer ? Visibility.Visible : Visibility.Collapsed;
        AnswerBorder.Visibility = Visibility.Visible;
        NumberRun.Text = $"{item.Number}. ";
        ScoreRun.Text = $"{item.ScoreLabel} ";
        var question = item.Question;
        var parts = question is not null ? EssayAnswerLayout.Parts(question) : [];
        QuestionRun.Text = question is null ? item.QuestionText : EssayAnswerLayout.Stem(question, parts);
        PreviewParts.Visibility = !showAnswer && parts.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        PreviewParts.Text = PreviewParts.Visibility == Visibility.Visible
            ? string.Join('\n', parts.Select(EssayAnswerLayout.PartHeading)) : string.Empty;
        AnswerTextBox.Height = item.IsTermDefinition ? 72d : 112d;

        _isLoading = true;
        AnswerTextBox.Text = item.UserAnswer;
        AnswerTextBox.IsReadOnly = !isExamActive;
        AnswerTextBox.IsTabStop = isExamActive;
        _isLoading = false;
        if (_multipart is not null) AnswerPanel.Children.Remove(_multipart);
        _multipart = null;
        if (showAnswer && question is not null && parts.Count > 1)
        {
            AnswerBorder.Visibility = Visibility.Collapsed;
            _multipart = new MultipartAnswerEditor { Margin = new Thickness(18, 10, 0, 0) };
            _multipart.SetQuestion(question, item.UserAnswer, isExamActive, paper: true);
            _multipart.AnswerChanged += (_, _) => item.UserAnswer = _multipart.AnswerText;
            if (item.AiAssessment is { } assessment && !isExamActive) _multipart.SetAssessment(assessment);
            AnswerPanel.Children.Add(_multipart);
        }
    }

    private void AnswerTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isLoading && _item is not null)
            _item.UserAnswer = AnswerTextBox.Text.Trim();
    }
}
