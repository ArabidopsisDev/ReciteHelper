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
    public string AnswerText => EssayAnswerLayout.Join(_inputs.Select(input => input.Text).ToList());

    public void SetQuestion(Question question, string? answer, bool editable, bool paper = false)
    {
        FontFamily = AppFonts.Serif;
        FontWeight = FontWeights.Normal;
        _panel.Children.Clear();
        _inputs.Clear();
        _feedback.Clear();
        var parts = EssayAnswerLayout.Parts(question);
        var answers = EssayAnswerLayout.Split(answer, parts.Count);
        Content = paper ? new ScrollViewer { Content = _panel, MaxHeight = 440, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } : _panel;
        for (var i = 0; i < parts.Count; i++)
        {
            var title = new TextBlock { Text = $"（{i + 1}）{parts[i].Prompt}", TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, i == 0 ? 0 : 24, 0, 12), FontSize = paper ? 15 : 14 };
            _panel.Children.Add(title);
            var input = new TextBox { Text = answers[i], AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top,
                Height = paper ? 80 : 130, Padding = new Thickness(paper ? 8 : 16), FontSize = 16,
                FontFamily = AppFonts.Serif, FontWeight = FontWeights.Normal,
                IsReadOnly = !editable, IsTabStop = editable };
            if (paper) input.Style = null;
            input.TextChanged += (_, _) => AnswerChanged?.Invoke(this, EventArgs.Empty);
            _inputs.Add(input);
            _panel.Children.Add(input);
            var feedback = new Border { Padding = new Thickness(paper ? 8 : 16), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(0), MinHeight = 70, Visibility = Visibility.Collapsed };
            feedback.SetResourceReference(Border.BorderBrushProperty, "ArcBorder");
            if (!paper) feedback.SetResourceReference(Border.CornerRadiusProperty, "ArcRadiusControl");
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
                FontSize = 16, LineHeight = 27, TextWrapping = TextWrapping.Wrap };
            _feedback[i].Visibility = Visibility.Visible;
        }
    }
}
