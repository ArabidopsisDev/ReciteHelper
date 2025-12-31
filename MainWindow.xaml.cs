using Microsoft.Win32;
using ReciteHelper.Model;
using ReciteHelper.Utils;
using ReciteHelper.View;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using static ReciteHelper.Model.ProjectType;

namespace ReciteHelper
{
    public partial class MainWindow : Window
    {
        private List<RecentProject> recentProjects = new List<RecentProject>();
        private string recentProjectsFile = "recent_projects.json";

        public MainWindow()
        {
            Utils.Deformity.HorribleMethod();

            InitializeComponent();
            LoadRecentProjects();
            LoadSlogan();
            PopulateRecentProjectsUI();
        }

        private void LoadSlogan()
        {
            List<string> slogan = ["你一定会坚持到底的", "常回家看看", "我有卡SPFA症",
                "向上的路没有同伴", "咕咕，咕咕，咕咕咕！", "坚持融入日常、抓在经常",
                "我真的是一个很坏的雪莉吗", "你好多宝宝，你开幼儿园算了", "对的对的对的，哦不对！",
                "Vive la France"];
            SloganLabel.Content = slogan[Random.Shared.Next(0, slogan.Count())];

            if (DateTime.Now.Hour > 14 && Random.Shared.Next(1, 10) > 8)
                SloganLabel.Content = "哇塞，睡得跟猪头一样";
            if (DateTime.Now.Hour > 14 && Random.Shared.Next(1, 10) > 8)
                SloganLabel.Content = "每天睡得屁股都挪不动了吧";
            if (DateTime.Now.Hour > 10 && Random.Shared.Next(1, 10) > 8)
                SloganLabel.Content = "又不学习，你去spa";
            if (DateTime.Now.Hour > 10 && Random.Shared.Next(1, 10) > 8)
                SloganLabel.Content = "别躺在床上刷手机啃苹果了";
            if (Random.Shared.Next(1, 10) > 8)
                SloganLabel.Content = "我读到生词怎么办，跳过";
            if (Random.Shared.Next(1, 10) > 8)
                SloganLabel.Content = "每天都在屋子里面滑狗";
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
                var json = JsonSerializer.Serialize(recentProjects, new JsonSerializerOptions { WriteIndented = true });
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
                AddRecentProjectToUI(project.ProjectName, project.ProjectPath);
        }

        private void AddRecentProjectToUI(string? projectName, string? projectPath)
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
            var select = new ProjectTypeSelectionWindow();
            var dialogResult = select.ShowDialog();
            var type = new ProjectType();
            bool? result = false;

            if (dialogResult == true)
                type = select.SelectedProjectType;

            if (type.TemplateType == ProjectTemplateType.ClassicalReview)
            {
                result = new CreateProjectWindow(CatchProject)
                {
                    Owner = this
                }.ShowDialog();
            }

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
            var dialog = new OpenFileDialog();

            if (dialog.ShowDialog() == true)
            {
                var exportFile = dialog.FileName;
                try
                {
                    var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                    var outputFolder = Path.Combine(baseDirectory, "temp");

                    Directory.CreateDirectory(outputFolder);
                    Directory.Clear(outputFolder);
                    ZipFile.ExtractToDirectory(exportFile, outputFolder);

                    // Why use a manifest file?
                    // Because the manifest file will later need to include information
                    // such as the author's name and a brief description of the document
                    var manifestText = File.ReadAllText(Path.Combine(outputFolder, "manifest.json"));
                    var manifest = JsonSerializer.Deserialize<Manifest?>(manifestText);

                    if (manifest is null || manifest.ProjectFile is null)
                        throw new ArgumentException("Incomplete manifest file");
                    var projectFile = manifest.ProjectFile;

                    // Release file
                    var exactFolder = Path.Combine(baseDirectory, "imports", projectFile.Split('.')[0]);
                    Directory.CreateDirectory(exactFolder);
                    File.Copy($@"{outputFolder}\{projectFile}", $@"{exactFolder}\{projectFile.Replace("_exp", "")}", true);

                    // Add to list
                    CatchProject($@"{exactFolder}\{projectFile.Replace("_exp", "")}", manifest.ProjectFile.Replace("_exp", ""));

                    // In theory, users should have space for choice.
                    MessageBox.Show("项目导入成功", "导入成功", MessageBoxButton.OK, MessageBoxImage.Information); ;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"文件类型不正确或已损坏。\n详细信息：{ex.Message}",
                        "导入失败", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void CatchProject(string path, string name)
        {
            // Display to user
            recentProjects.Add(new() { ProjectPath=path, LastAccessed=DateTime.Now, ProjectName=name });
            SaveRecentProjects();
            PopulateRecentProjectsUI();
        }

        private void OpenProject(string projectPath)
        {
            if (File.Exists(projectPath))
            {
                try
                {
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

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            var originalCount = recentProjects.Count;

            recentProjects = recentProjects
                .Where(project => File.Exists(project.ProjectPath))
                .ToList();

            if (recentProjects.Count != originalCount)
            {
                var json = JsonSerializer.Serialize(recentProjects, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(recentProjectsFile, json);
            }
        }
    }
}