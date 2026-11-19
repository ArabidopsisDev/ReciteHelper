using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Services;
using ReciteHelper.Wpf.Utilities;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ReciteHelper.Wpf.Controls;

public sealed class MultipartAnswerEditor : UserControl
{
    private readonly StackPanel _panel = new();
    private readonly List<TextBox> _inputs = [];
    private readonly List<Border> _feedback = [];
    public event EventHandler? AnswerChanged;
    public IReadOnlyList<string> AnswerTexts => _inputs.Select(input => input.Text).ToArray();
    public string AnswerText => EssayAnswerLayout.Join(AnswerTexts);

    public void SetQuestion(Question question, string? answer, bool editable, bool paper = false, IReadOnlyList<string>? draft = null)
    {
        FontFamily = AppFonts.Serif;
        FontWeight = FontWeights.Normal;
        _panel.Children.Clear();
        _inputs.Clear();
        _feedback.Clear();
        var parts = EssayAnswerLayout.Parts(question);
        var answers = draft ?? EssayAnswerLayout.Split(answer, parts.Count);
        Content = paper ? new ScrollViewer { Content = _panel, MaxHeight = 440, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } : _panel;
        for (var i = 0; i < parts.Count; i++)
        {
            var title = new TextBlock { Text = EssayAnswerLayout.PartHeading(parts[i], i), TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, i == 0 ? 0 : 20, 0, 8), FontSize = paper ? 15 : 17, LineHeight = 28 };
            _panel.Children.Add(title);
            var input = new TextBox { Text = i < answers.Count ? answers[i] : string.Empty, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top,
                Height = paper ? 80 : 116, Padding = new Thickness(paper ? 8 : 12), FontSize = paper ? 16 : 18,
                FontFamily = AppFonts.Serif, FontWeight = FontWeights.Normal,
                IsReadOnly = !editable, IsTabStop = editable };
            if (paper) input.Style = null;
            input.TextChanged += (_, _) => AnswerChanged?.Invoke(this, EventArgs.Empty);
            _inputs.Add(input);
            _panel.Children.Add(input);
            var feedback = new Border
            {
                Padding = paper ? new Thickness(8) : new Thickness(14, 4, 0, 4),
                BorderThickness = paper ? new Thickness(1) : new Thickness(2, 0, 0, 0),
                Visibility = Visibility.Collapsed
            };
            feedback.SetResourceReference(Border.BorderBrushProperty, paper ? "ArcBorder" : "ArcBorderStrong");
            _feedback.Add(feedback);
            _panel.Children.Add(feedback);
        }
    }

    public void SetAssessment(AiAnswerAssessment assessment)
    {
        for (var i = 0; i < _inputs.Count; i++)
        {
            _inputs[i].Visibility = Visibility.Collapsed;
            _feedback[i].Child = new AiAnnotatedAnswer { Assessment = EssayAnswerLayout.ForPart(assessment, i, _inputs.Count),
                FontSize = _inputs[i].FontSize };
            _feedback[i].Visibility = Visibility.Visible;
        }
    }
}
