using Microsoft.Win32;
using ReciteHelper.Models;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace ReciteHelper
{
    public partial class MainWindow : Window
    {
        private List<RecentProject> recentProjects = new List<RecentProject>();
        private string recentProjectsFile = "recent_projects.json";

        public MainWindow()
        {
            InitializeComponent();
            LoadRecentProjects();
            LoadSlogan();
            PopulateRecentProjectsUI();
        }

        private void LoadSlogan()
        {
            List<string> slogan = ["自力更生 艰苦奋斗", "为人民服务", "高质量发展是首要任务", 
                "高水平科技自立自强", "人民城市人民建 人民城市为人民", "坚持融入日常、抓在经常",
                "扎实推进乡村振兴战略", "保障粮食和重要农产品安全", "守护好中华民族的文化瑰宝",
                "反对大吃大喝 注意节约"];
            SloganLabel.Content = slogan[Random.Shared.Next(0, slogan.Count())];
        }

        private void LoadRecentProjects()
        {
            try
            {
                if (File.Exists(recentProjectsFile))
                {
                    string json = File.ReadAllText(recentProjectsFile);
                    recentProjects = JsonSerializer.Deserialize<List<RecentProject>>(json) ?? new List<RecentProject>();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载最近项目失败: {ex.Message}");
                recentProjects = new List<RecentProject>();
            }
        }


        private void SaveRecentProjects()
        {
            try
            {
                string json = JsonSerializer.Serialize(recentProjects, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(recentProjectsFile, json);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存最近项目失败: {ex.Message}");
            }
        }

        private void PopulateRecentProjectsUI()
        {
            RecentProjectsPanel.Children.Clear();

            // Sort by visit time
            recentProjects.Sort((x, y) => y.LastAccessed.CompareTo(x.LastAccessed));

            foreach (var project in recentProjects)
            {
                AddRecentProjectToUI(project.ProjectName, project.ProjectPath);
            }
        }

        private void AddRecentProjectToUI(string projectName, string projectPath)
        {
            var button = new Button
            {
                Style = (Style)FindResource("RecentItemStyle"),
                Tag = projectPath
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var image = new Image
            {
                Source = new System.Windows.Media.Imaging.BitmapImage(
                    new Uri("pack://application:,,,/ReciteHelper;component/Images/project.png")),
                Width = 24,
                Height = 24,
                Margin = new Thickness(0, 0, 12, 0)
            };
            Grid.SetColumn(image, 0);

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
            button.Click += RecentProject_Click;

            RecentProjectsPanel.Children.Add(button);
        }

        public void AddRecentProject(string projectPath)
        {
            string projectName = Path.GetFileName(projectPath);

            // Check if exists
            var existingProject = recentProjects.Find(p => p.ProjectPath.Equals(projectPath, StringComparison.OrdinalIgnoreCase));
            if (existingProject != null)
            {
                // Update visit time
                existingProject.LastAccessed = DateTime.Now;
            }
            else
            {
                // Add new project
                recentProjects.Add(new RecentProject
                {
                    ProjectName = projectName,
                    ProjectPath = projectPath,
                    LastAccessed = DateTime.Now
                });

                // Master~ I want to be ♡~ filled up!
                if (recentProjects.Count > 10)
                {
                    recentProjects.Sort((x, y) => y.LastAccessed.CompareTo(x.LastAccessed));
                    recentProjects = recentProjects.GetRange(0, 10);
                }
            }

            // Update UI
            SaveRecentProjects();
            PopulateRecentProjectsUI();
        }

        private void RecentProject_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string projectPath)
            {
                OpenProject(projectPath);
            }
        }

        private void CreateNewProject_Click(object sender, RoutedEventArgs e)
        {
            var result = new CreateProjectWindow()
            {
                Owner = this
            }.ShowDialog();

            if (result == false)
            {
                MessageBox.Show("已放弃创建项目");
            }
        }

        private void LoadProject_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "ReciteHelper项目文件 (*.rhproj)|*.rhproj",
                Multiselect = false
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string projectPath = openFileDialog.FileName;
                OpenProject(projectPath);

                // Add to recent projects
                AddRecentProject(projectPath);
            }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog();

            if (dialog.ShowDialog() == true)
            {
                string folderPath = dialog.FolderName;

                // Find the project file in the folder
                var projectFiles = Directory.GetFiles(folderPath, "*.rhproj");
                if (projectFiles.Length > 0)
                {
                    // If you find the project file, open the first one.
                    OpenProject(projectFiles[0]);
                    AddRecentProject(projectFiles[0]);
                }
                else
                {
                    MessageBox.Show("在选择的文件夹中未找到项目文件 (*.rhproj)", "提示",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }

            }
        }

        private void OpenProject(string projectPath)
        {
            if (File.Exists(projectPath))
            {
                try
                {
                    // TODO: This section implements the logic for actually opening the project
                    // e.g. Loading project data, switching to the main interface, etc

                    var jsonString = File.ReadAllText(projectPath);
                    var project = JsonSerializer.Deserialize<Project>(jsonString!);
                    if (project != null)
                    {
                        project.LastAccessed = DateTime.Now;
                        var quizWindow = new SelectChapterWindow(project);
                        quizWindow.Show();

                        SaveRecentProjects();
                        PopulateRecentProjectsUI();
                    }

                }
                catch (Exception ex)
                {
                    MessageBox.Show($"打开项目失败: {ex.Message}", "错误",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show($"项目文件不存在: {projectPath}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}