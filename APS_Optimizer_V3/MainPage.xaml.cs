using System.ComponentModel;
using APS_Optimizer_V3.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Shapes;
using Path = Microsoft.UI.Xaml.Shapes.Path;
using Point = Windows.Foundation.Point;
using Size = Windows.Foundation.Size;
using Brush = Microsoft.UI.Xaml.Media.Brush;
using System.Diagnostics;

namespace APS_Optimizer_V3;
public sealed partial class MainPage : Page
{
    public MainViewModel ViewModel => DataContext as MainViewModel ??
                                      throw new InvalidOperationException("DataContext is not MainViewModel");

    public MainPage()
    {
        InitializeComponent();
        Loaded += MainPage_Loaded;
        Unloaded += MainPage_Unloaded;
    }

    private void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is INotifyPropertyChanged npc)
        {
            npc.PropertyChanged += ViewModel_PropertyChanged;
        }
        EditorGridBorder.SizeChanged += OverlayArea_SizeChanged;
        ResultGridBorder.SizeChanged += OverlayArea_SizeChanged;
        UpdateSymmetryOverlay(); // Initial draw
    }

    private void MainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is INotifyPropertyChanged npc)
        {
            npc.PropertyChanged -= ViewModel_PropertyChanged;
        }
        EditorGridBorder.SizeChanged -= OverlayArea_SizeChanged;
        ResultGridBorder.SizeChanged -= OverlayArea_SizeChanged;

        if (DataContext is IDisposable disposableViewModel)
        {
            disposableViewModel.Dispose();
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.SelectedSymmetryType) || e.PropertyName == nameof(ViewModel.SelectedSymmetry))
        {
            // queue the update to allow layout to finish first
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, UpdateSymmetryOverlay);
        }
    }


    private void OverlayArea_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Redraw overlay after the grid area size changed
        UpdateSymmetryOverlay();
    }

    private void UpdateSymmetryOverlay()
    {
        // Check ViewModel and Canvas exist
        if (ViewModel == null || SymmetryOverlayCanvas == null)
        {
            Console.WriteLine("ViewModel or SymmetryOverlayCanvas is null.");
            return;
        }
        double width = EditorGridBorder.ActualWidth;
        double height = EditorGridBorder.ActualHeight;

        SymmetryOverlayCanvas.Children.Clear();
        ResultSymmetryOverlayCanvas.Children.Clear();

        SymmetryOverlayCanvas.Width = width;
        SymmetryOverlayCanvas.Height = height;
        ResultSymmetryOverlayCanvas.Width = width;
        ResultSymmetryOverlayCanvas.Height = height;

        if (width <= 0 || height <= 0)
        {
            Console.WriteLine("Invalid canvas size for symmetry overlay.");
            return;
        } // Don't draw if size is invalid

        double centerX = width / 2.0;
        double centerY = height / 2.0;

        double arrowRadius = 15;

        var reflexiveStrokeBrush = new SolidColorBrush(Colors.Cyan)
        {
            Opacity = 0.7 // Semi-transparent for better visibility
        };

        var rotationalStrokeBrush = new SolidColorBrush(Colors.Red)
        {
            Opacity = 0.7
        };

        var resultReflexiveStrokeBrush = new SolidColorBrush(Colors.Cyan)
        {
            Opacity = 0.4 // result overlay less opaque
        };

        var resultRotationalStrokeBrush = new SolidColorBrush(Colors.Red)
        {
            Opacity = 0.4
        };

        double strokeThickness = 2.0;
        var strokeDashArray = new DoubleCollection() { 2, 3 }; // Dashed line

        switch (ViewModel.SelectedSymmetryType)
        {
            case SelectedSymmetryType.Horizontal:
                DrawHorizontalLine(SymmetryOverlayCanvas, width, centerY, reflexiveStrokeBrush, strokeThickness, strokeDashArray);
                DrawHorizontalLine(ResultSymmetryOverlayCanvas, width, centerY, resultReflexiveStrokeBrush, strokeThickness, strokeDashArray);
                break;
            case SelectedSymmetryType.Quadrants:
                DrawHorizontalLine(SymmetryOverlayCanvas, width, centerY, reflexiveStrokeBrush, strokeThickness, strokeDashArray);
                DrawHorizontalLine(ResultSymmetryOverlayCanvas, width, centerY, resultReflexiveStrokeBrush, strokeThickness, strokeDashArray);
                DrawVerticalLine(SymmetryOverlayCanvas, height, centerX, reflexiveStrokeBrush, strokeThickness, strokeDashArray);
                DrawVerticalLine(ResultSymmetryOverlayCanvas, height, centerX, resultReflexiveStrokeBrush, strokeThickness, strokeDashArray);
                break;
            case SelectedSymmetryType.Vertical:
                DrawVerticalLine(SymmetryOverlayCanvas, height, centerX, reflexiveStrokeBrush, strokeThickness, strokeDashArray);
                DrawVerticalLine(ResultSymmetryOverlayCanvas, height, centerX, resultReflexiveStrokeBrush, strokeThickness, strokeDashArray);
                break;
            case SelectedSymmetryType.Rotational180:
                DrawRotationalArrow(SymmetryOverlayCanvas, centerX, centerY, arrowRadius, 90, 270, rotationalStrokeBrush, strokeThickness);
                DrawRotationalArrow(SymmetryOverlayCanvas, centerX, centerY, arrowRadius, 270, 450, rotationalStrokeBrush, strokeThickness);
                DrawRotationalArrow(ResultSymmetryOverlayCanvas, centerX, centerY, arrowRadius, 90, 270, resultRotationalStrokeBrush, strokeThickness);
                DrawRotationalArrow(ResultSymmetryOverlayCanvas, centerX, centerY, arrowRadius, 270, 450, resultRotationalStrokeBrush, strokeThickness);
                break;
            case SelectedSymmetryType.Rotational90:
                DrawRotationalArrow(SymmetryOverlayCanvas, centerX, centerY, arrowRadius, 0, 90, rotationalStrokeBrush, strokeThickness);
                DrawRotationalArrow(SymmetryOverlayCanvas, centerX, centerY, arrowRadius, 90, 180, rotationalStrokeBrush, strokeThickness);
                DrawRotationalArrow(SymmetryOverlayCanvas, centerX, centerY, arrowRadius, 180, 270, rotationalStrokeBrush, strokeThickness);
                DrawRotationalArrow(SymmetryOverlayCanvas, centerX, centerY, arrowRadius, 270, 360, rotationalStrokeBrush, strokeThickness);
                DrawRotationalArrow(ResultSymmetryOverlayCanvas, centerX, centerY, arrowRadius, 0, 90, resultRotationalStrokeBrush, strokeThickness);
                DrawRotationalArrow(ResultSymmetryOverlayCanvas, centerX, centerY, arrowRadius, 90, 180, resultRotationalStrokeBrush, strokeThickness);
                DrawRotationalArrow(ResultSymmetryOverlayCanvas, centerX, centerY, arrowRadius, 180, 270, resultRotationalStrokeBrush, strokeThickness);
                DrawRotationalArrow(ResultSymmetryOverlayCanvas, centerX, centerY, arrowRadius, 270, 360, resultRotationalStrokeBrush, strokeThickness);
                break;

            case SelectedSymmetryType.None:
            default:
                break;
        }
    }

    private void DrawHorizontalLine(Canvas targetCanvas, double canvasWidth, double yPosition, Brush stroke, double thickness, DoubleCollection dashArray)
    {
        var line = new Line
        {
            X1 = 0,
            Y1 = yPosition,
            X2 = canvasWidth,
            Y2 = yPosition,
            Stroke = stroke,
            StrokeThickness = thickness,
            StrokeDashArray = dashArray
        };
        targetCanvas.Children.Add(line);
    }

    private void DrawVerticalLine(Canvas targetCanvas, double canvasHeight, double xPosition, Brush stroke, double thickness, DoubleCollection dashArray)
    {
        var line = new Line
        {
            X1 = xPosition,
            Y1 = 0,
            X2 = xPosition,
            Y2 = canvasHeight,
            Stroke = stroke,
            StrokeThickness = thickness,
            StrokeDashArray = dashArray
        };
        targetCanvas.Children.Add(line);
    }

    private void DrawRotationalArrow(Canvas targetCanvas, double centerX, double centerY, double radius, double startAngleDeg, double endAngleDeg, Brush stroke, double thickness)
    {
        Path arrowPath = CreateCurvedArrowPath(centerX, centerY, radius, startAngleDeg, endAngleDeg, stroke, thickness);
        targetCanvas.Children.Add(arrowPath);
    }

    // probably over engineered but works
    private Path CreateCurvedArrowPath(
        double centerX, double centerY,
        double radius,
        double startAngleDeg, double endAngleDeg,
        Brush strokeBrush, double strokeThickness,
        double gapAngleDeg = 10.0)
    {
        // Shorten the arc
        double effectiveStartAngleDeg = startAngleDeg + gapAngleDeg;
        double effectiveEndAngleDeg = endAngleDeg - gapAngleDeg;

        if (effectiveStartAngleDeg >= effectiveEndAngleDeg) return new Path(); // Invalid arc

        // Convert angles
        double startAngleRad = effectiveStartAngleDeg * Math.PI / 180.0;
        double effectiveEndAngleRad = effectiveEndAngleDeg * Math.PI / 180.0;

        // Start and end points for shortened arc
        Point startPoint = new Point(centerX + radius * Math.Cos(startAngleRad),
                                     centerY + radius * Math.Sin(startAngleRad));
        Point arcEndPoint = new Point(centerX + radius * Math.Cos(effectiveEndAngleRad - 3 * Math.PI / 180.0), // -3 needed for some reason otherwise arrowhead is not aligned
                                   centerY + radius * Math.Sin(effectiveEndAngleRad - 3 * Math.PI / 180.0));

        // Arc segment for curve
        var arcSegment = new ArcSegment
        {
            Point = arcEndPoint,
            Size = new Size(radius, radius),
            IsLargeArc = Math.Abs(endAngleDeg - startAngleDeg) > 180, // Use original angles
            SweepDirection = SweepDirection.Clockwise,
            RotationAngle = 0
        };

        arcEndPoint = new Point(centerX + radius * Math.Cos(effectiveEndAngleRad),
                                  centerY + radius * Math.Sin(effectiveEndAngleRad));

        // --- Arrowhead Calculation ---
        double arrowHeadAngle = 30.0 * Math.PI / 180.0;
        double arrowHeadLength = 6.0;

        // Calculate the tangent angle @ effective end point
        double finalSegmentAngleRad = Math.Atan2(radius * Math.Cos(effectiveEndAngleRad),
                                                -radius * Math.Sin(effectiveEndAngleRad));

        // Angle pointing directly backward from the direction of travel
        double backwardAngleRad = finalSegmentAngleRad + Math.PI;

        // Calculate the two points for the arrowhead wings, relative to arcEndPoint
        double wingAngle1 = backwardAngleRad + arrowHeadAngle;
        double arrowPoint1X = arcEndPoint.X + arrowHeadLength * Math.Cos(wingAngle1);
        double arrowPoint1Y = arcEndPoint.Y + arrowHeadLength * Math.Sin(wingAngle1);

        double wingAngle2 = backwardAngleRad - arrowHeadAngle;
        double arrowPoint2X = arcEndPoint.X + arrowHeadLength * Math.Cos(wingAngle2);
        double arrowPoint2Y = arcEndPoint.Y + arrowHeadLength * Math.Sin(wingAngle2);

        // Define segments for the arrowhead lines originating from the arc's endpoint
        var lineToArrowPoint1 = new LineSegment { Point = new Point(arrowPoint1X, arrowPoint1Y) };
        var lineBackToEndpoint = new LineSegment { Point = arcEndPoint }; // Move back to the tip
        var lineToArrowPoint2 = new LineSegment { Point = new Point(arrowPoint2X, arrowPoint2Y) };

        // Path Figure construction
        var pathFigure = new PathFigure
        {
            StartPoint = startPoint,
            IsFilled = false,
            IsClosed = false,
            Segments = new PathSegmentCollection {
            arcSegment,          // arc to arcEndPoint
            lineToArrowPoint1,   // line from arcEndPoint to wing1
            lineBackToEndpoint,  // line from wing1 back to arcEndPoint
            lineToArrowPoint2    // line from arcEndPoint to wing2
        }
        };

        var pathGeometry = new PathGeometry { Figures = { pathFigure } };
        var path = new Path { Data = pathGeometry, Stroke = strokeBrush, StrokeThickness = strokeThickness };

        return path;
    }




}