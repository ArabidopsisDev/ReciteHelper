using System.Windows;

namespace ReciteHelper.Wpf.Views;

public partial class ClearReviewRecordsWindow : Window
{
    internal ClearReviewRecordsWindow(int count)
    {
        InitializeComponent();
        ScopeText.Text = $"本次操作涉及当前标签页的 {count} 道题。";
    }

    internal bool AnswersOnly => AnswersOnlyOption.IsChecked is true;

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
