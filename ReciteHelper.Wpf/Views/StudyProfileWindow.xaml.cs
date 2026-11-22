using Microsoft.Win32;
using ReciteHelper.Infrastructure.Services;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ReciteHelper.Wpf.Views;

public partial class StudyProfileWindow : Window
{
    private readonly LocalStudyAccount _account;
    private byte[]? _avatar;
    internal StudyProfile Profile { get; private set; }

    internal StudyProfileWindow(LocalStudyAccount account, StudyProfile profile)
    {
        _account = account;
        Profile = profile;
        InitializeComponent();
        Username.Text = profile.Name;
        SetAvatar(profile.Avatar);
    }

    private void ChooseAvatar_Click(object sender, RoutedEventArgs e)
    {
        var file = new OpenFileDialog { Title = "选择头像", Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif" };
        if (file.ShowDialog(this) is not true) return;
        try
        {
            SetAvatar(ReadAvatar(file.FileName));
            EditStatus.Text = string.Empty;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or UnauthorizedAccessException)
        {
            EditStatus.Text = "无法读取这张图片。请选择另一张。";
        }
    }

    private void ResetAvatar_Click(object sender, RoutedEventArgs e)
    {
        SetAvatar(null);
        EditStatus.Text = string.Empty;
    }

    private static byte[] ReadAvatar(string filePath)
    {
        using var source = File.OpenRead(filePath);
        var image = LoadBitmap(source, 512);
        var size = Math.Min(image.PixelWidth, image.PixelHeight);
        var square = new CroppedBitmap(image, new Int32Rect((image.PixelWidth - size) / 2,
            (image.PixelHeight - size) / 2, size, size));
        var scale = Math.Min(1, 256d / size);
        var avatar = new TransformedBitmap(square, new ScaleTransform(scale, scale));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(avatar));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    private void SetAvatar(byte[]? avatar)
    {
        _avatar = avatar;
        AvatarPreview.Fill = AvatarBrush(avatar);
        ResetAvatar.IsEnabled = avatar is not null;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = Username.Text.Trim();
        if (name.Length == 0)
        {
            EditStatus.Text = "请输入用户名。";
            Username.Focus();
            return;
        }
        IsEnabled = false;
        try
        {
            Profile = (await _account.SaveProfileAsync(name, _avatar)).Profile;
            DialogResult = true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            EditStatus.Text = $"无法保存资料。{ex.Message}";
        }
        finally { IsEnabled = true; }
    }

    internal static ImageBrush? AvatarBrush(byte[]? avatar)
    {
        if (avatar is null) return null;
        using var stream = new MemoryStream(avatar);
        return new ImageBrush(LoadBitmap(stream)) { Stretch = Stretch.UniformToFill };
    }

    private static BitmapImage LoadBitmap(Stream stream, int width = 0)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.StreamSource = stream;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = width;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
