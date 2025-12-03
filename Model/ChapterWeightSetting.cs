using System.ComponentModel;

namespace ReciteHelper.Model;

/// <summary>
/// Represents the weight configuration for a specific chapter, including the chapter name, the number of questions, and
/// the assigned weight.
/// </summary>
/// <remarks>This class is typically used in scenarios where chapters are weighted differently, such as in test
/// generation or scoring systems. It implements <see cref="INotifyPropertyChanged"/> to support data binding and notify
/// clients when property values change.</remarks>
public class ChapterWeightSetting : INotifyPropertyChanged
{
    private string _chapterName;
    private int _questionCount;
    private double _weight;

    public string ChapterName
    {
        get => _chapterName;
        set
        {
            _chapterName = value;
            OnPropertyChanged(nameof(ChapterName));
        }
    }

    public int QuestionCount
    {
        get => _questionCount;
        set
        {
            _questionCount = value;
            OnPropertyChanged(nameof(QuestionCount));
        }
    }

    public double Weight
    {
        get => _weight;
        set
        {
            _weight = value;
            OnPropertyChanged(nameof(Weight));
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected virtual void OnPropertyChanged(string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
