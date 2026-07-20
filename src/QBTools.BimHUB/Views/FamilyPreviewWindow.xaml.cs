using QBTools.BimHUB.Models;
using QBTools.BimHUB.Compatibility;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace QBTools.BimHUB.Views;

public partial class FamilyPreviewWindow : Window
{
    private const double MinimumZoom = 0.1;
    private const double MaximumZoom = 8;
    private const double ZoomStep = 1.15;

    private bool _isManuallyZoomed;
    private double _zoom = 1;

    public FamilyPreviewWindow(object item)
    {
        InitializeComponent();
        DataContext = item;
        ContentRendered += FamilyPreviewWindow_ContentRendered;
    }

    private void FamilyPreviewWindow_ContentRendered(object? sender, EventArgs e)
    {
        FitImageToWindow();
    }

    private void PreviewScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }

        Point cursor = e.GetPosition(PreviewScrollViewer);
        _isManuallyZoomed = true;
        SetZoom(e.Delta > 0 ? _zoom * ZoomStep : _zoom / ZoomStep, cursor);
        e.Handled = true;
    }

    private void SetZoom(double requestedZoom, Point? cursor)
    {
        double newZoom = RuntimeCompatibility.Clamp(requestedZoom, MinimumZoom, MaximumZoom);
        if (Math.Abs(newZoom - _zoom) < 0.001)
        {
            return;
        }

        double horizontalRatio = PreviewScrollViewer.ExtentWidth <= 0
            ? 0.5
            : (PreviewScrollViewer.HorizontalOffset + (cursor?.X ?? PreviewScrollViewer.ViewportWidth / 2))
              / PreviewScrollViewer.ExtentWidth;
        double verticalRatio = PreviewScrollViewer.ExtentHeight <= 0
            ? 0.5
            : (PreviewScrollViewer.VerticalOffset + (cursor?.Y ?? PreviewScrollViewer.ViewportHeight / 2))
              / PreviewScrollViewer.ExtentHeight;

        _zoom = newZoom;
        ImageScaleTransform.ScaleX = _zoom;
        ImageScaleTransform.ScaleY = _zoom;
        PreviewScrollViewer.UpdateLayout();

        double anchorX = cursor?.X ?? PreviewScrollViewer.ViewportWidth / 2;
        double anchorY = cursor?.Y ?? PreviewScrollViewer.ViewportHeight / 2;
        PreviewScrollViewer.ScrollToHorizontalOffset(
            Math.Max(0, horizontalRatio * PreviewScrollViewer.ExtentWidth - anchorX));
        PreviewScrollViewer.ScrollToVerticalOffset(
            Math.Max(0, verticalRatio * PreviewScrollViewer.ExtentHeight - anchorY));
    }

    private void FitImageToWindow()
    {
        if (PreviewImage.Source is not BitmapSource source
            || source.PixelWidth <= 0
            || source.PixelHeight <= 0)
        {
            SetZoom(1, null);
            return;
        }

        double availableWidth = Math.Max(1, PreviewScrollViewer.ViewportWidth - 24);
        double availableHeight = Math.Max(1, PreviewScrollViewer.ViewportHeight - 24);
        double fitZoom = Math.Min(
            availableWidth / source.PixelWidth,
            availableHeight / source.PixelHeight);
        SetZoom(fitZoom, null);
        CenterImage();
    }

    private void CenterImage()
    {
        PreviewScrollViewer.UpdateLayout();
        PreviewScrollViewer.ScrollToHorizontalOffset(
            Math.Max(0, (PreviewScrollViewer.ExtentWidth - PreviewScrollViewer.ViewportWidth) / 2));
        PreviewScrollViewer.ScrollToVerticalOffset(
            Math.Max(0, (PreviewScrollViewer.ExtentHeight - PreviewScrollViewer.ViewportHeight) / 2));
    }

    private void FamilyPreviewWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isManuallyZoomed || !IsLoaded)
        {
            return;
        }

        Dispatcher.BeginInvoke(FitImageToWindow, DispatcherPriority.Loaded);
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }
}
