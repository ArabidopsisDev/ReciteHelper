using Microsoft.Win32;
using ReciteHelper.Application.Services;
using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Core.Services;
using ReciteHelper.Infrastructure.Services;
using ReciteHelper.Wpf.Utilities;
using ReciteHelper.Wpf.Controls;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ReciteHelper.Wpf.Views;

public partial class ReviewMistakesView : UserControl, IWorkspacePaneLayout
{
    private readonly Project _project;
    private readonly IExamSetRepository? _examSets;
    private IReadOnlyList<ReviewMistake> _entries = [];
    private IReadOnlyList<ReviewMistake> _externalEntries = [];
    private CancellationTokenSource? _loading;
    private bool _horizontal;
    private bool _showColumns;
    private static readonly Brush MissingForeground = TextBrush(MistakeParagraph.MissingForeground);
    private static readonly Brush MissingBackground = TextBrush(MistakeParagraph.MissingBackground);
    private static readonly Brush IncorrectForeground = TextBrush(MistakeParagraph.IncorrectForeground);

    internal ReviewMistakesView(Project project, IExamSetRepository? examSets)
    {
        InitializeComponent();
        _project = project;
        _examSets = examSets;
        ListViewer.SizeChanged += (_, _) => UpdateColumns();
        Loaded += (_, _) =>
        {
            _project.ReviewMistakesChanged += MistakesChanged;
            Refresh();
            _ = RefreshExamMistakesAsync();
        };
        Unloaded += (_, _) =>
        {
            _project.ReviewMistakesChanged -= MistakesChanged;
            _loading?.Cancel();
        };
        Refresh();
    }

    private void MistakesChanged(object? sender, bool externalExam) => Dispatcher.BeginInvoke(() =>
    {
        if (!IsLoaded) return;
        if (externalExam) _ = RefreshExamMistakesAsync();
        else Refresh();
    });

    private async Task RefreshExamMistakesAsync()
    {
        if (_examSets is null) return;
        _loading?.Cancel();
        var loading = _loading = new CancellationTokenSource();
        try
        {
            var sets = await _examSets.LoadAllAsync(_project, loading.Token);
            loading.Token.ThrowIfCancellationRequested();
            _externalEntries = sets.SelectMany(set => set.ReviewMistakes).ToArray();
            Refresh();
        }
        catch (OperationCanceledException) when (loading.IsCancellationRequested) { }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            System.Diagnostics.Debug.WriteLine(exception);
        }
        finally
        {
            if (ReferenceEquals(_loading, loading)) _loading = null;
            loading.Dispose();
        }
    }

    internal void Refresh()
    {
        var entries = ReviewMistakes.Read(_project).Concat(_externalEntries).OrderByDescending(entry => entry.Time).ToArray();
        if (ListViewer.Document is not null && entries.SequenceEqual(_entries)) return;
        _entries = entries;
        RenderEntries();
    }

    public void SetHorizontalLayout(bool horizontal)
    {
        if (_horizontal == horizontal) return;
        _horizontal = horizontal;
        UpdateColumns();
    }

    private void UpdateColumns()
    {
        var showColumns = _horizontal && ListViewer.ActualWidth >= 650;
        if (_showColumns == showColumns) return;
        _showColumns = showColumns;
        RenderEntries();
    }

    private void RenderEntries()
    {
        CountText.Text = $"{_entries.Count} 条作答记录";
        ExportButton.IsEnabled = _entries.Count > 0;
        var document = new FlowDocument { FontFamily = AppFonts.Serif, FontSize = 15, PagePadding = new Thickness(16, 6, 16, 18) };
        document.SetResourceReference(FlowDocument.ForegroundProperty, "ArcForeground");
        if (_entries.Count == 0)
            document.Blocks.Add(new Paragraph(new Run("批改后的错误和遗漏会显示在这里。")));
        else
            document.Blocks.Add(new Paragraph
            {
                FontSize = 12,
                Margin = new Thickness(0, 7, 0, 3),
                Inlines = { StyledRun(new MistakeTextRun("遗漏", MistakeTextStyle.Missing)),
                    new Run("  "), StyledRun(new MistakeTextRun("需订正", MistakeTextStyle.Incorrect)) }
            });

        TableRowGroup? rows = null;
        if (_showColumns && _entries.Count > 1)
        {
            rows = new TableRowGroup();
            var table = new Table
            {
                CellSpacing = 0, Columns = { new TableColumn { Width = new GridLength(1, GridUnitType.Star) },
                    new TableColumn { Width = new GridLength(1, GridUnitType.Star) } },
                RowGroups = { rows }
            };
            document.Blocks.Add(table);
        }
        for (var index = 0; index < _entries.Count; index++)
        {
            var entry = _entries[index];
            var question = new Paragraph(new Run(entry.Question)) { FontWeight = FontWeights.Medium, Margin = new Thickness(0, 12, 0, 4) };
            var metadata = new Paragraph(new Run($"{entry.Time.ToLocalTime():MM-dd HH:mm}  {(entry.IsExam ? "考试" : "练习")}"))
                { FontSize = 11, Margin = new Thickness(0, 0, 0, 8) };
            metadata.SetResourceReference(Block.ForegroundProperty, "ArcTextSecondary");
            var heading = new Section { Blocks = { question, metadata } };
            var details = new Paragraph { Margin = new Thickness(0, 0, 0, 9), LineHeight = 25 };
            foreach (var run in MistakeParagraph.Format(entry.Corrections))
                details.Inlines.Add(StyledRun(run));
            heading.Blocks.Add(details);
            if (rows is null)
                document.Blocks.Add(heading);
            else
            {
                var left = index % 2 == 0;
                if (left) rows.Rows.Add(new TableRow());
                rows.Rows[^1].Cells.Add(new TableCell(heading)
                {
                    Padding = left ? new Thickness(0, 0, 12, 0) : new Thickness(12, 0, 0, 0)
                });
            }
        }
        ListViewer.Document = document;
    }

    private static Run StyledRun(MistakeTextRun entry)
    {
        var run = new Run(entry.Text);
        if (entry.Style == MistakeTextStyle.Missing)
        {
            run.Foreground = MissingForeground;
            run.Background = MissingBackground;
        }
        else if (entry.Style == MistakeTextStyle.Incorrect)
            run.Foreground = IncorrectForeground;
        return run;
    }

    private static Brush TextBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#" + hex));
        brush.Freeze();
        return brush;
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var name = _project.ProjectName ?? "复习项目";
        var dialog = new SaveFileDialog { FileName = $"{name} 错误列表.docx", Filter = "Word 文档 (*.docx)|*.docx", DefaultExt = ".docx" };
        if (dialog.ShowDialog(Window.GetWindow(this)) is not true) return;
        try { MistakeDocumentExporter.Export(dialog.FileName, name, _entries); }
        catch (IOException ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, "无法导出错误列表", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
