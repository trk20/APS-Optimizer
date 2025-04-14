// ViewModels/ShapeViewModel.cs
using System.Diagnostics;
using APS_Optimizer_V3.Helpers;
using APS_Optimizer_V3.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation; // Correct namespace

namespace APS_Optimizer_V3.ViewModels;
public partial class ShapeViewModel : ObservableObject, IDisposable // Changed base class
{
    [ObservableProperty] private string _name = "Unnamed Shape";
    private int _currentRotationIndex = 0;
    private List<CellTypeInfo[,]> _rotations = new List<CellTypeInfo[,]>();

    [ObservableProperty] private bool _isEnabled = true;

    // The UIElement for the preview in the ListView
    [ObservableProperty] private Grid? _previewGrid;

    // Event for IsEnabled changes (MainViewModel subscribes)
    public event EventHandler? IsEnabledChanged;

    // The underlying Shape data
    public ShapeInfo Shape { get; private set; }

    // Preview dimensions (might still be useful)
    [ObservableProperty] private int _previewWidth = 0;
    [ObservableProperty] private int _previewHeight = 0;

    // Constants for preview appearance
    private const double PreviewCellSize = 20.0;
    private static readonly Brush PreviewDefaultBackground = new SolidColorBrush(Colors.White);
    private static readonly Brush PreviewShapeBackground = new SolidColorBrush(Colors.Cyan);

    public ShapeViewModel(ShapeInfo shape)
    {
        Shape = shape;
        Name = shape.Name; // Sync name initially
        GenerateRotations(shape.Pattern); // Generate rotations based on ShapeInfo's pattern
        UpdatePreview(); // Generate initial preview grid
    }

    // --- Rotation Logic ---

    private void GenerateRotations(CellTypeInfo[,] baseShape)
    {
        _rotations.Clear();
        if (baseShape == null || baseShape.Length == 0) return;

        // Use the ShapeInfo's generated rotations directly
        _rotations = Shape.GetAllRotationGrids();

        Debug.WriteLine($"ShapeViewModel '{Name}' using {Shape.GetAllRotationGrids().Count} unique rotations from ShapeInfo.");

        _currentRotationIndex = 0; // Reset index
        OnPropertyChanged(nameof(CanRotate)); // Notify CanRotate might have changed
    }

    // Called by MainViewModel's timer
    public void AdvanceRotation()
    {
        if (!CanRotate()) return; // Only advance if multiple rotations exist

        _currentRotationIndex = (_currentRotationIndex + 1) % _rotations.Count;
        UpdatePreview(); // Update the visual preview to show the new rotation
    }

    public bool CanRotate() => _rotations.Count > 1;

    public CellTypeInfo[,] GetCurrentRotationGrid()
    {
        if (_rotations.Count == 0) return new CellTypeInfo[0, 0];
        // Ensure index is valid
        _currentRotationIndex = _currentRotationIndex % _rotations.Count;
        return _rotations[_currentRotationIndex];
    }

    public List<CellTypeInfo[,]> GetAllRotationGrids() => _rotations;

    // --- Preview Generation ---

    private void UpdatePreview()
    {
        if (_rotations.Count == 0)
        {
            PreviewGrid = null; // No rotations, no preview
            PreviewWidth = 0;
            PreviewHeight = 0;
            return;
        }

        // Ensure index is valid before accessing
        _currentRotationIndex = _currentRotationIndex % _rotations.Count;
        CellTypeInfo[,] currentShape = _rotations[_currentRotationIndex];

        if (currentShape == null || currentShape.Rank != 2)
        {
            PreviewGrid = null; // Invalid shape data
            PreviewWidth = 0;
            PreviewHeight = 0;
            Debug.WriteLine($"Warning: Invalid shape data for preview in '{Name}'.");
            return;
        }


        int rows = currentShape.GetLength(0);
        int cols = currentShape.GetLength(1);

        // Update observable properties for dimensions if needed elsewhere
        PreviewHeight = rows;
        PreviewWidth = cols;

        var newGrid = new Grid
        {
            Background = PreviewDefaultBackground,
            Width = cols * PreviewCellSize,
            Height = rows * PreviewCellSize
            // Add a small margin/padding inside the border if desired
            // Padding = new Thickness(1)
        };

        // Define rows and columns based on PreviewCellSize
        for (int r = 0; r < rows; r++) newGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(PreviewCellSize) });
        for (int c = 0; c < cols; c++) newGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(PreviewCellSize) });

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                CellTypeInfo type = currentShape[r, c];

                // Skip null cells in the preview
                if (type == null) continue;

                // Add Icon if available
                if (!string.IsNullOrEmpty(type.IconPath)) // Don't show icon for Blocked type
                {
                    try
                    {
                        var iconUri = new Uri($"ms-appx:///Assets/{type.IconPath}");
                        var cellBorder = new Border
                        {
                            Background = PreviewShapeBackground,
                            BorderThickness = new Thickness(0.5),
                            BorderBrush = new SolidColorBrush(Colors.Black),
                            Width = PreviewCellSize,
                            Height = PreviewCellSize,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                        };
                        var image = new Image()
                        {
                            Source = iconUri,//svgSource,
                            Stretch = Stretch.Uniform,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                        };
                        cellBorder.Add(image); // Add image to the rectangle
                        // Apply rotation if needed
                        if (type.IsRotatable && type.CurrentRotation != RotationDirection.North)
                        {
                            var rotateTransform = new RotateTransform
                            {
                                Angle = (int)type.CurrentRotation * 90
                            };
                            image.RenderTransform = rotateTransform;
                            image.RenderTransformOrigin = new Point(0.5, 0.5);
                        }
                        Grid.SetRow(cellBorder, r);
                        Grid.SetColumn(cellBorder, c);

                        newGrid.Children.Add(cellBorder);
                    }
                    catch (Exception ex) { Debug.WriteLine($"Error creating preview icon for {type.Name}: {ex.Message}"); }
                }

            }
        }
        PreviewGrid = newGrid; // Update the UI property
    }

    // --- Data Update ---

    // Called after editing a shape
    public void UpdateShapeData(string newName, CellTypeInfo[,] newBaseShape)
    {
        Name = newName;
        // Update the underlying ShapeInfo
        Shape = new ShapeInfo(newName, newBaseShape, Shape.IsRotatable); // Recreate ShapeInfo to regenerate rotations
        GenerateRotations(newBaseShape); // Update local rotations list
        _currentRotationIndex = 0;
        UpdatePreview(); // Refresh the preview
        OnPropertyChanged(nameof(CanRotate)); // Notify rotation capability might have changed
        // MainViewModel needs to re-evaluate timer state and MaxPreviewColumnWidth (already handled via event/property change)
    }

    // --- Commands ---
    [RelayCommand]
    private void RequestEdit()
    {
        // This command is primarily for binding in XAML.
        // The actual logic is handled by the code-behind (e.g., EditShapeMenuItem_Click)
        // which calls MainViewModel.ShowEditShapeDialog.
        Debug.WriteLine($"Edit requested via command for shape: {Name}");
    }

    // --- Property Change Notification ---
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        // When IsEnabled changes, raise the event for MainViewModel
        if (e.PropertyName == nameof(IsEnabled))
        {
            IsEnabledChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // --- IDisposable Implementation ---
    private bool _disposed = false;

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed state (managed objects).
                IsEnabledChanged = null; // Remove event subscribers
                _previewGrid = null;     // Allow the UI grid to be garbage collected
                _rotations.Clear();      // Clear the list
                // ShapeInfo itself doesn't need explicit disposal unless it holds unmanaged resources
            }
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
