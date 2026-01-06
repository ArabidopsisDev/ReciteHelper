using ReciteHelper.Model;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using static ReciteHelper.Model.ProjectType;

namespace ReciteHelper.View
{
    public partial class ProjectTypeSelectionWindow : Window, INotifyPropertyChanged
    {
        private ObservableCollection<ProjectType> _projectTypes;
        private ObservableCollection<ProjectType> _filteredProjectTypes;
        private ProjectType _selectedProjectType;

        public ProjectTypeSelectionWindow()
        {
            InitializeComponent();
            DataContext = this;

            InitializeProjectTypes();
            UpdateDisplay();
        }

        private void InitializeProjectTypes()
        {
            // Initialize the list of project types
            _projectTypes = new ObservableCollection<ProjectType>
            {
                new ProjectType
                {
                    Id = 1,
                    TypeName = "经典复习项目",
                    Description = "适用于偏文科类的经典ReciteHelper复习项目，可以将复习资料整理为知识点，以及生成常规的填空题和解答题题库用于复习。",
                    IconPath = "pack://application:,,,/ReciteHelper;component/Images/type_classical.png",
                    TemplateType = ProjectTemplateType.ClassicalReview
                },
                new ProjectType
                {
                    Id = 2,
                    TypeName = "记忆卡片",
                    Description = "适用于需要快速记忆和检测的场景，以及无明确主题的零散知识点的记忆。将根据复习资料生成知识点卡片进行记忆。",
                    IconPath = "pack://application:,,,/ReciteHelper;component/Images/type_card.png",
                    TemplateType = ProjectTemplateType.FlashCard
                },
                new ProjectType
                {
                    Id = 3,
                    TypeName = "题库文件创建",
                    Description = "适用于资料被分为多个文件的，以及文件类型繁杂不易被处理的情况，该项目将把所有的资料文件合并为一个文件供创建其它项目使用。",
                    IconPath = "pack://application:,,,/ReciteHelper;component/Images/type_pdf.png",
                    TemplateType = ProjectTemplateType.PDFMerge
                },
                new ProjectType
                {
                    Id = 4,
                    TypeName = "旮旯给木",
                    Description = "适用于复习科目偏向于文科，且难以背诵的情况。ciallo (∠·ω )⌒★ 来跟你的知识点们谈一场美妙的恋爱吧~",
                    IconPath = "pack://application:,,,/ReciteHelper;component/Images/type_girl.png",
                    TemplateType = ProjectTemplateType.GalGame
                }
            };

            _filteredProjectTypes = new ObservableCollection<ProjectType>(_projectTypes);
            ProjectTypesItemsControl.ItemsSource = _filteredProjectTypes;
        }

        private void UpdateDisplay()
        {
            NoResultsPanel.Visibility = _filteredProjectTypes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ConfirmButton.IsEnabled = _selectedProjectType != null;
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterProjectTypes();
        }

        private void FilterProjectTypes()
        {
            string searchText = SearchTextBox.Text?.Trim().ToLower();

            if (string.IsNullOrEmpty(searchText))
            {
                // If no search term is found, show all items
                _filteredProjectTypes.Clear();
                foreach (var type in _projectTypes)
                {
                    _filteredProjectTypes.Add(type);
                }
            }
            else
            {
                // Filter by search terms
                var filtered = _projectTypes.Where(p =>
                    (p.TypeName?.ToLower().Contains(searchText) ?? false) ||
                    (p.Description?.ToLower().Contains(searchText) ?? false)
                ).ToList();

                _filteredProjectTypes.Clear();
                foreach (var type in filtered)
                {
                    _filteredProjectTypes.Add(type);
                }
            }

            // If the currently selected item is not in the filter results, deselect
            if (_selectedProjectType != null && !_filteredProjectTypes.Contains(_selectedProjectType))
            {
                _selectedProjectType = null;
            }

            UpdateDisplay();
        }

        private void ProjectTypeButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ProjectType projectType)
            {
                SelectProjectType(projectType);
            }
        }

        private void SelectProjectType(ProjectType projectType)
        {
            _selectedProjectType = projectType;

            // Update the styles of all project items
            foreach (var item in _filteredProjectTypes)
            {
                var container = ProjectTypesItemsControl.ItemContainerGenerator.ContainerFromItem(item);
                if (container is ContentPresenter contentPresenter)
                {
                    var border = FindVisualChild<Border>(contentPresenter, "ProjectTypeBorder");
                    if (border != null)
                    {
                        if (item == _selectedProjectType)
                        {
                            border.Style = (Style)FindResource("SelectedProjectTypeItemStyle");
                        }
                        else
                        {
                            border.Style = (Style)FindResource("ProjectTypeItemStyle");
                        }
                    }
                }
            }

            UpdateDisplay();
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProjectType != null)
            {
                SelectedProjectType = _selectedProjectType;
                DialogResult = true;
                Close();
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private T FindVisualChild<T>(DependencyObject parent, string childName = null) where T : DependencyObject
        {
            if (parent == null) return null;

            int childrenCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);

                if (child is T result)
                {
                    if (childName == null || (child is FrameworkElement fe && fe.Name == childName))
                    {
                        return result;
                    }
                }

                var descendant = FindVisualChild<T>(child, childName);
                if (descendant != null) return descendant;
            }

            return null;
        }

        public ProjectType SelectedProjectType { get; private set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}