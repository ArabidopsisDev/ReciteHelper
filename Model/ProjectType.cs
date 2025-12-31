using ReciteHelper.View;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace ReciteHelper.Model;

/// <summary>
/// Represents a project type with associated metadata, such as name, description, icon, and template category.
/// </summary>
/// <remarks>This class implements <see cref="INotifyPropertyChanged"/>, enabling data binding scenarios where UI
/// elements automatically update in response to property changes. It is typically used to describe and categorize
/// different types of projects within an application.</remarks>
public class ProjectType : INotifyPropertyChanged
{
    private int _id;
    private string _typeName;
    private string _description;
    private string _iconPath;
    private ProjectTemplateType _templateType;

    public int Id
    {
        get => _id;
        set
        {
            _id = value;
            OnPropertyChanged(nameof(Id));
        }
    }

    public string TypeName
    {
        get => _typeName;
        set
        {
            _typeName = value;
            OnPropertyChanged(nameof(TypeName));
        }
    }

    public string Description
    {
        get => _description;
        set
        {
            _description = value;
            OnPropertyChanged(nameof(Description));
        }
    }

    public string IconPath
    {
        get => _iconPath;
        set
        {
            _iconPath = value;
            OnPropertyChanged(nameof(IconPath));
        }
    }

    public ProjectTemplateType TemplateType
    {
        get => _templateType;
        set
        {
            _templateType = value;
            OnPropertyChanged(nameof(TemplateType));
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected virtual void OnPropertyChanged(string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }


    /// <summary>
    /// Specifies the available types of project templates supported by the application.
    /// </summary>
    /// <remarks>Use this enumeration to select the template type when creating or managing a project. The
    /// values correspond to different study or review methodologies.</remarks>
    public enum ProjectTemplateType
    {
        ClassicalReview,
        FlashCard
    }
}
