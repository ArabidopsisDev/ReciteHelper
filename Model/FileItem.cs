using System.ComponentModel;

namespace ReciteHelper.Model;

/// <summary>
/// Represents a file and its associated metadata for use in file management or display scenarios.
/// </summary>
/// <remarks>The FileItem class implements INotifyPropertyChanged to support data binding scenarios, such as
/// updating UI elements when file properties change. Each property corresponds to a specific attribute of a file,
/// making this class suitable for use in file explorers, lists, or other components that display file
/// information.</remarks>
public class FileItem : INotifyPropertyChanged
{
    public int Index
    {
        get => field;
        set
        {
            field = value;
            OnPropertyChanged(nameof(Index));
        }
    }

    public string FilePath
    {
        get => field;
        set
        {
            field = value;
            OnPropertyChanged(nameof(FilePath));
        }
    }

    public string FileName
    {
        get => field;
        set
        {
            field = value;
            OnPropertyChanged(nameof(FileName));
        }
    }

    public string FileExtension
    {
        get => field;
        set
        {
            field = value;
            OnPropertyChanged(nameof(FileExtension));
        }
    }

    public string FileSize
    {
        get => field;
        set
        {
            field = value;
            OnPropertyChanged(nameof(FileSize));
        }
    }

    public string LastModified
    {
        get => field;
        set
        {
            field = value;
            OnPropertyChanged(nameof(LastModified));
        }
    }

    public long FileSizeBytes
    {
        get => field;
        set
        {
            field = value;
            OnPropertyChanged(nameof(FileSizeBytes));
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected virtual void OnPropertyChanged(string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
