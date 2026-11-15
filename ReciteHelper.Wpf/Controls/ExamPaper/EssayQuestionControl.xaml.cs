using ReciteHelper.Wpf.Models;
using System.Windows.Controls;

namespace ReciteHelper.Wpf.Controls.ExamPaper;

public partial class EssayQuestionControl : UserControl
{
    private ExamQuestionItem? _item;
    private bool _isLoading;

    public EssayQuestionControl()
    {
        InitializeComponent();
    }

    public void SetQuestion(ExamQuestionItem item, bool isExamActive, bool showAnswer = true)
    {
        _item = item;
        AnswerBorder.Visibility = showAnswer ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        NumberRun.Text = $"{item.Number}. ";
        ScoreRun.Text = $"{item.ScoreLabel} ";
        QuestionRun.Text = item.QuestionText;
        AnswerTextBox.Height = item.IsTermDefinition ? 72d : 112d;

        _isLoading = true;
        AnswerTextBox.Text = item.UserAnswer;
        AnswerTextBox.IsReadOnly = !isExamActive;
        AnswerTextBox.IsTabStop = isExamActive;
        _isLoading = false;
    }

    private void AnswerTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isLoading && _item is not null)
            _item.UserAnswer = AnswerTextBox.Text.Trim();
    }
}
