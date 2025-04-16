// MainPage.xaml.cs
using System.ComponentModel;
using System.Drawing;
using APS_Optimizer_V3.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Path = Microsoft.UI.Xaml.Shapes.Path; // Ensure ViewModel namespace is included
using Point = Windows.Foundation.Point; // Ensure Point is from Windows.Foundation
using Size = Windows.Foundation.Size; // Ensure Size is from Windows.Foundation
using Brush = Microsoft.UI.Xaml.Media.Brush; // Ensure Brush is from Microsoft.UI.Xaml.Media

namespace APS_Optimizer_V3;
public sealed partial class MainPage : Page
{
    // Helper to access the ViewModel strongly-typed
    public MainViewModel ViewModel => DataContext as MainViewModel ??
                                      throw new InvalidOperationException("DataContext is not MainViewModel");

    public MainPage()
    {
        this.InitializeComponent();
        // Hook into Unloaded event for cleanup if MainViewModel is IDisposable
        // (Assuming MainViewModel implements IDisposable from previous steps)
        this.Loaded += MainPage_Loaded;
        this.Unloaded += MainPage_Unloaded;
    }

    private void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is INotifyPropertyChanged npc)
        {
            npc.PropertyChanged += ViewModel_PropertyChanged;
        }
        EditorGridBorder.SizeChanged += OverlayArea_SizeChanged;
        UpdateSymmetryOverlay(); // Initial draw
    }

    private void MainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is INotifyPropertyChanged npc)
        {
            npc.PropertyChanged -= ViewModel_PropertyChanged;
        }
        EditorGridBorder.SizeChanged -= OverlayArea_SizeChanged; // Unsubscribe

        if (this.DataContext is IDisposable disposableViewModel)
        {
            disposableViewModel.Dispose();
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // ONLY react explicitly to symmetry selection changes here.
        // Size changes are handled by OverlayArea_SizeChanged.
        if (e.PropertyName == nameof(ViewModel.SelectedSymmetryType) || e.PropertyName == nameof(ViewModel.SelectedSymmetry))
        {
            // Enqueue the update to allow layout potentially finish first
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, UpdateSymmetryOverlay);
        }
    }

    private void OverlayArea_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Redraw overlay AFTER the grid area size has actually changed
        UpdateSymmetryOverlay();
    }

    private void UpdateSymmetryOverlay()
    {
        // Ensure ViewModel and Canvas are available
        if (ViewModel == null || SymmetryOverlayCanvas == null)
        {
            Console.WriteLine("ViewModel or SymmetryOverlayCanvas is null.");
            return;
        }
        ;

        SymmetryOverlayCanvas.Children.Clear(); // Clear previous indicators
        SymmetryOverlayCanvas.Height = EditorGridBorder.ActualHeight; // Set canvas height to match the grid area
        SymmetryOverlayCanvas.Width = EditorGridBorder.ActualWidth; // Set canvas width to match the grid area
        double width = SymmetryOverlayCanvas.Width;
        double height = SymmetryOverlayCanvas.Height;
        if (width <= 0 || height <= 0)
        {
            Console.WriteLine("Invalid canvas size for symmetry overlay.");
            return;
        }
        ; // Don't draw if size is invalid

        double centerX = width / 2.0;
        double centerY = height / 2.0;

        double arrowRadius = 15;//Math.Max(Math.Min(width, height) * 0.25, 10);

        var reflexiveStrokeBrush = new SolidColorBrush(Colors.Cyan)
        {
            Opacity = 0.7 // Semi-transparent for better visibility
        };

        var rotationalStrokeBrush = new SolidColorBrush(Colors.Red)
        {
            Opacity = 0.7 // Semi-transparent for better visibility
        };
        double strokeThickness = 2.0;
        var strokeDashArray = new DoubleCollection() { 2, 3 }; // Dashed line style

        switch (ViewModel.SelectedSymmetryType)
        {
            case SelectedSymmetryType.Horizontal:
            case SelectedSymmetryType.Quadrants:
                var hLine = new Line
                {
                    X1 = 0,
                    Y1 = centerY,
                    X2 = width,
                    Y2 = centerY,
                    Stroke = reflexiveStrokeBrush,
                    StrokeThickness = strokeThickness,
                    StrokeDashArray = strokeDashArray
                };
                SymmetryOverlayCanvas.Children.Add(hLine);
                if (ViewModel.SelectedSymmetryType != SelectedSymmetryType.Quadrants) break; // Only draw H if not Two Line
                goto case SelectedSymmetryType.Vertical; // Fallthrough for Two Line

            case SelectedSymmetryType.Vertical:
                var vLine = new Line
                {
                    X1 = centerX,
                    Y1 = 0,
                    X2 = centerX,
                    Y2 = height,
                    Stroke = reflexiveStrokeBrush,
                    StrokeThickness = strokeThickness,
                    StrokeDashArray = strokeDashArray
                };
                SymmetryOverlayCanvas.Children.Add(vLine);
                break;

            case SelectedSymmetryType.Rotational180:
                // Draw two curved arrows
                // Arrow 1: Top-Right to Bottom-Left (approx -45 to 135 deg)
                Path arrow1_180 = CreateCurvedArrowPath(centerX, centerY, arrowRadius, 90, 270, rotationalStrokeBrush, strokeThickness, 10);
                SymmetryOverlayCanvas.Children.Add(arrow1_180);
                Path arrow2_180 = CreateCurvedArrowPath(centerX, centerY, arrowRadius, 270, 450, rotationalStrokeBrush, strokeThickness, 10);
                SymmetryOverlayCanvas.Children.Add(arrow2_180);
                break;

            case SelectedSymmetryType.Rotational90:
                // Draw four curved arrows
                // Arrow 1: Top-Right quadrant (approx -45 to 45 deg)
                Path arrow1_90 = CreateCurvedArrowPath(centerX, centerY, arrowRadius, 0, 90, rotationalStrokeBrush, strokeThickness, 10);
                SymmetryOverlayCanvas.Children.Add(arrow1_90);
                Path arrow2_90 = CreateCurvedArrowPath(centerX, centerY, arrowRadius, 90, 180, rotationalStrokeBrush, strokeThickness, 10);
                SymmetryOverlayCanvas.Children.Add(arrow2_90);
                Path arrow3_90 = CreateCurvedArrowPath(centerX, centerY, arrowRadius, 180, 270, rotationalStrokeBrush, strokeThickness, 10);
                SymmetryOverlayCanvas.Children.Add(arrow3_90);
                Path arrow4_90 = CreateCurvedArrowPath(centerX, centerY, arrowRadius, 270, 360, rotationalStrokeBrush, strokeThickness, 10);
                SymmetryOverlayCanvas.Children.Add(arrow4_90);
                break;

            case SelectedSymmetryType.None:
            default:
                // Do nothing, canvas is already cleared
                break;
        }
    }



    private async void EditShapeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        // Get the MenuFlyoutItem that was clicked
        if (sender is MenuFlyoutItem menuItem)
        {
            // Get the DataContext of the MenuFlyoutItem, which is the ShapeViewModel
            if (menuItem.DataContext is ShapeViewModel shapeToEdit)
            {
                // Get the XamlRoot from the MenuFlyoutItem itself (most reliable way from a flyout)
                var xamlRoot = menuItem.XamlRoot;

                if (xamlRoot != null)
                {
                    // Call the ViewModel's method to show the dialog
                    // No need for the separate ShowEditShapeDialogCommand anymore
                    await CustomViewModel.ShowEditShapeDialog(shapeToEdit, XamlRoot);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Error: Could not get XamlRoot from MenuFlyoutItem.");
                    // TODO: Show error to user?
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Error: DataContext of MenuFlyoutItem is not a ShapeViewModel.");
            }
        }
    }

    private async void RemoveShapeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem menuItem)
        {
            if (menuItem.DataContext is ShapeViewModel shapeToRemove)
            {
                var xamlRoot = menuItem.XamlRoot;
                if (xamlRoot != null)
                {
                    await ViewModel.RequestRemoveShape(shapeToRemove, xamlRoot);
                }
                else
                {
                    Console.WriteLine("Error: Could not get XamlRoot from MenuFlyoutItem for removal.");
                }
            }
            else
            {
                Console.WriteLine("Error: DataContext of MenuFlyoutItem is not a ShapeViewModel for removal.");
            }
        }
    }

    private Path CreateCurvedArrowPath(
        double centerX, double centerY,
        double radius,
        double startAngleDeg, double endAngleDeg,
        Brush strokeBrush, double strokeThickness,
        double gapAngleDeg = 5.0)
    {
        // Apply GAP to shorten the arc
        double effectiveStartAngleDeg = startAngleDeg + gapAngleDeg;
        double effectiveEndAngleDeg = endAngleDeg - gapAngleDeg;

        if (effectiveStartAngleDeg >= effectiveEndAngleDeg)
        {
            effectiveEndAngleDeg = effectiveStartAngleDeg + 0.1;
            if (effectiveStartAngleDeg >= endAngleDeg) return new Path();
        }

        // Convert effective angles to radians
        double startAngleRad = effectiveStartAngleDeg * Math.PI / 180.0;
        double endAngleRad = effectiveEndAngleDeg * Math.PI / 180.0; // Use effective end angle

        // Calculate start and end points of the shortened arc
        Point startPoint = new Point(centerX + radius * Math.Cos(startAngleRad),
                                     centerY + radius * Math.Sin(startAngleRad));
        Point endPoint = new Point(centerX + radius * Math.Cos(endAngleRad),
                                   centerY + radius * Math.Sin(endAngleRad));

        // Arc segment for the curve
        var arcSegment = new ArcSegment
        {
            Point = endPoint,
            Size = new Size(radius, radius),
            IsLargeArc = Math.Abs(endAngleDeg - startAngleDeg) > 180,
            SweepDirection = SweepDirection.Clockwise,
            RotationAngle = 0
        };

        // --- Arrowhead Calculation (Based on last arc segment direction) ---
        double arrowHeadAngle = 30.0 * Math.PI / 180.0; // Angle of arrowhead lines relative to back direction
        double arrowHeadLength = 6.0;

        // Calculate a point slightly *before* the endpoint on the arc
        double preEndAngleRad = endAngleRad - (1.0 * Math.PI / 180.0); // Look back 1 degree
        Point preEndPoint = new Point(centerX + radius * Math.Cos(preEndAngleRad),
                                      centerY + radius * Math.Sin(preEndAngleRad));

        // Calculate the angle of the vector pointing from preEndPoint to endPoint
        // This approximates the direction of the curve at its very end
        double dx = endPoint.X - preEndPoint.X;
        double dy = endPoint.Y - preEndPoint.Y;
        double finalSegmentAngleRad = Math.Atan2(dy, dx);

        // Calculate the angle pointing *backwards* along this final segment
        double backwardAngleRad = finalSegmentAngleRad + Math.PI;

        // Calculate the two points for the arrowhead lines, deviating from the backward direction
        // Point 1
        double angle1 = backwardAngleRad + arrowHeadAngle;
        double arrowPoint1X = endPoint.X + arrowHeadLength * Math.Cos(angle1);
        double arrowPoint1Y = endPoint.Y + arrowHeadLength * Math.Sin(angle1);
        // Point 2
        double angle2 = backwardAngleRad - arrowHeadAngle;
        double arrowPoint2X = endPoint.X + arrowHeadLength * Math.Cos(angle2);
        double arrowPoint2Y = endPoint.Y + arrowHeadLength * Math.Sin(angle2);
        // --- End Arrowhead Calculation ---


        // Define segments for the arrowhead lines originating from the endpoint
        var lineToArrowPoint1 = new LineSegment { Point = new Point(arrowPoint1X, arrowPoint1Y) };
        var lineBackToEndpoint = new LineSegment { Point = endPoint }; // Move back to end for next line
        var lineToArrowPoint2 = new LineSegment { Point = new Point(arrowPoint2X, arrowPoint2Y) };


        // Path Figure construction (remains the same)
        var pathFigure = new PathFigure
        {
            StartPoint = startPoint,
            Segments = new PathSegmentCollection {
                arcSegment,
                lineToArrowPoint1,
                lineBackToEndpoint,
                lineToArrowPoint2
            }
        };

        // Path Geometry
        var pathGeometry = new PathGeometry { Figures = { pathFigure } };

        // Path element
        var path = new Path
        {
            Data = pathGeometry,
            Stroke = strokeBrush,
            StrokeThickness = strokeThickness
        };

        return path;
    }


}