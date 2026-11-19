using ReciteHelper.Core.Aggregates;
using ReciteHelper.Wpf.Utilities;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;

namespace ReciteHelper.Wpf.Controls;

public interface IWorkspacePaneLayout
{
    void SetHorizontalLayout(bool horizontal);
}

[ContentProperty(nameof(MainContent))]
public sealed class WorkspacePaneManager : Grid
{
    internal const string Notes = "Notes";
    internal const string Sources = "Sources";
    internal const string Mistakes = "Mistakes";
    internal enum Placement { Left, Right, Bottom, Float, Top }
    private enum Flow { Auto, Vertical, Horizontal }
    private enum Grouping { Tabs, Stacked, SideBySide }

    public sealed class Layout
    {
        public int Place { get; set; }
        public bool Open { get; set; }
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool HasBounds { get; set; }
        public bool Selected { get; set; }
        public int Orientation { get; set; }
        public int Arrangement { get; set; }
        public double Share { get; set; } = 1;
    }

    private sealed record Pane(string Key, string Title, FrameworkElement Body, DockPanel Content);
    private const string DragFormat = "ReciteHelper.WorkspacePane";
    private readonly Dictionary<Project, Dictionary<string, Pane>> _projects = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, Window> _floating = [];
    private readonly Dictionary<Placement, TabControl> _groups = [];
    private readonly Dictionary<Placement, Grid> _regions = [];
    private readonly Dictionary<string, Layout> _layouts = [];
    private readonly Dictionary<Placement, GridSplitter> _splitters = [];
    private Project? _project;
    private Window? _owner;
    private string? _layoutFile;
    private FrameworkElement? _mainContent;
    private bool _draggingPane;
    private bool _rendering;
    private readonly Border _dropIndicator = new() { Visibility = Visibility.Collapsed, IsHitTestVisible = false };

    internal event EventHandler<string>? PaneClosed;

    public WorkspacePaneManager()
    {
        foreach (var width in new[] { 0, 0, -1, 0, 0 }) ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = width < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(width),
            MinWidth = width < 0 ? 300 : 0
        });
        for (var row = 0; row < 5; row++)
            RowDefinitions.Add(new RowDefinition { Height = row == 2 ? new GridLength(1, GridUnitType.Star) : new GridLength(0),
                MinHeight = row == 2 ? 190 : 0 });
        AddGroup(Placement.Left, 0, 2, 1, 2);
        AddGroup(Placement.Right, 4, 2, 3, 2);
        AddGroup(Placement.Top, 0, 0, 0, 1);
        AddGroup(Placement.Bottom, 0, 4, 0, 3);
        foreach (var position in new[] { Placement.Top, Placement.Bottom })
        {
            SetColumnSpan(_regions[position], 5);
            SetColumnSpan(_splitters[position], 5);
        }
        _dropIndicator.SetResourceReference(Border.BackgroundProperty, "ArcAccent");
        _dropIndicator.Opacity = 0.22;
        SetZIndex(_dropIndicator, 10);
        SetRowSpan(_dropIndicator, 5);
        Children.Add(_dropIndicator);
        AllowDrop = true;
        DragOver += TrackDrag;
        DragLeave += (_, _) => _dropIndicator.Visibility = Visibility.Collapsed;
        Drop += DropPane;
    }

    public FrameworkElement? MainContent
    {
        get => _mainContent;
        set
        {
            if (_mainContent is not null) Children.Remove(_mainContent);
            _mainContent = value;
            if (value is null) return;
            SetColumn(value, 2);
            SetRow(value, 2);
            Children.Add(value);
        }
    }

    internal void Initialize(Window owner, string? file = null)
    {
        _owner = owner;
        _layoutFile = file ?? Path.Combine(AppContext.BaseDirectory, "workspace-layout.json");
        try
        {
            if (File.Exists(_layoutFile))
                foreach (var (key, layout) in JsonSerializer.Deserialize<Dictionary<string, Layout>>(File.ReadAllText(_layoutFile)) ?? [])
                    _layouts[key] = layout;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
        SizeChanged += (_, _) => SizeGroups();
        owner.LocationChanged += (_, _) => PlaceBesideOwner();
        owner.SizeChanged += (_, _) => PlaceBesideOwner();
    }

    internal void SetProject(Project? project)
    {
        if (ReferenceEquals(_project, project)) return;
        CaptureBounds();
        _project = project;
        Render();
    }

    internal bool IsOpen(string key) => For(key).Open;

    internal void Register(Project project, string key, string title, FrameworkElement body)
    {
        if (!_projects.TryGetValue(project, out var panes)) _projects.Add(project, panes = []);
        var panel = new DockPanel();
        var pane = new Pane(key, title, body, panel);
        panel.Children.Add(Header(pane));
        panel.Children.Add(body);
        panel.SizeChanged += (_, _) => ApplyFlow(pane);
        panes.Add(key, pane);
        if (ReferenceEquals(project, _project) && IsOpen(key)) Render(key);
    }

    internal void Remove(Project project)
    {
        if (ReferenceEquals(project, _project)) SetProject(null);
        _projects.Remove(project);
    }

    internal void ShowPane(string key)
    {
        For(key).Open = true;
        Render(key);
        SaveLayout();
    }

    internal void TogglePane(string key)
    {
        if (!IsOpen(key)) ShowPane(key);
        else ClosePane(key);
    }

    internal void ResetLayout()
    {
        var visible = _layouts.Where(pair => pair.Value.Open).Select(pair => pair.Key).ToArray();
        _layouts.Clear();
        foreach (var key in visible) For(key).Open = true;
        Render();
        SaveLayout();
    }

    private Layout For(string key)
    {
        if (_layouts.TryGetValue(key, out var layout)) return layout;
        layout = new Layout
        {
            Place = (int)(key == Notes ? Placement.Float : Placement.Right),
            Width = key == Notes ? 320 : 360, Height = 265
        };
        _layouts.Add(key, layout);
        return layout;
    }

    private void AddGroup(Placement place, int col, int row, int splitterCol, int splitterRow)
    {
        var region = new Grid();
        var group = new TabControl { BorderThickness = new Thickness(0), Padding = new Thickness(0) };
        group.SetResourceReference(FrameworkElement.StyleProperty, "ToolDockGroupStyle");
        group.SetResourceReference(BackgroundProperty, "ArcSurface");
        group.SelectionChanged += (_, e) =>
        {
            if (_rendering || e.Source != group || group.SelectedItem is not TabItem selected) return;
            foreach (TabItem item in group.Items) For((string)item.Tag).Selected = ReferenceEquals(item, selected);
            SaveLayout();
        };
        var splitter = new GridSplitter { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
            ResizeDirection = IsHorizontalEdge(place) ? GridResizeDirection.Rows : GridResizeDirection.Columns,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext };
        splitter.SetResourceReference(BackgroundProperty, "ArcBorder");
        splitter.DragCompleted += (_, _) =>
        {
            foreach (TabItem item in group.Items)
            {
                var layout = For((string)item.Tag);
                if (IsHorizontalEdge(place)) layout.Height = region.ActualHeight;
                else layout.Width = region.ActualWidth;
            }
            SaveLayout();
        };
        SetColumn(region, col); SetRow(region, row);
        SetColumn(splitter, splitterCol); SetRow(splitter, splitterRow);
        Children.Add(region); Children.Add(splitter);
        _regions.Add(place, region);
        _groups.Add(place, group); _splitters.Add(place, splitter);
    }

    private DockPanel Header(Pane pane)
    {
        var header = new DockPanel { Height = 33, LastChildFill = true };
        header.SetResourceReference(BackgroundProperty, "ArcSurfaceMuted");
        var close = ToolButton("×", "关闭面板");
        close.Click += (_, _) => ClosePane(pane.Key);
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        var position = ToolButton("⋯", "面板布局");
        position.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = position };
            foreach (var (option, caption) in new[] { (Placement.Left, "停靠左侧"), (Placement.Right, "停靠右侧"),
                (Placement.Top, "停靠顶部"), (Placement.Bottom, "停靠底部"), (Placement.Float, "浮动窗口") })
            {
                var item = new MenuItem { Header = caption, IsChecked = For(pane.Key).Place == (int)option };
                item.Click += (_, _) => MovePane(pane.Key, option);
                menu.Items.Add(item);
            }
            if (pane.Key == Notes)
            {
                var beside = new MenuItem { Header = "贴靠主窗口外侧" };
                beside.Click += (_, _) => MovePane(pane.Key, Placement.Float, beside: true);
                menu.Items.Add(beside);
            }
            menu.Items.Add(new Separator());
            var layout = For(pane.Key);
            var orientation = new MenuItem { Header = "内容布局" };
            foreach (var (option, title) in new[] { (Flow.Auto, "随面板调整"), (Flow.Vertical, "纵向布局"), (Flow.Horizontal, "横向布局") })
            {
                var item = new MenuItem { Header = title, IsChecked = layout.Orientation == (int)option };
                item.Click += (_, _) => SetFlow(pane, option);
                orientation.Items.Add(item);
            }
            menu.Items.Add(orientation);
            if (layout.Place != (int)Placement.Float)
            {
                var arrangement = new MenuItem { Header = "同区域的面板" };
                foreach (var (option, title) in new[] { (Grouping.Tabs, "合并为标签页"), (Grouping.Stacked, "上下拆分"), (Grouping.SideBySide, "左右拆分") })
                {
                    var item = new MenuItem { Header = title, IsChecked = layout.Arrangement == (int)option };
                    item.Click += (_, _) => SetGrouping(pane.Key, option);
                    arrangement.Items.Add(item);
                }
                menu.Items.Add(arrangement);
            }
            menu.IsOpen = true;
        };
        DockPanel.SetDock(position, Dock.Right);
        header.Children.Add(position);
        var captionBar = new Border { Padding = new Thickness(12, 0, 0, 0), Cursor = Cursors.SizeAll,
            Child = new TextBlock { Text = pane.Title, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 } };
        captionBar.Background = Brushes.Transparent;
        Point? dragStart = null;
        captionBar.PreviewMouseLeftButtonDown += (_, e) => dragStart = e.GetPosition(captionBar);
        captionBar.PreviewMouseLeftButtonUp += (_, _) => dragStart = null;
        captionBar.MouseMove += (_, e) =>
        {
            if (_draggingPane || e.LeftButton != MouseButtonState.Pressed || dragStart is not { } start) return;
            var delta = e.GetPosition(captionBar) - start;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _draggingPane = true;
            try
            {
                DragDrop.DoDragDrop(captionBar, new DataObject(DragFormat, pane.Key), DragDropEffects.Move);
            }
            finally { dragStart = null; _draggingPane = false; _dropIndicator.Visibility = Visibility.Collapsed; }
        };
        header.Children.Add(captionBar);
        DockPanel.SetDock(header, Dock.Top);
        return header;
    }

    private void SetFlow(Pane pane, Flow flow)
    {
        For(pane.Key).Orientation = (int)flow;
        ApplyFlow(pane);
        SaveLayout();
    }

    private void ApplyFlow(Pane pane)
    {
        if (pane.Body is not IWorkspacePaneLayout view) return;
        var layout = For(pane.Key);
        var horizontal = (Flow)layout.Orientation switch
        {
            Flow.Horizontal => true,
            Flow.Vertical => false,
            _ => IsHorizontalEdge((Placement)layout.Place) ||
                 layout.Place == (int)Placement.Float && pane.Content.ActualWidth > pane.Content.ActualHeight * 1.25
        };
        view.SetHorizontalLayout(horizontal);
    }

    private void SetGrouping(string key, Grouping grouping)
    {
        var place = For(key).Place;
        foreach (var layout in _layouts.Values.Where(layout => layout.Place == place))
            layout.Arrangement = (int)grouping;
        Render(key);
        SaveLayout();
    }

    private Button ToolButton(string text, string label)
    {
        var button = new Button { Content = text, Width = 32, Height = 30, MinHeight = 0, Padding = new Thickness(0), ToolTip = label };
        button.SetResourceReference(FrameworkElement.StyleProperty, "IconButtonStyle");
        return button;
    }

    private void TrackDrag(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DragFormat)) return;
        var place = Edge(e.GetPosition(this));
        _dropIndicator.Visibility = place == Placement.Float ? Visibility.Collapsed : Visibility.Visible;
        SetColumn(_dropIndicator, 0);
        SetColumnSpan(_dropIndicator, 5);
        _dropIndicator.Width = IsHorizontalEdge(place) ? double.NaN : Math.Min(220, ActualWidth / 3);
        _dropIndicator.Height = IsHorizontalEdge(place) ? Math.Min(190, ActualHeight / 3) : double.NaN;
        _dropIndicator.HorizontalAlignment = place == Placement.Left ? HorizontalAlignment.Left
            : place == Placement.Right ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
        _dropIndicator.VerticalAlignment = place == Placement.Bottom ? VerticalAlignment.Bottom
            : place == Placement.Top ? VerticalAlignment.Top : VerticalAlignment.Stretch;
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private Placement Edge(Point point) => point.X < ActualWidth * 0.26 ? Placement.Left
        : point.X > ActualWidth * 0.74 ? Placement.Right
        : point.Y > ActualHeight * 0.68 ? Placement.Bottom
        : point.Y < ActualHeight * 0.24 ? Placement.Top : Placement.Float;

    private static bool IsHorizontalEdge(Placement place) => place is Placement.Top or Placement.Bottom;

    private void DropPane(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DragFormat) is not string key) return;
        _dropIndicator.Visibility = Visibility.Collapsed;
        MovePane(key, Edge(e.GetPosition(this)));
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void MovePane(string key, Placement place, bool beside = false)
    {
        CaptureBounds();
        var layout = For(key);
        layout.Place = (int)place;
        var neighbor = _layouts.FirstOrDefault(pair => pair.Key != key && pair.Value.Place == (int)place && pair.Value.Open).Value;
        if (neighbor is not null) layout.Arrangement = neighbor.Arrangement;
        if (beside) layout.HasBounds = false;
        else if (place == Placement.Float && !layout.HasBounds && _owner is { } owner)
        {
            layout.Left = owner.Left + 44; layout.Top = owner.Top + 70;
            layout.Width = Math.Max(340, layout.Width); layout.Height = Math.Max(400, layout.Height);
            layout.HasBounds = true;
        }
        Render(key);
        SaveLayout();
    }

    private void ClosePane(string key)
    {
        CaptureBounds();
        For(key).Open = false;
        Render();
        PaneClosed?.Invoke(this, key);
        SaveLayout();
    }

    private void Render(string? focus = null)
    {
        _rendering = true;
        try { RenderGroups(focus); }
        finally { _rendering = false; }
    }

    private void RenderGroups(string? focus)
    {
        foreach (var region in _regions.Values)
        {
            region.Children.Clear();
            region.RowDefinitions.Clear();
            region.ColumnDefinitions.Clear();
        }
        foreach (var group in _groups.Values)
        {
            foreach (TabItem item in group.Items) item.Content = null;
            group.Items.Clear();
        }
        foreach (var (key, window) in _floating.ToArray())
        {
            if (_project is not null && _projects.TryGetValue(_project, out var current) &&
                current.TryGetValue(key, out var pane) && ReferenceEquals(pane.Content, window.Content) &&
                For(key).Open && For(key).Place == (int)Placement.Float) continue;
            window.Content = null;
            _floating.Remove(key);
            window.Close();
        }
        if (_project is not null && _projects.TryGetValue(_project, out var panes))
        {
            foreach (var pane in panes.Values.Where(pane => For(pane.Key).Open))
            {
                ApplyFlow(pane);
                var place = (Placement)For(pane.Key).Place;
                if (_groups.TryGetValue(place, out var group))
                    group.Items.Add(new TabItem { Header = pane.Title, Tag = pane.Key });
                else
                    ShowFloating(pane, focus == pane.Key);
            }
            foreach (var (place, group) in _groups)
            {
                if (group.Items.Count == 0) continue;
                var items = group.Items.Cast<TabItem>().ToArray();
                var selection = items.FirstOrDefault(item => (string)item.Tag == focus)
                    ?? items.FirstOrDefault(item => For((string)item.Tag).Selected) ?? items[0];
                group.SelectedItem = selection;
                foreach (var item in items) For((string)item.Tag).Selected = ReferenceEquals(item, selection);
                var arrangement = (Grouping)For((string)items[0].Tag).Arrangement;
                var region = _regions[place];
                if (items.Length > 1 && arrangement != Grouping.Tabs)
                    Split(region, items.Select(item => panes[(string)item.Tag]).ToArray(), arrangement == Grouping.Stacked);
                else
                {
                    foreach (var item in items) item.Content = panes[(string)item.Tag].Content;
                    region.Children.Add(group);
                }
            }
        }
        SizeGroups();
    }

    private void Split(Grid region, Pane[] panes, bool stacked)
    {
        for (var i = 0; i < panes.Length; i++)
        {
            var pane = panes[i];
            var share = new GridLength(Math.Max(0.08, For(pane.Key).Share), GridUnitType.Star);
            if (stacked) region.RowDefinitions.Add(new RowDefinition { Height = share, MinHeight = 90 });
            else region.ColumnDefinitions.Add(new ColumnDefinition { Width = share, MinWidth = 100 });
            SetRow(pane.Content, stacked ? i * 2 : 0);
            SetColumn(pane.Content, stacked ? 0 : i * 2);
            region.Children.Add(pane.Content);
            if (i == panes.Length - 1) continue;
            if (stacked) region.RowDefinitions.Add(new RowDefinition { Height = new GridLength(5) });
            else region.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
            var splitter = new GridSplitter
            {
                HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
                ResizeDirection = stacked ? GridResizeDirection.Rows : GridResizeDirection.Columns,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext
            };
            splitter.SetResourceReference(BackgroundProperty, "ArcBorder");
            if (stacked) SetRow(splitter, i * 2 + 1);
            else SetColumn(splitter, i * 2 + 1);
            splitter.DragCompleted += (_, _) =>
            {
                var total = panes.Sum(item => stacked ? item.Content.ActualHeight : item.Content.ActualWidth);
                if (total <= 0) return;
                foreach (var item in panes)
                    For(item.Key).Share = (stacked ? item.Content.ActualHeight : item.Content.ActualWidth) / total;
                SaveLayout();
            };
            region.Children.Add(splitter);
        }
    }

    private void SizeGroups()
    {
        var sideCount = _groups.Count(pair => !IsHorizontalEdge(pair.Key) && pair.Value.Items.Count > 0);
        var rowCount = _groups.Count(pair => IsHorizontalEdge(pair.Key) && pair.Value.Items.Count > 0);
        foreach (var (place, group) in _groups)
        {
            var open = group.Items.Count > 0;
            _regions[place].Visibility = _splitters[place].Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            if (open && group.Parent == _regions[place])
            {
                group.ApplyTemplate();
                if (group.Template.FindName("ToolTabs", group) is FrameworkElement tabs)
                    tabs.Visibility = group.Items.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            }
            var layout = open ? For((string)((TabItem)group.Items[0]).Tag) : null;
            if (IsHorizontalEdge(place))
            {
                var top = place == Placement.Top;
                RowDefinitions[top ? 1 : 3].Height = new GridLength(open ? 5 : 0);
                RowDefinitions[top ? 0 : 4].Height = new GridLength(open
                    ? Math.Min(Math.Max(135, layout!.Height), Math.Max(135, (ActualHeight - 202) / rowCount)) : 0);
            }
            else
            {
                var left = place == Placement.Left;
                ColumnDefinitions[left ? 1 : 3].Width = new GridLength(open ? 5 : 0);
                ColumnDefinitions[left ? 0 : 4].Width = new GridLength(open
                    ? Math.Min(Math.Max(228, layout!.Width), Math.Max(228, (ActualWidth - 340) / sideCount)) : 0);
            }
        }
    }

    private void ShowFloating(Pane pane, bool activate)
    {
        if (_owner is null || !_owner.IsVisible) return;
        if (_floating.TryGetValue(pane.Key, out var existing))
        {
            if (activate) existing.Activate();
            if (!For(pane.Key).HasBounds) PlaceBesideOwner();
            return;
        }
        var layout = For(pane.Key);
        var window = new Window { Title = pane.Title, Owner = _owner, Content = pane.Content, ShowInTaskbar = false,
            Width = Math.Max(250, layout.Width), Height = Math.Max(260, layout.Height), MinWidth = 245, MinHeight = 230,
            ShowActivated = activate };
        window.SetResourceReference(FrameworkElement.StyleProperty, "AppWindowStyle");
        var area = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (layout.HasBounds)
        {
            window.Left = Math.Clamp(layout.Left, area.Left, Math.Max(area.Left, area.Right - Math.Min(160, window.Width)));
            window.Top = Math.Clamp(layout.Top, area.Top, Math.Max(area.Top, area.Bottom - 80));
        }
        _floating.Add(pane.Key, window);
        window.Closing += (_, e) =>
        {
            if (!_floating.TryGetValue(pane.Key, out var current) || !ReferenceEquals(current, window) || _owner is not { IsVisible: true }) return;
            e.Cancel = true;
            Dispatcher.BeginInvoke(() => ClosePane(pane.Key));
        };
        new WindowInteropHelper(window).EnsureHandle();
        if (!layout.HasBounds) MakeSpaceBesideOwner(window);
        window.Show();
        PlaceBesideOwner();
        window.LocationChanged += (_, _) =>
        {
            if (layout.HasBounds) RememberFloating(window, layout);
        };
        window.SizeChanged += (_, _) =>
        {
            if (layout.HasBounds) RememberFloating(window, layout);
        };
    }

    private void MakeSpaceBesideOwner(Window window)
    {
        if (_owner is not { WindowState: WindowState.Normal } owner || !NativeWindowBounds.TryGet(owner, out var bounds, out var work)) return;
        var width = Math.Ceiling(window.Width * VisualTreeHelper.GetDpi(owner).DpiScaleX);
        var left = work.Right - width - bounds.Width;
        if (left >= work.Left && bounds.Left > left)
            NativeWindowBounds.Place(owner, new Rect(left, bounds.Top, bounds.Width, bounds.Height), resize: false);
    }

    private void PlaceBesideOwner()
    {
        if (_owner is not { } owner || owner.WindowState == WindowState.Minimized ||
            !NativeWindowBounds.TryGet(owner, out var bounds, out var work)) return;
        foreach (var (key, window) in _floating)
        {
            if (For(key).HasBounds) continue;
            var width = Math.Min(work.Width, Math.Ceiling(For(key).Width * VisualTreeHelper.GetDpi(owner).DpiScaleX));
            var height = Math.Min(bounds.Height, work.Height);
            NativeWindowBounds.Place(window, new Rect(Math.Clamp(bounds.Right, work.Left, work.Right - width),
                Math.Clamp(bounds.Top, work.Top, work.Bottom - height), width, height), resize: true);
        }
    }

    private static void RememberFloating(Window window, Layout layout)
    {
        if (window.WindowState != WindowState.Normal) return;
        layout.Left = window.Left; layout.Top = window.Top;
        layout.Width = window.Width; layout.Height = window.Height;
    }

    private void CaptureBounds()
    {
        foreach (var (key, window) in _floating)
            if (For(key).HasBounds) RememberFloating(window, For(key));
    }

    internal void SaveLayout()
    {
        if (_layoutFile is null) return;
        CaptureBounds();
        try
        {
            var temporary = _layoutFile + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_layouts));
            File.Move(temporary, _layoutFile, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }
}
