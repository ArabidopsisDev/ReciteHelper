using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Enums;
using ReciteHelper.Core.ValueObjects;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ReciteHelper.Wpf.Models;

public sealed class ExamQuestionItem : INotifyPropertyChanged
{
    private string _userAnswer = string.Empty;
    private ExamAnswerStatus _status;

    public int Number { get; set; }
    public Question? Question { get; set; }
    internal Question? HistorySource { get; set; }
    internal int? ImportedIndex { get; init; }
    public int Score { get; set; }
    public Core.DTOs.AiAnswerAssessment? AiAssessment { get; set; }
    public double EarnedScore { get; set; }
    public string Explanation { get; set; } = string.Empty;

    public string UserAnswer
    {
        get => _userAnswer;
        set
        {
            var normalized = value ?? string.Empty;
            if (_userAnswer == normalized)
                return;

            _userAnswer = normalized;
            Status = string.IsNullOrWhiteSpace(_userAnswer)
                ? ExamAnswerStatus.NotAnswered
                : ExamAnswerStatus.Answered;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAnswered));
        }
    }

    public ExamAnswerStatus Status
    {
        get => _status;
        set
        {
            if (_status == value)
                return;

            _status = value;
            OnPropertyChanged();
        }
    }

    public string QuestionText => Question?.Text ?? string.Empty;
    public bool IsSingleChoice => Question?.IsSingleChoice is true;
    public bool IsFillBlank => Question?.IsFillBlank is true;
    public bool IsTrueFalse => Question?.IsTrueFalse is true;
    public bool IsTermDefinition => Question?.IsTermDefinition is true;
    public bool IsAnswered
    {
        get
        {
            if (!IsFillBlank)
                return !string.IsNullOrWhiteSpace(UserAnswer);

            var answers = ReciteHelper.Core.Entities.Question.SplitBlankAnswers(UserAnswer);
            return answers.Count == (Question?.BlankCount ?? 0) &&
                   answers.All(answer => !string.IsNullOrWhiteSpace(answer));
        }
    }
    public IReadOnlyList<QuestionOption> Options => Question?.Options ?? [];
    public string ScoreLabel => $"（{Score}分）";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
