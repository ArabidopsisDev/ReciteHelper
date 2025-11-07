using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;

namespace ReciteHelper
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            LoadRecentProjects();
        }

        private void LoadRecentProjects()
        {
            // 这里加载最近的项目列表
            // 可以从设置文件或数据库中读取
            // RecentProjectsPanel.Children.Clear();
            // foreach (var project in recentProjects)
            // {
            //     AddRecentProjectItem(project);
            // }
        }

        private void AddRecentProjectItem(string projectName, string projectPath)
        {
            var button = new Button
            {
                Style = (Style)FindResource("RecentItemStyle")
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // 项目图标
            var image = new Image
            {
                Source = new System.Windows.Media.Imaging.BitmapImage(
                    new System.Uri("pack://application:,,,/ReciteHelper;component/Images/project.png")),
                Width = 24,
                Height = 24,
                Margin = new Thickness(0, 0, 12, 0)
            };
            Grid.SetColumn(image, 0);

            // 项目信息
            var stackPanel = new StackPanel();
            Grid.SetColumn(stackPanel, 1);

            var nameTextBlock = new TextBlock
            {
                Text = projectName,
                FontWeight = FontWeights.SemiBold,
                Foreground = System.Windows.Media.Brushes.Black,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            var pathTextBlock = new TextBlock
            {
                Text = projectPath,
                FontSize = 11,
                Foreground = System.Windows.Media.Brushes.Gray,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            stackPanel.Children.Add(nameTextBlock);
            stackPanel.Children.Add(pathTextBlock);

            grid.Children.Add(image);
            grid.Children.Add(stackPanel);

            button.Content = grid;
            button.Click += (s, e) => OpenProject(projectPath);

            RecentProjectsPanel.Children.Add(button);
        }

        private void CreateNewProject_Click(object sender, RoutedEventArgs e)
        {
            // 创建新项目的逻辑
            MessageBox.Show("创建新项目功能");
        }

        private void LoadProject_Click(object sender, RoutedEventArgs e)
        {
            // 加载项目的逻辑
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "ReciteHelper项目文件 (*.rhproj)|*.rhproj"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                OpenProject(openFileDialog.FileName);
            }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            // 打开文件夹的逻辑
            var dialog = new OpenFolderDialog();
            if (dialog.ShowDialog() == true)
            {
                // 处理文件夹打开逻辑
                MessageBox.Show($"打开文件夹: {dialog.FolderName}");
            }
        }

        private void OpenProject(string projectPath)
        {
            // 打开项目的逻辑
            MessageBox.Show($"打开项目: {projectPath}");
        }
    }
}