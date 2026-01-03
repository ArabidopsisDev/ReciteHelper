using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace ReciteHelper.View
{
    public partial class ProgressWindow : Window, INotifyPropertyChanged
    {
        public ProgressWindow()
        {
            InitializeComponent();
            DataContext = this;
        }


        public int RoundCurrent
        {
            get => field;
            set => SetField(ref field, value);
        }

        public int RoundTotal
        {
            get => field;
            set => SetField(ref field, value);
        }

        public int ScanCurrent
        {
            get => field;
            set => SetField(ref field, value);
        }

        public int ScanTotal
        {
            get => field;
            set => SetField(ref field, value);
        }

        public int ClusterCurrent
        {
            get => field;
            set => SetField(ref field, value);
        }

        public int ClusterTotal
        {
            get => field;
            set => SetField(ref field, value);
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

    }
}