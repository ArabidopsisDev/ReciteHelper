using ReciteHelper.Model;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace ReciteHelper.ViewModel;

public class QuestionItem : INotifyPropertyChanged
{
    public int Number { get; set; }
    public Question? Question { get; set; }
    public AnswerStatus Status { get; set; }
    public string? UserAnswer { get; set; }
    public List<ReviewTag> ReviewTag { get; set; } = [];

    public Style? StatusStyle
    {
        get => field;
        set
        {
            field = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public enum AnswerStatus
{
    NotAnswered,
    Correct,
    Wrong
}

