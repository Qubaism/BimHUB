using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using QBTools.BimHUB.Compatibility;
using System.Collections.Specialized;

namespace QBTools.BimHUB.Controls;

public sealed class AdaptiveGridPanel : VirtualizingPanel, IScrollInfo
{
    public static readonly DependencyProperty MinimumItemWidthProperty = DependencyProperty.Register(
        nameof(MinimumItemWidth),
        typeof(double),
        typeof(AdaptiveGridPanel),
        new FrameworkPropertyMetadata(112d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ItemHeightProperty = DependencyProperty.Register(
        nameof(ItemHeight),
        typeof(double),
        typeof(AdaptiveGridPanel),
        new FrameworkPropertyMetadata(194d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing),
        typeof(double),
        typeof(AdaptiveGridPanel),
        new FrameworkPropertyMetadata(8d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    private Size _extent;
    private Size _viewport;
    private Point _offset;
    private int _columns = 1;
    private double _itemWidth = 112;

    public double MinimumItemWidth
    {
        get => (double)GetValue(MinimumItemWidthProperty);
        set => SetValue(MinimumItemWidthProperty, value);
    }

    public double ItemHeight
    {
        get => (double)GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        ItemsControl? owner = ItemsControl.GetItemsOwner(this);
        int itemCount = owner?.Items.Count ?? 0;
        double width = ResolveWidth(availableSize.Width);
        (_columns, _itemWidth) = AdaptiveGridLayout.CalculateColumns(
            width,
            MinimumItemWidth,
            Spacing);

        double viewportHeight = ResolveViewportHeight(availableSize.Height);
        int rows = itemCount == 0 ? 0 : (int)Math.Ceiling(itemCount / (double)_columns);
        double rowHeight = ItemHeight + Spacing;
        double extentHeight = rows == 0 ? 0 : rows * ItemHeight + (rows - 1) * Spacing;
        UpdateScrollInfo(new Size(width, extentHeight), new Size(width, viewportHeight));

        if (itemCount == 0)
        {
            ClearRealizedItems();
            return new Size(width, viewportHeight);
        }

        int firstRow = Math.Max(0, (int)Math.Floor(VerticalOffset / rowHeight));
        int visibleRows = Math.Max(1, (int)Math.Ceiling(viewportHeight / rowHeight) + 1);
        int firstItemIndex = Math.Min(itemCount - 1, firstRow * _columns);
        int lastItemIndex = Math.Min(itemCount - 1, (firstRow + visibleRows) * _columns - 1);

        CleanupItems(firstItemIndex, lastItemIndex);
        GenerateItems(firstItemIndex, lastItemIndex);

        return new Size(width, viewportHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        ItemsControl? owner = ItemsControl.GetItemsOwner(this);
        if (owner is null) return finalSize;

        double rowHeight = ItemHeight + Spacing;
        foreach (UIElement child in InternalChildren)
        {
            int itemIndex = owner.ItemContainerGenerator.IndexFromContainer(child);
            if (itemIndex < 0) continue;

            int column = itemIndex % _columns;
            int row = itemIndex / _columns;
            double x = column * (_itemWidth + Spacing);
            double y = row * rowHeight - VerticalOffset;
            child.Arrange(new Rect(x, y, _itemWidth, ItemHeight));
        }

        return finalSize;
    }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        base.OnItemsChanged(sender, args);
        if (args.Action == NotifyCollectionChangedAction.Reset
            || args.Action == NotifyCollectionChangedAction.Replace
            || args.Action == NotifyCollectionChangedAction.Move)
        {
            _offset = new Point(0, 0);
            ScrollOwner?.InvalidateScrollInfo();
            ClearRealizedItems();
        }
        InvalidateMeasure();
    }

    private void CleanupItems(int firstItemIndex, int lastItemIndex)
    {
        ItemsControl? owner = ItemsControl.GetItemsOwner(this);
        if (owner is null) return;

        for (int childIndex = InternalChildren.Count - 1; childIndex >= 0; childIndex--)
        {
            UIElement child = InternalChildren[childIndex];
            int itemIndex = owner.ItemContainerGenerator.IndexFromContainer(child);
            if (itemIndex >= firstItemIndex && itemIndex <= lastItemIndex) continue;

            ItemContainerGenerator.Remove(new GeneratorPosition(childIndex, 0), 1);
            RemoveInternalChildRange(childIndex, 1);
        }
    }

    private void GenerateItems(int firstItemIndex, int lastItemIndex)
    {
        IItemContainerGenerator generator = ItemContainerGenerator;
        GeneratorPosition startPosition = generator.GeneratorPositionFromIndex(firstItemIndex);
        int childIndex = startPosition.Offset == 0 ? startPosition.Index : startPosition.Index + 1;

        using IDisposable generation = generator.StartAt(
            startPosition,
            GeneratorDirection.Forward,
            true);
        for (int itemIndex = firstItemIndex; itemIndex <= lastItemIndex; itemIndex++, childIndex++)
        {
            UIElement child = (UIElement)generator.GenerateNext(out bool newlyRealized);
            if (newlyRealized)
            {
                if (childIndex >= InternalChildren.Count) AddInternalChild(child);
                else InsertInternalChild(childIndex, child);
                generator.PrepareItemContainer(child);
            }

            child.Measure(new Size(_itemWidth, ItemHeight));
        }
    }

    private void ClearRealizedItems()
    {
        if (InternalChildren.Count == 0)
        {
            return;
        }

        ItemContainerGenerator.RemoveAll();
        RemoveInternalChildRange(0, InternalChildren.Count);
    }

    private void UpdateScrollInfo(Size extent, Size viewport)
    {
        bool changed = extent != _extent || viewport != _viewport;
        _extent = extent;
        _viewport = viewport;
        double maximumOffset = Math.Max(0, ExtentHeight - ViewportHeight);
        if (_offset.Y > maximumOffset)
        {
            _offset.Y = maximumOffset;
            changed = true;
        }

        if (changed) ScrollOwner?.InvalidateScrollInfo();
    }

    private double ResolveWidth(double proposedWidth)
    {
        if (!double.IsInfinity(proposedWidth) && !double.IsNaN(proposedWidth) && proposedWidth > 0)
        {
            return proposedWidth;
        }

        return ActualWidth > 0 ? ActualWidth : MinimumItemWidth;
    }

    private double ResolveViewportHeight(double proposedHeight)
    {
        if (!double.IsInfinity(proposedHeight) && !double.IsNaN(proposedHeight) && proposedHeight > 0)
        {
            return proposedHeight;
        }

        if (ScrollOwner?.ViewportHeight > 0) return ScrollOwner.ViewportHeight;
        return ActualHeight > 0 ? ActualHeight : ItemHeight * 3;
    }

    public bool CanHorizontallyScroll { get; set; }
    public bool CanVerticallyScroll { get; set; }
    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => _offset.X;
    public double VerticalOffset => _offset.Y;
    public ScrollViewer? ScrollOwner { get; set; }

    public void LineUp() => SetVerticalOffset(VerticalOffset - Math.Max(16, ItemHeight / 8));
    public void LineDown() => SetVerticalOffset(VerticalOffset + Math.Max(16, ItemHeight / 8));
    public void LineLeft() { }
    public void LineRight() { }
    public void MouseWheelUp() => SetVerticalOffset(VerticalOffset - Math.Max(48, ItemHeight / 2));
    public void MouseWheelDown() => SetVerticalOffset(VerticalOffset + Math.Max(48, ItemHeight / 2));
    public void MouseWheelLeft() { }
    public void MouseWheelRight() { }
    public void PageUp() => SetVerticalOffset(VerticalOffset - ViewportHeight);
    public void PageDown() => SetVerticalOffset(VerticalOffset + ViewportHeight);
    public void PageLeft() { }
    public void PageRight() { }
    public void SetHorizontalOffset(double offset) { }

    public void SetVerticalOffset(double offset)
    {
        double maximumOffset = Math.Max(0, ExtentHeight - ViewportHeight);
        double newOffset = RuntimeCompatibility.Clamp(offset, 0, maximumOffset);
        if (Math.Abs(newOffset - _offset.Y) < 0.1) return;

        _offset.Y = newOffset;
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateMeasure();
    }

    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        ItemsControl? owner = ItemsControl.GetItemsOwner(this);
        DependencyObject? container = owner is null
            ? null
            : ItemsControl.ContainerFromElement(owner, visual);
        int itemIndex = container is null || owner is null
            ? -1
            : owner.ItemContainerGenerator.IndexFromContainer(container);
        if (itemIndex < 0) return rectangle;

        double top = itemIndex / _columns * (ItemHeight + Spacing);
        double bottom = top + ItemHeight;
        if (top < VerticalOffset) SetVerticalOffset(top);
        else if (bottom > VerticalOffset + ViewportHeight) SetVerticalOffset(bottom - ViewportHeight);
        return new Rect(0, top, _itemWidth, ItemHeight);
    }
}
