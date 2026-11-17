using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Services;
using ReciteHelper.Wpf.Models;
using ReciteHelper.Wpf.Utilities;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ReciteHelper.Wpf.Controls.ExamPaper;

public partial class FillBlankQuestionControl : UserControl
{
    private readonly List<TextBox> _answerBoxes = [];
    private ExamQuestionItem? _item;
    private bool _isLoading;

    public FillBlankQuestionControl()
    {
        InitializeComponent();
    }

    public void SetQuestion(ExamQuestionItem item, bool isExamActive)
    {
        _item = item;
        QuestionTextBlock.Inlines.Clear();
        _answerBoxes.Clear();

        var savedAnswers = Question.SplitBlankAnswers(item.UserAnswer);
        _isLoading = true;

        QuestionTextBlock.Inlines.Add(new Run($"{item.Number}. "));
        QuestionTextBlock.Inlines.Add(new Run($"{item.ScoreLabel} ") { FontWeight = FontWeights.Bold });

        var questionText = FillBlankTextNormalizer.NormalizeForDisplay(
            StripLeadingQuestionNumber(item.QuestionText),
            item.Question?.GetCorrectAnswers() ?? []);
        var blankMatches = BlankMarkerRegex().Matches(questionText);
        var blankCount = Math.Max(1, item.Question?.BlankCount ?? Math.Max(1, blankMatches.Count));
        var currentIndex = 0;
        var textPosition = 0;

        foreach (Match match in blankMatches.Cast<Match>().Take(blankCount))
        {
            if (match.Index > textPosition)
                QuestionTextBlock.Inlines.Add(new Run(questionText[textPosition..match.Index]));

            var field = CreateAnswerField(currentIndex, currentIndex < savedAnswers.Count ? savedAnswers[currentIndex] : string.Empty, isExamActive);
            _answerBoxes.Add(field);
            QuestionTextBlock.Inlines.Add(CreateInlineField(currentIndex, field));
            currentIndex++;
            textPosition = match.Index + match.Length;
        }

        if (textPosition < questionText.Length)
            QuestionTextBlock.Inlines.Add(new Run(questionText[textPosition..]));

        while (currentIndex < blankCount)
        {
            QuestionTextBlock.Inlines.Add(new Run(" "));
            var field = CreateAnswerField(currentIndex, currentIndex < savedAnswers.Count ? savedAnswers[currentIndex] : string.Empty, isExamActive);
            _answerBoxes.Add(field);
            QuestionTextBlock.Inlines.Add(CreateInlineField(currentIndex, field));
            currentIndex++;
        }

        _isLoading = false;
    }

    private TextBox CreateAnswerField(int index, string answer, bool isExamActive)
    {
        var field = new TextBox
        {
            // Paper fields keep their compact baseline geometry independently of app forms.
            Style = null,
            Width = 92,
            MinWidth = 76,
            Padding = new Thickness(3, 1, 3, 1),
            Background = new SolidColorBrush(Color.FromRgb(255, 253, 232)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0, 122, 204)),
            BorderThickness = new Thickness(0, 0, 0, 1.5),
            FontFamily = AppFonts.Serif,
            FontSize = 16,
            Foreground = new SolidColorBrush(Color.FromRgb(159, 24, 38)),
            IsReadOnly = !isExamActive,
            IsTabStop = isExamActive,
            Text = answer,
            Tag = index
        };
        field.TextChanged += AnswerField_TextChanged;
        return field;
    }

    private static InlineUIContainer CreateInlineField(int index, TextBox field)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(4, 0, 6, 0)
        };
        panel.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 0, 3, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = AppFonts.Serif,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0, 92, 168)),
            Text = ToCircledNumber(index + 1)
        });
        panel.Children.Add(field);

        return new InlineUIContainer(panel)
        {
            BaselineAlignment = BaselineAlignment.TextBottom
        };
    }

    private void AnswerField_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading || _item is null)
            return;

        _item.UserAnswer = Question.JoinBlankAnswers(_answerBoxes.Select(field => field.Text));
    }

    private static string StripLeadingQuestionNumber(string text)
    {
        return LeadingQuestionNumberRegex().Replace(text ?? string.Empty, string.Empty, 1);
    }

    private static string ToCircledNumber(int number)
    {
        return number is >= 1 and <= 20
            ? char.ConvertFromUtf32(0x245F + number)
            : $"({number})";
    }

    [GeneratedRegex(@"_{2,}|＿{2,}")]
    private static partial Regex BlankMarkerRegex();

    [GeneratedRegex(@"^\s*\d+\s*[\.、]\s*")]
    private static partial Regex LeadingQuestionNumberRegex();
}
