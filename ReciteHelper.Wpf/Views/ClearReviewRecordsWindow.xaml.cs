using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Services;
using ReciteHelper.Wpf.ViewModels;
using System.Windows;

namespace ReciteHelper.Wpf.Views;

public partial class ClearReviewRecordsWindow : Window
{
    private readonly ClearQuestion[] _questions;

    internal ClearReviewRecordsWindow(IEnumerable<QuestionItem> questions)
    {
        InitializeComponent();
        _questions = questions.Select(item =>
        {
            var question = item.Question!;
            var stem = EssayAnswerLayout.Stem(question, EssayAnswerLayout.Parts(question));
            return new ClearQuestion(question, item.Number, stem, question.Status);
        }).ToArray();
        QuestionList.ItemsSource = _questions;
        AllQuestionsOption.Checked += (_, _) => UpdateScope();
        WrongQuestionsOption.Checked += (_, _) => UpdateScope();
        PickQuestionsOption.Checked += (_, _) => UpdateScope();
        QuestionList.SelectionChanged += (_, _) => UpdateScope();
        UpdateScope();
    }

    internal bool AnswersOnly => AnswersOnlyOption.IsChecked is true;
    internal IReadOnlyList<Question> QuestionsToClear { get; private set; } = [];

    private IEnumerable<ClearQuestion> SelectedQuestions()
    {
        if (PickQuestionsOption.IsChecked is true) return QuestionList.SelectedItems.Cast<ClearQuestion>();
        if (WrongQuestionsOption.IsChecked is true) return _questions.Where(question => question.LastResult is false);
        return _questions;
    }

    private void UpdateScope()
    {
        var picking = PickQuestionsOption.IsChecked is true;
        var count = SelectedQuestions().Count();
        QuestionList.Visibility = picking ? Visibility.Visible : Visibility.Collapsed;
        ScopeText.Text = picking ? $"已勾选 {count} 道题。" : WrongQuestionsOption.IsChecked is true
            ? $"当前标签页最近一次作答判错的题共有 {count} 道。"
            : $"范围为当前标签页的全部 {count} 道题。";
        ConfirmButton.IsEnabled = count > 0;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        QuestionsToClear = SelectedQuestions().Select(question => question.Question).ToArray();
        DialogResult = true;
    }

    private sealed record ClearQuestion(Question Question, int Number, string Stem, bool? LastResult)
    {
        public string StatusText => LastResult switch
        {
            true => "上次判对",
            false => "上次判错",
            null => "未判定"
        };
    }
}
