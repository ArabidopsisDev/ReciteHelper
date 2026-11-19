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

    private const double MinimumSpacing = 62;
    private const double PlotTop = 29;
    private const double ScoreHeight = 106;
    private const double PlotHeight = PlotTop + ScoreHeight + 27;
    private static readonly double[] Ticks = [1, 0.75, 0.5, 0.25, 0];
    private static readonly Color LowScore = Color.FromRgb(178, 76, 96);
    private static readonly Color MidScore = Color.FromRgb(181, 129, 53);
    private static readonly Color HighScore = Color.FromRgb(33, 134, 112);

    private readonly record struct Attempt(ReviewTag Tag, bool IsExam, double Score, bool UsesLegacyValue = false);
    private Attempt[] _attempts = [];

    public AnswerHistoryTimeline()
    {
        InitializeComponent();
        Plot.Height = PlotHeight;
        foreach (var tick in Ticks)
        {
            var label = new TextBlock
            {
                Text = Percent(tick), FontSize = 11, Height = 16, Width = 34, TextAlignment = TextAlignment.Right
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "ArcTextSecondary");
            Canvas.SetTop(label, ScoreY(tick) - 8);
            ScoreAxis.Children.Add(label);
        }
    }

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
        var lastReview = question?.ReviewTag.MaxBy(tag => tag.Time);
        _attempts = question?.ReviewTag
            .Where(tag => tag.OptimizerWeight > 0)
            .Select(tag => PracticeAttempt(tag, question, ReferenceEquals(tag, lastReview)))
            .Concat((question.ExamHistory ?? []).Select(tag => new Attempt(tag, true, Normalize(tag.ScoreRatio ?? tag.Similarity))))
            .OrderBy(attempt => attempt.Tag.Time).ToArray() ?? [];
        Visibility = _attempts.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        CountText.Text = $"{_attempts.Length} 次";
        DrawPlot();
        HistoryScroll.ScrollToRightEnd();
    }

    private void DrawPlot()
    {
        Plot.Children.Clear();
        if (_attempts.Length == 0) return;

        var width = Math.Max(HistoryScroll.ViewportWidth, MinimumSpacing * _attempts.Length);
        var spacing = Math.Clamp(width / _attempts.Length, MinimumSpacing, 94);
        Plot.Width = width;
        var startX = (width - spacing * (_attempts.Length - 1)) / 2;

        foreach (var tick in Ticks)
        {
            var y = ScoreY(tick);
            var line = new Line { X2 = width, Y1 = y, Y2 = y, StrokeThickness = 1, IsHitTestVisible = false };
            line.SetResourceReference(Shape.StrokeProperty, tick == 0 ? "ArcBorderStrong" : "ArcBorder");
            if (tick > 0) line.StrokeDashArray = new DoubleCollection { 2, 4 };
            Plot.Children.Add(line);
        }

        var points = _attempts.Select((attempt, index) => new Point(startX + spacing * index, ScoreY(attempt.Score))).ToArray();
        for (var i = 1; i < points.Length; i++)
        {
            var from = _attempts[i - 1].Score;
            var to = _attempts[i].Score;
            var fill = new LinearGradientBrush(ScoreColor(from), ScoreColor(to),
                new Point(0, 0), new Point(1, 0)) { Opacity = 0.6 };
            if ((from - 0.5) * (to - 0.5) < 0)
                fill.GradientStops.Add(new GradientStop(MidScore, (0.5 - from) / (to - from)));
            fill.Freeze();
            Plot.Children.Add(new Path { Data = Ribbon(points[i - 1], points[i]), Fill = fill, IsHitTestVisible = false });
        }

        for (var i = 0; i < points.Length; i++)
            AddNode(_attempts[i], points[i], i + 1, spacing);
    }

    // Adapted from TKCode.Arc's SankeyFlow.Render.cs (MIT).
    private static StreamGeometry Ribbon(Point from, Point to)
    {
        const double half = 1.7;
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

    private void AddNode(Attempt attempt, Point center, int number, double spacing)
    {
        const double markerSize = 24;
        const double valueHeight = 17;
        var color = new SolidColorBrush(ScoreColor(attempt.Score));
        color.Freeze();
        var halo = new Ellipse { Stroke = color, StrokeThickness = 1.5, Opacity = 0 };
        var dot = new Ellipse { Width = 18, Height = 18, Fill = color, Stroke = Brushes.White, StrokeThickness = 2 };
        var marker = new Grid { Width = markerSize, Height = markerSize, Children = { halo, dot } };
        var value = new TextBlock
        {
            Text = Percent(attempt.Score), FontSize = 12, FontWeight = FontWeights.Medium,
            Height = valueHeight, TextAlignment = TextAlignment.Center
        };
        value.SetResourceReference(TextBlock.ForegroundProperty, "ArcForeground");
        var tooltip = Describe(attempt, number);
        var node = new Border
        {
            Width = Math.Min(spacing, 56), Background = Brushes.Transparent, Focusable = true,
            ToolTip = tooltip, Child = new StackPanel { Children = { value, marker } }
        };
        AutomationProperties.SetName(node, tooltip);
        ToolTipService.SetShowDuration(node, 20000);
        void UpdateHalo() => halo.Opacity = node.IsKeyboardFocused || node.IsMouseOver ? 0.65 : 0;
        node.MouseEnter += (_, _) => UpdateHalo();
        node.MouseLeave += (_, _) => UpdateHalo();
        node.IsKeyboardFocusedChanged += (_, _) => UpdateHalo();
        Canvas.SetLeft(node, center.X - node.Width / 2);
        Canvas.SetTop(node, center.Y - valueHeight - markerSize / 2);
        Plot.Children.Add(node);

        var tick = new Line
        {
            X1 = center.X, X2 = center.X, Y1 = ScoreY(0), Y2 = ScoreY(0) + 4, StrokeThickness = 1, IsHitTestVisible = false
        };
        tick.SetResourceReference(Shape.StrokeProperty, "ArcBorderStrong");
        Plot.Children.Add(tick);
        var label = new TextBlock { Text = $"#{number}", FontSize = 11, Width = spacing, TextAlignment = TextAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "ArcTextMuted");
        Canvas.SetLeft(label, center.X - spacing / 2);
        Canvas.SetTop(label, ScoreY(0) + 7);
        Plot.Children.Add(label);
    }

    private static Attempt PracticeAttempt(ReviewTag tag, Question question, bool isLatest)
    {
        if (tag.ScoreRatio is { } score) return new Attempt(tag, false, Normalize(score));
        if (isLatest && question.LastAiAssessment?.RawScoreRatio is { } raw)
            return new Attempt(tag, false, Normalize(raw));
        if (question.IsSingleChoice || question.IsTrueFalse)
        {
            var correct = tag.IsCorrect ?? ReviewGrader.IsRecall(tag.Grade > 0
                ? (ReviewGrade)tag.Grade : ReviewGrader.FromLegacyQuality(tag.QValue));
            return new Attempt(tag, false, correct ? 1 : 0);
        }
        return new Attempt(tag, false, Normalize(tag.Similarity), UsesLegacyValue: true);
    }

    private static double ScoreY(double score) => PlotTop + (1 - score) * ScoreHeight;

    private static double Normalize(double recorded) => Math.Clamp(recorded > 1 ? recorded / 100 : recorded, 0, 1);

    private static string Percent(double score) => $"{score * 100:0.#}%";

    private static Color ScoreColor(double score)
    {
        var from = score <= 0.5 ? LowScore : MidScore;
        var to = score <= 0.5 ? MidScore : HighScore;
        var fraction = score <= 0.5 ? score * 2 : (score - 0.5) * 2;
        byte Mix(byte a, byte b) => (byte)Math.Round(a + (b - a) * fraction);
        return Color.FromRgb(Mix(from.R, to.R), Mix(from.G, to.G), Mix(from.B, to.B));
    }

    private static string Describe(Attempt attempt, int number)
    {
        var tag = attempt.Tag;
        var kind = attempt.IsExam ? "考试" : "复习";
        var time = tag.Time.Kind == DateTimeKind.Utc ? tag.Time.ToLocalTime() : tag.Time;
        var scoreLabel = attempt.UsesLegacyValue ? "记录值" : "得分";
        var lines = new List<string>
        {
            $"第 {number} 次{kind}  {scoreLabel} {Percent(attempt.Score)}",
            time.ToString("yyyy-MM-dd HH:mm:ss")
        };
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
        HistoryScroll.ScrollToHorizontalOffset(HistoryScroll.HorizontalOffset - e.Delta / 120d * MinimumSpacing * 2);
        e.Handled = true;
    }

    private void HistoryScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ViewportWidthChange != 0 && _attempts.Length > 0) DrawPlot();
    }
}
