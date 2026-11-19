using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Scheduling;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ReciteHelper.Wpf.Controls;

public partial class AnswerHistoryTimeline : UserControl
{
    public static readonly DependencyProperty QuestionProperty = DependencyProperty.Register(
        nameof(Question), typeof(Question), typeof(AnswerHistoryTimeline),
        new PropertyMetadata(null, (target, args) => ((AnswerHistoryTimeline)target).Draw((Question?)args.NewValue)));

    private const double Spacing = 56;
    private const double NodeWidth = 26;
    private const double NodeHeight = 16;
    private readonly record struct Attempt(ReviewTag Tag, bool IsExam, string Verdict);
    private readonly record struct Lane(double Y, SolidColorBrush Foreground, SolidColorBrush Background);

    private static readonly Lane Accepted = new(10, Brush(0x176647), Brush(0xD9F0E4));
    private static readonly Lane Partial = new(28, Brush(0x7E5414), Brush(0xFAEAC6));
    private static readonly Lane Wrong = new(46, Brush(0xA43342), Brush(0xF8DEE1));
    private static readonly Lane Fallacy = new(64, Brush(0x6E438B), Brush(0xEFE3F7));

    public AnswerHistoryTimeline() => InitializeComponent();

    public Question? Question
    {
        get => (Question?)GetValue(QuestionProperty);
        set => SetValue(QuestionProperty, value);
    }

    internal void SetQuestion(Question? question)
    {
        if (ReferenceEquals(Question, question))
            Draw(question);
        else
            Question = question;
    }

    private void Draw(Question? question)
    {
        Plot.Children.Clear();
        var lastReview = question?.ReviewTag.MaxBy(tag => tag.Time);
        var attempts = question?.ReviewTag
            .Select(tag => new Attempt(tag, false, Verdict(tag, ReferenceEquals(tag, lastReview) ? question.LastAiAssessment : null)))
            .Concat((question.ExamHistory ?? []).Select(tag => new Attempt(tag, true, Verdict(tag))))
            .OrderBy(attempt => attempt.Tag.Time).ToArray() ?? [];
        Visibility = attempts.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (attempts.Length == 0) return;

        CountText.Text = $"{attempts.Length} 次";
        Plot.Width = attempts.Length * Spacing;
        var minY = attempts.Min(attempt => GetLane(attempt.Verdict).Y);
        Plot.Height = attempts.Max(attempt => GetLane(attempt.Verdict).Y) - minY + 32;
        var points = attempts.Select((attempt, index) =>
            new Point(Spacing * (index + 0.5), GetLane(attempt.Verdict).Y - minY + 10)).ToArray();
        for (var i = 1; i < points.Length; i++)
        {
            var from = new Point(points[i - 1].X + NodeWidth / 2 - 1, points[i - 1].Y);
            var to = new Point(points[i].X - NodeWidth / 2 + 1, points[i].Y);
            var fromColor = GetLane(attempts[i - 1].Verdict).Foreground.Color;
            var toColor = GetLane(attempts[i].Verdict).Foreground.Color;
            var fill = new LinearGradientBrush(Tint(fromColor), Tint(toColor), new Point(0, 0), new Point(1, 0));
            fill.Freeze();
            Plot.Children.Add(new Path { Data = Ribbon(from, to), Fill = fill, IsHitTestVisible = false });
        }
        for (var i = 0; i < attempts.Length; i++)
            AddNode(attempts[i], points[i], i + 1);

        HistoryScroll.ScrollToRightEnd();
    }

    // Adapted from TKCode.Arc's SankeyFlow.Render.cs (MIT). The history has one
    // node per column, so every ribbon joins the center of two adjacent nodes.
    private static StreamGeometry Ribbon(Point from, Point to)
    {
        const double half = 3;
        var reach = (to.X - from.X) * 0.5;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(from.X, from.Y - half), true, true);
            context.BezierTo(new Point(from.X + reach, from.Y - half), new Point(to.X - reach, to.Y - half),
                new Point(to.X, to.Y - half), true, false);
            context.LineTo(new Point(to.X, to.Y + half), true, false);
            context.BezierTo(new Point(to.X - reach, to.Y + half), new Point(from.X + reach, from.Y + half),
                new Point(from.X, from.Y + half), true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private void AddNode(Attempt attempt, Point center, int number)
    {
        var lane = GetLane(attempt.Verdict);
        var tooltip = Describe(attempt, number);
        var node = new Border
        {
            Width = NodeWidth, Height = NodeHeight, CornerRadius = new CornerRadius(4),
            BorderBrush = lane.Foreground, Background = lane.Background, Focusable = true,
            ToolTip = tooltip,
            Child = new TextBlock
            {
                Text = attempt.Verdict, FontSize = 10, FontWeight = FontWeights.SemiBold,
                Foreground = lane.Foreground, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        AutomationProperties.SetName(node, tooltip);
        ToolTipService.SetShowDuration(node, 20000);
        node.MouseEnter += (_, _) => node.BorderThickness = new Thickness(1);
        node.MouseLeave += (_, _) => node.BorderThickness = new Thickness(node.IsKeyboardFocused ? 1 : 0);
        node.IsKeyboardFocusedChanged += (_, _) => node.BorderThickness = new Thickness(node.IsKeyboardFocused || node.IsMouseOver ? 1 : 0);
        Canvas.SetLeft(node, center.X - NodeWidth / 2);
        Canvas.SetTop(node, center.Y - NodeHeight / 2);
        Plot.Children.Add(node);

        var label = new TextBlock { Text = $"#{number}", FontSize = 10, Width = Spacing, TextAlignment = TextAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "ArcTextMuted");
        Canvas.SetLeft(label, center.X - Spacing / 2);
        Canvas.SetBottom(label, 0);
        Plot.Children.Add(label);
    }

    private static string Verdict(ReviewTag tag, Core.DTOs.AiAnswerAssessment? assessment = null)
        => tag.Verdict ?? (assessment is not null
            ? AiGradingPolicy.GetRecordedVerdict(assessment.IsCorrect,
                assessment.RawScoreRatio ?? assessment.ScoreRatio, assessment.HasFallacy)
            : (tag.IsCorrect ?? ReviewGrader.IsRecall(tag.Grade > 0
                ? (ReviewGrade)tag.Grade : ReviewGrader.FromLegacyQuality(tag.QValue))) ? "AC" : "WA");

    private static Lane GetLane(string verdict) => verdict switch
    {
        "AC" => Accepted,
        "PC" => Partial,
        "FL" => Fallacy,
        _ => Wrong
    };

    private static string Describe(Attempt attempt, int number)
    {
        var tag = attempt.Tag;
        var status = attempt.Verdict switch { "AC" => "通过", "PC" => "部分正确", "FL" => "概念混淆", _ => "错误" };
        var kind = attempt.IsExam ? "考试" : "复习";
        var time = tag.Time.Kind == DateTimeKind.Utc ? tag.Time.ToLocalTime() : tag.Time;
        var lines = new List<string>
        {
            $"第 {number} 次{kind}  {attempt.Verdict} {status}",
            time.ToString("yyyy-MM-dd HH:mm:ss")
        };
        if (attempt.IsExam)
            lines.Add($"得分 {tag.Similarity:P0}");
        if (tag.EFValue is { } ef)
            lines.Add($"EF {ef:0.00}");
        if (tag.Stability is { } stability)
            lines.Add($"稳定度 {stability:0.##} 天");
        if (tag.Difficulty is { } difficulty)
            lines.Add($"难度 {difficulty:0.##}");
        if (tag.Retrievability is { } recall)
            lines.Add($"作答前可回忆率 {recall:P0}");
        if (tag.ElapsedDays is { } elapsed)
            lines.Add($"距上次复习 {elapsed} 天");
        if (tag.ResponseSeconds is { } seconds)
            lines.Add($"作答用时 {seconds:0.#} 秒");
        if (tag.NextIntervalDays is { } interval)
            lines.Add($"下次复习间隔 {interval:0.##} 天");
        return string.Join('\n', lines);
    }

    private void HistoryScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Shift || HistoryScroll.ScrollableWidth <= 0) return;
        HistoryScroll.ScrollToHorizontalOffset(HistoryScroll.HorizontalOffset - e.Delta / 120d * Spacing * 2);
        e.Handled = true;
    }

    private static Color Tint(Color color) => Color.FromArgb(90, color.R, color.G, color.B);

    private static SolidColorBrush Brush(int rgb)
    {
        var brush = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        brush.Freeze();
        return brush;
    }
}
