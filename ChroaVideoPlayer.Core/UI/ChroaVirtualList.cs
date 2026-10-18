using System.Collections;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace ChroaVideoPlayer.Core.UI;

public class ChroaVirtualList : UserControl
{
    public static readonly StyledProperty<object?> HeaderProperty =
        AvaloniaProperty.Register<ChroaVirtualList, object?>(nameof(Header));

    public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty =
        AvaloniaProperty.Register<ChroaVirtualList, IDataTemplate?>(nameof(ItemTemplate));

    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<ChroaVirtualList, IEnumerable?>(nameof(ItemsSource));

    private readonly TranslateTransform _headerOffset = new();

    private readonly ContentControl _header;

    private readonly Border _headerClip;

    private readonly ItemsControl _items;

    private readonly ScrollViewer _listScroll;

    public ChroaVirtualList()
    {
        _header = new ContentControl
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            RenderTransform = _headerOffset,
        };

        _headerClip = new Border
        {
            ClipToBounds = true,
            Child = _header,
        };

        _items = new ItemsControl
        {
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
        };

        _listScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _items,
        };

        _listScroll.ScrollChanged += OnListScrolled;

        _items.LayoutUpdated += (_, _) =>
        {
            SyncColumnWidths();
            UpdateFillWidth();
        };

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Children =
            {
                _headerClip,
                _listScroll,
            },
        };

        Grid.SetRow(_listScroll, 1);

        Content = layout;
    }

    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public IDataTemplate? ItemTemplate
    {
        get => GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == HeaderProperty) _header.Content = Header;
        else if (change.Property == ItemTemplateProperty) _items.ItemTemplate = ItemTemplate;
        else if (change.Property == ItemsSourceProperty) _items.ItemsSource = ItemsSource;
    }

    private void OnListScrolled(object? sender, ScrollChangedEventArgs e)
    {
        _headerOffset.X = -_listScroll.Offset.X;

        UpdateFillWidth();
    }

    private void UpdateFillWidth()
    {
        var viewport = _listScroll.Viewport.Width;

        if (!double.IsFinite(viewport) || viewport <= 0) return;

        if (_items.MinWidth != viewport) _items.MinWidth = viewport;

        if (_header.Content is Control header && header.MinWidth != viewport) header.MinWidth = viewport;
    }

    private void SyncColumnWidths()
    {
        if (_header.Content is not Grid header) return;

        var columns = header.ColumnDefinitions.Count;

        if (columns == 0) return;

        foreach (var presenter in _items.GetVisualDescendants().OfType<ContentPresenter>())
        {
            if (presenter.Child is not Grid row) continue;
            if (row.ColumnDefinitions.Count != columns) continue;

            for (var index = 0; index < columns; index++)
            {
                var width = header.ColumnDefinitions[index].Width;

                if (row.ColumnDefinitions[index].Width == width) continue;

                row.ColumnDefinitions[index].Width = width;
            }
        }
    }
}
