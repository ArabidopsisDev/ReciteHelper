using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Services;
using ReciteHelper.Wpf.ViewModels;
using System.Windows;

namespace ReciteHelper.Wpf.Views;

public partial class ClearReviewRecordsWindow : Window
{
    private readonly ClearQuestion[] _questions;
    private readonly Question[] _archiveCandidates;
    private readonly HashSet<Question> _facetsCoveredByArchive;
    private readonly ClearQuestion[] _wrongQuestions;

    internal ClearReviewRecordsWindow(IEnumerable<QuestionItem> questions)
    {
        InitializeComponent();
        _questions = questions.Select(item =>
        {
            var question = item.Question!;
            var stem = EssayAnswerLayout.Stem(question, EssayAnswerLayout.Parts(question));
            return new ClearQuestion(question, item.Number, stem, question.Status);
        }).ToArray();
        _wrongQuestions = _questions.Where(item => item.LastResult is false).ToArray();

        // Clearing an original also clears its facets. When both are listed, archive at the level of the original.
        var facetsOfGradedQuestions = _questions.Where(item => item.LastResult is not null)
            .SelectMany(item => item.Question.ReviewFacets.Select(facet => facet.Question))
            .ToHashSet<Question>(ReferenceEqualityComparer.Instance);
        _archiveCandidates = _questions.Where(item => item.LastResult is true && !facetsOfGradedQuestions.Contains(item.Question))
            .Select(item => item.Question).ToArray();
        _facetsCoveredByArchive = _archiveCandidates.SelectMany(question => question.ReviewFacets.Select(facet => facet.Question))
            .ToHashSet<Question>(ReferenceEqualityComparer.Instance);

        QuestionList.ItemsSource = _questions;
        ArchiveCorrectCheckBox.Content = $"同时归档答对的题（{_archiveCandidates.Length} 道）";
        AllQuestionsOption.Checked += (_, _) => UpdateScope();
        WrongQuestionsOption.Checked += (_, _) => UpdateScope();
        PickQuestionsOption.Checked += (_, _) => UpdateScope();
        ArchiveCorrectCheckBox.Checked += (_, _) => UpdateSelection();
        ArchiveCorrectCheckBox.Unchecked += (_, _) => UpdateSelection();
        QuestionList.SelectionChanged += (_, _) => UpdateSelection();
        UpdateScope();
    }

    internal bool AnswersOnly => AnswersOnlyOption.IsChecked is true;
    internal IReadOnlyList<Question> QuestionsToClear { get; private set; } = [];
    internal IReadOnlyList<Question> QuestionsToArchive { get; private set; } = [];

    private bool ArchiveCorrect => WrongQuestionsOption.IsChecked is true && ArchiveCorrectCheckBox.IsChecked is true;

    private IEnumerable<ClearQuestion> SelectedQuestions()
    {
        if (PickQuestionsOption.IsChecked is true) return QuestionList.SelectedItems.Cast<ClearQuestion>();
        if (WrongQuestionsOption.IsChecked is true)
            return ArchiveCorrect
                ? _wrongQuestions.Where(item => !_facetsCoveredByArchive.Contains(item.Question))
                : _wrongQuestions;
        return _questions;
    }

    private void UpdateScope()
    {
        var allowArchive = WrongQuestionsOption.IsChecked is true && _archiveCandidates.Length > 0;
        if (!allowArchive) ArchiveCorrectCheckBox.IsChecked = false;
        ArchiveWithWrongPanel.Visibility = allowArchive ? Visibility.Visible : Visibility.Collapsed;
        QuestionList.Visibility = PickQuestionsOption.IsChecked is true ? Visibility.Visible : Visibility.Collapsed;
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        var count = SelectedQuestions().Count();
        ScopeText.Text = PickQuestionsOption.IsChecked is true ? $"已勾选 {count} 道题。"
            : WrongQuestionsOption.IsChecked is true ? $"将清空当前标签页的 {count} 道错题。"
            : $"范围为当前标签页的 {count} 道未归档题。";
        ClearModePanel.IsEnabled = count > 0;
        ConfirmButton.Content = ArchiveCorrect ? count > 0 ? "清空错题并归档" : "确认归档" : "确认清空";
        ConfirmButton.IsEnabled = count > 0 || ArchiveCorrect;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        QuestionsToClear = SelectedQuestions().Select(item => item.Question).ToArray();
        if (ArchiveCorrect) QuestionsToArchive = _archiveCandidates;
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
