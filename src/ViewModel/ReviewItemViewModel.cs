using System.ComponentModel;
using System.Windows;

namespace ReciteHelper.ViewModel;

public class ReviewItemViewModel : INotifyPropertyChanged
{
    public int QuestionNumber { get; set; }
    public string? QuestionContent { get; set; }
    public string? UserAnswer { get; set; }
    public string? CorrectAnswer { get; set; }
    public string? Explanation { get; set; }
    public bool IsCorrect { get; set; }
    public Style? ItemStyle { get; set; }

    public bool HasExplanation => !string.IsNullOrEmpty(Explanation);

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged(string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

