using ReciteHelper.Core.DTOs;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ReciteHelper.Wpf.Controls;

public sealed class AiAnnotatedAnswer : TextBlock
{
    public static readonly DependencyProperty AssessmentProperty = DependencyProperty.Register(
        nameof(Assessment), typeof(AiAnswerAssessment), typeof(AiAnnotatedAnswer), new PropertyMetadata(null, OnAssessmentChanged));

    public static readonly DependencyProperty FallbackTextProperty = DependencyProperty.Register(
        nameof(FallbackText), typeof(string), typeof(AiAnnotatedAnswer), new PropertyMetadata(string.Empty, OnFallbackChanged));

    public string FallbackText
    {
        get => (string)GetValue(FallbackTextProperty);
        set => SetValue(FallbackTextProperty, value);
    }

    public AiAnswerAssessment? Assessment
    {
        get => (AiAnswerAssessment?)GetValue(AssessmentProperty);
        set => SetValue(AssessmentProperty, value);
    }

    private static void OnAssessmentChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var block = (AiAnnotatedAnswer)sender;
        if (e.NewValue is AiAnswerAssessment assessment) Render(block, assessment);
        else { block.Text = block.FallbackText; block.ToolTip = null; }
    }

    private static void OnFallbackChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var block = (AiAnnotatedAnswer)sender;
        if (block.Assessment is null) block.Text = (string)e.NewValue;
    }

    public static void Render(TextBlock block, AiAnswerAssessment assessment)
    {
        block.Inlines.Clear();
        block.ToolTip = $"AI 评分：{assessment.ScoreRatio:P0}。{assessment.Explanation}";
        foreach (var item in assessment.Annotations)
        {
            block.Inlines.Add(new Run(item.Text)
            {
                Foreground = item.Status switch { "correct" => Brushes.DarkGreen, "incorrect" => Brushes.Firebrick, _ => Brushes.DarkGoldenrod },
                Background = item.Status == "partial" ? Brushes.LightYellow : Brushes.Transparent,
                ToolTip = item.Comment
            });
        }
        if (assessment.MissingPoints.Count > 0)
            block.Inlines.Add(new Run(" 〔需补充〕")
            {
                Foreground = Brushes.DarkGoldenrod,
                Background = Brushes.LightYellow,
                ToolTip = string.Join("\n", assessment.MissingPoints)
            });
    }
}
