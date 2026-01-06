using Microsoft.Win32;
using ReciteHelper.Model;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace ReciteHelper.View
{
    public partial class CreateGalGameWindow : Window, INotifyPropertyChanged
    {
        private string _selectedFilePath;

        public CreateGalGameWindow()
        {
            InitializeComponent();
            DataContext = this;
        }


        public string SelectedFilePath
        {
            get => _selectedFilePath;
            set
            {
                if (_selectedFilePath != value)
                {
                    _selectedFilePath = value;
                    OnPropertyChanged();
                }
            }
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "复习项目文件 (*.rhproj)|*.rhproj|所有文件 (*.*)|*.*",
                Title = "选择复习项目文件",
                Multiselect = false,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                CheckFileExists = true,
                CheckPathExists = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                SelectedFilePath = openFileDialog.FileName;

                if (!Path.GetExtension(SelectedFilePath).Equals(".rhproj", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("请选择 .rhproj 格式的文件", "文件格式错误",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    SelectedFilePath = null;
                }
            }
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SelectedFilePath))
            {
                MessageBox.Show("请先选择复习项目文件", "未选择文件",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!File.Exists(SelectedFilePath))
            {
                MessageBox.Show("选择的文件不存在，请重新选择", "文件不存在",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var result = MessageBox.Show(
                $"确定要使用 {Path.GetFileName(SelectedFilePath)} 创建 GalGame 吗？\n\n" +
                "此过程可能需要一些时间，请耐心等待...",
                "确认创建",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                var path = SelectedFilePath;
                var project = JsonSerializer.Deserialize<Project>(path)!;

                var singleChapter = new StringBuilder();
                var chapterQuestions = new List<StringBuilder>();

                foreach (var chapter in project.Chapters!)
                {
                    foreach (var question in chapter.Questions!)
                    {
                        singleChapter.AppendLine($"问题：{question.Text} 答案：{question.CorrectAnswer}");
                    }

                    chapterQuestions.Add(singleChapter);
                }



                DialogResult = true;
                Close();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}