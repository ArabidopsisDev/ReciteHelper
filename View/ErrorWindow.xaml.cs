using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Windows;

namespace ReciteHelper.View
{
    public partial class ErrorWindow : Window, INotifyPropertyChanged
    {
        public static bool mutex = true;
        private Exception _exception;

        public ErrorWindow(Exception exception)
        {
            InitializeComponent();
            _exception = exception;
            mutex = false;

            InitializeExceptionInfo();
            DataContext = this;
        }

        private void InitializeExceptionInfo()
        {
            ExceptionType = _exception.GetType().Name;

            ExceptionMessage = _exception.Message;

            var detailsBuilder = new StringBuilder();
            AppendExceptionDetails(detailsBuilder, _exception);
            ExceptionDetails = detailsBuilder.ToString();
        }

        private void AppendExceptionDetails(StringBuilder builder, Exception ex, int depth = 0)
        {
            // 添加缩进
            if (depth > 0)
            {
                builder.AppendLine();
                builder.AppendLine(new string('-', 50));
                builder.AppendLine($"内部异常 #{depth}:");
            }

            builder.AppendLine($"类型: {ex.GetType().FullName}");
            builder.AppendLine($"消息: {ex.Message}");
            builder.AppendLine($"堆栈跟踪:");
            builder.AppendLine(ex.StackTrace);

            if (ex.InnerException != null)
            {
                AppendExceptionDetails(builder, ex.InnerException, depth + 1);
            }
        }

        public string ExceptionType
        {
            get => field;
            set => SetField(ref field, value);
        }

        public string ExceptionMessage
        {
            get => field;
            set => SetField(ref field, value);
        }

        public string ExceptionDetails
        {
            get => field;
            set => SetField(ref field, value);
        }


        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(ExceptionDetails);

                CopyButton.Content = "已复制!";

                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2)
                };
                timer.Tick += (s, args) =>
                {
                    CopyButton.Content = "复制信息";
                    timer.Stop();
                };
                timer.Start();
            }
            catch
            {
                MessageBox.Show("复制失败，请手动选择文本进行复制", "复制错误",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }


        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            mutex = true;
        }
    }
}