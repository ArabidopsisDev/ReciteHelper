using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Enums;
using ReciteHelper.Core.Scheduling;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ReciteHelper.Wpf.Controls;

public partial class SubquestionResults : UserControl
{
    public static readonly DependencyProperty AssessmentProperty = DependencyProperty.Register(nameof(Assessment),
        typeof(AiAnswerAssessment), typeof(SubquestionResults), new PropertyMetadata(null, OnAssessmentChanged));
    public AiAnswerAssessment? Assessment
    {
        get => (AiAnswerAssessment?)GetValue(AssessmentProperty);
        set => SetValue(AssessmentProperty, value);
    }
    public SubquestionResults() => InitializeComponent();
    private static void OnAssessmentChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        => ((SubquestionResults)sender).Refresh();

    private void Refresh()
    {
        var facets = Assessment?.Facets ?? [];
        Visibility = facets.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        var rows = facets.Select((facet, index) =>
        {
            var raw = facet.RawScoreRatio ?? facet.ScoreRatio;
            var status = AiGradingPolicy.GetVerdict(raw, facet.HasFallacy);
            var (label, color) = status switch
            {
                "AC" => ("Accepted（已掌握）", "#389E0D"),
                "PC" => ("Partial Credit（部分正确）", "#C67B00"),
                "FL" => ("Fallacy（关键概念混淆）", "#7B3FB3"),
                _ => ("Wrong Answer（未达标）", "#D43832")
            };
            var reason = facet.HasFallacy ? $"关键混淆：{facet.FallacyReason}" : facet.Explanation;
            var title = $"第 {index + 1} 问：{status} / {label}";
            var nature = facet.AnswerNature switch
            {
                AnswerNature.Conceptual => "概念性\n",
                AnswerNature.Expressive => "表达性\n",
                AnswerNature.Conceptual | AnswerNature.Expressive => "概念性、表达性\n",
                _ => string.Empty
            };
            var metric = facet.AnswerNature.HasFlag(AnswerNature.Expressive) ? "判分" : "核心要点覆盖";
            var detail = $"{nature}{metric} {raw:P0}，占原题 {facet.Weight:P0}\n{facet.Question}\n{reason}".TrimEnd();
            return new PointRow($"#{index + 1}", status, raw.ToString("P0"), new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
                title, detail, title + "\n" + detail);
        }).ToList();
        Points.ItemsSource = rows;
    }
    private sealed record PointRow(string Number, string Status, string Coverage, Brush Color, string Title, string Detail, string ToolTip);
}
