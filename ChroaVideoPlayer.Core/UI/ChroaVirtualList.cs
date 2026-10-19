using System.Collections;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
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

    private readonly ContentControl _header;

    private readonly ItemsControl _items;

    private readonly ScrollViewer _listScroll;

    private readonly Grid _body;

    private readonly ScrollViewer _scroll;

    public ChroaVirtualList()
    {
        _header = new ContentControl();

        _items = new ItemsControl
        {
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
        };

        _listScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _items,
        };

        _body = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Children =
            {
                _header,
                _listScroll,
            },
        };

        Grid.SetRow(_listScroll, 1);

        _scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _body,
        };

        _scroll.ScrollChanged += (_, _) => UpdateFillWidth();
        _scroll.SizeChanged += (_, _) => UpdateFillWidth();

        _items.LayoutUpdated += (_, _) =>
        {
            SyncColumnWidths();
            UpdateFillWidth();
        };

        LayoutUpdated += (_, _) => UpdateFillWidth();

        Content = _scroll;
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

    private void UpdateFillWidth()
    {
        var viewport = _scroll.Viewport.Width;

        if (!double.IsFinite(viewport) || viewport <= 0) return;

        if (_body.MinWidth != viewport) _body.MinWidth = viewport;
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
