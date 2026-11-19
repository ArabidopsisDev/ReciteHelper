using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Wpf.Models;
using ReciteHelper.Wpf.Controls;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ReciteHelper.Wpf.Views;

public partial class QuestionHelpView : UserControl, IWorkspacePaneLayout
{
    private readonly Project _project;
    private readonly IQuestionHelpService _help;
    private Question? _question;
    private string? _answer;
    private CancellationTokenSource? _work;
    private IReadOnlyList<KnowledgeBaseMatch> _matches = [];
    private bool _hasResult;
    private bool _horizontal;
    private bool _hasExplanation;
    private static readonly Brush Highlight = new SolidColorBrush(Color.FromRgb(208, 239, 219));

    internal QuestionHelpView(Project project, IQuestionHelpService help)
    {
        InitializeComponent();
        _project = project;
        _help = help;
    }

    internal void SetQuestion(Question? question, string? answer)
    {
        if (ReferenceEquals(_question, question) && _answer == answer) return;
        _work?.Cancel();
        _work?.Dispose();
        _work = null;
        _question = question;
        _answer = answer;
        _matches = [];
        _hasResult = false;
        Passages.Children.Clear();
        _hasExplanation = false;
        ExplanationText.Text = string.Empty;
        UpdateExplanationVisibility();
        GenerateButton.Content = "生成解析";
        GenerateButton.IsEnabled = false;
        ReadingScroll.ScrollToTop();
        ExplanationScroll.ScrollToTop();
        SearchStatus.Text = question is null ? "作答后可以查看这道题的资料。" : "正在检索资料…";
        SearchStatus.ToolTip = null;
        SearchStatus.Visibility = Visibility.Visible;
        if (question is null) return;
        _work = new CancellationTokenSource();
        _ = LoadAsync(question, _work.Token);
    }

    private async Task LoadAsync(Question question, CancellationToken token)
    {
        try
        {
            var matches = await _help.FindMatchesAsync(_project, question, token);
            token.ThrowIfCancellationRequested();
            _matches = matches;
            foreach (var match in matches) Passages.Children.Add(CreatePassage(match));
            SearchStatus.Text = "这道题暂时没有检索到相关资料。";
            SearchStatus.Visibility = matches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested) return;
            SearchStatus.Text = "暂时无法检索资料。";
            SearchStatus.ToolTip = ex.Message;
        }
        GenerateButton.IsEnabled = true;
        if (Config.Configure.AutoGenerateQuestionExplanation)
            await GenerateAsync(scrollToResult: false);
    }

    private async void Generate_Click(object sender, RoutedEventArgs e) => await GenerateAsync(scrollToResult: true);

    private async Task GenerateAsync(bool scrollToResult)
    {
        if (_question is not { } question || _work is not { } work || !GenerateButton.IsEnabled) return;
        var token = work.Token;
        GenerateButton.IsEnabled = false;
        _hasExplanation = true;
        UpdateExplanationVisibility();
        ExplanationText.Text = "正在生成解析…";
        if (scrollToResult) Explanation.BringIntoView();
        try
        {
            var result = await _help.ExplainAsync(question, _answer ?? string.Empty, _matches, token);
            token.ThrowIfCancellationRequested();
            ExplanationText.Text = result.Trim();
            _hasResult = !string.IsNullOrWhiteSpace(result);
            if (!_hasResult) ExplanationText.Text = "没有收到解析，可以重试。";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested) ExplanationText.Text = $"生成解析失败。{ex.Message}";
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                GenerateButton.IsEnabled = true;
                GenerateButton.Content = _hasResult ? "重新生成解析" : "生成解析";
            }
        }
    }

    private void UpdateExplanationVisibility() => Explanation.Visibility = _horizontal || _hasExplanation
        ? Visibility.Visible : Visibility.Collapsed;

    public void SetHorizontalLayout(bool horizontal)
    {
        if (_horizontal == horizontal) return;
        _horizontal = horizontal;
        if (horizontal)
        {
            ReadingContent.Children.Remove(Explanation);
            ExplanationScroll.Content = Explanation;
        }
        else
        {
            ExplanationScroll.Content = null;
            ReadingContent.Children.Add(Explanation);
        }
        var visible = horizontal ? Visibility.Visible : Visibility.Collapsed;
        DividerColumn.Width = new GridLength(horizontal ? 5 : 0);
        ExplanationColumn.Width = horizontal ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        ExplanationDivider.Visibility = ExplanationScroll.Visibility = visible;
        Explanation.Margin = new Thickness(0, horizontal ? 0 : 14, 0, 0);
        UpdateExplanationVisibility();
    }

    private Border CreatePassage(KnowledgeBaseMatch match)
    {
        var title = new TextBlock { Text = match.Title, FontSize = 12, FontWeight = FontWeights.Medium, TextWrapping = TextWrapping.Wrap };
        title.SetResourceReference(TextBlock.ForegroundProperty, "ArcTextSecondary");
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0), LineHeight = 24 };
        text.SetResourceReference(StyleProperty, "ReadingTextStyle");
        var spans = new List<(int Start, int End)>();
        foreach (var key in match.Highlights.Where(key => key.Length > 0).OrderByDescending(key => key.Length))
        {
            var start = match.Content.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (start < 0 || spans.Any(span => start < span.End && start + key.Length > span.Start)) continue;
            spans.Add((start, start + key.Length));
        }
        var cursor = 0;
        foreach (var (start, end) in spans.OrderBy(span => span.Start))
        {
            text.Inlines.Add(new Run(match.Content[cursor..start]));
            text.Inlines.Add(new Run(match.Content[start..end]) { Background = Highlight });
            cursor = end;
        }
        text.Inlines.Add(new Run(match.Content[cursor..]));
        var row = new Border { Padding = new Thickness(0, 10, 0, 12), BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new StackPanel { Children = { title, text } } };
        row.SetResourceReference(Border.BorderBrushProperty, "ArcBorder");
        return row;
    }
}
