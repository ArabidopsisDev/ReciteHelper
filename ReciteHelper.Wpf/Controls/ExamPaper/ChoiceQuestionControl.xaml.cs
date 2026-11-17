using ReciteHelper.Core.ValueObjects;
using ReciteHelper.Wpf.Models;
using ReciteHelper.Wpf.Utilities;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ReciteHelper.Wpf.Controls.ExamPaper;

public partial class ChoiceQuestionControl : UserControl
{
    private const double DefaultOptionFontSize = 16d;
    private const double MinimumCompactFontSize = 14d;
    private const double OptionTextWidth = 258d;
    private const double MinorOverflowWidth = 32d;

    private ExamQuestionItem? _item;
    private List<ChoiceOptionItem> _options = [];

    public ChoiceQuestionControl()
    {
        InitializeComponent();
    }

    public void SetQuestion(ExamQuestionItem item, bool isExamActive)
    {
        _item = item;
        IsHitTestVisible = isExamActive;
        Focusable = isExamActive;
        Opacity = isExamActive ? 1d : 0.86d;
        NumberRun.Text = $"{item.Number}. ";
        QuestionRun.Text = item.QuestionText;
        _options = item.Options
            .Select(option => new ChoiceOptionItem(
                QuestionOption.NormalizeId(option.Id),
                option.Text,
                QuestionOption.NormalizeId(option.Id) == QuestionOption.NormalizeId(item.UserAnswer),
                CalculateOptionFontSize(QuestionOption.NormalizeId(option.Id), option.Text)))
            .ToList();
        OptionsItemsControl.ItemsSource = _options;
    }

    private void OptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_item is null || sender is not Button { Tag: string optionId })
            return;

        _item.UserAnswer = QuestionOption.NormalizeId(optionId);
        foreach (var option in _options)
            option.IsSelected = option.Id == _item.UserAnswer;

        OptionsItemsControl.Items.Refresh();
    }

    private static double CalculateOptionFontSize(string id, string text)
    {
        var formattedText = new FormattedText(
            $"{id}. {text}",
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(
                AppFonts.Serif,
                FontStyles.Normal,
                FontWeights.Normal,
                FontStretches.Normal),
            DefaultOptionFontSize,
            Brushes.Black,
            1d);
        var textWidth = formattedText.WidthIncludingTrailingWhitespace;

        if (textWidth <= OptionTextWidth || textWidth > OptionTextWidth + MinorOverflowWidth)
            return DefaultOptionFontSize;

        return Math.Max(
            MinimumCompactFontSize,
            Math.Floor(DefaultOptionFontSize * OptionTextWidth / textWidth * 2d) / 2d);
    }

    private sealed class ChoiceOptionItem(string id, string text, bool isSelected, double fontSize)
    {
        public string Id { get; } = id;
        public string Text { get; } = text;
        public bool IsSelected { get; set; } = isSelected;
        public double FontSize { get; } = fontSize;
    }
}
