using System.Diagnostics;
using APS_Optimizer_V3.Helpers;
using Microsoft.UI;
using Windows.Foundation;

namespace APS_Optimizer_V3.ViewModels;
public partial class ShapeViewModel : ObservableObject, IDisposable
{
    [ObservableProperty] private string _name = "Unnamed Shape";
    private int _currentRotationIndex = 0;
    private List<CellTypeInfo[,]> _rotations = new List<CellTypeInfo[,]>();

    [ObservableProperty] private bool _isEnabled = true;

    // UIElement for preview in ListView
    [ObservableProperty] private Grid? _previewGrid;

    // Event for IsEnabled changes (MainViewModel subscribes)
    public event EventHandler? IsEnabledChanged;

    // Shape data
    public ShapeInfo Shape { get; private set; }

    // Constants for appearance
    private const double PreviewCellSize = 20.0;
    private static readonly Brush PreviewDefaultBackground = new SolidColorBrush(Colors.White);
    private static readonly Brush PreviewShapeBackground = new SolidColorBrush(Colors.Cyan);

    public ShapeViewModel(ShapeInfo shape)
    {
        Shape = shape;
        Name = shape.Name;
        GenerateRotations(shape.Pattern); // Generate rotations based on shape pattern
        UpdatePreview(); // Generate initial preview grid
    }

    // --- Rotation Logic ---
    private void GenerateRotations(CellTypeInfo[,] baseShape)
    {
        _rotations.Clear();
        if (baseShape == null || baseShape.Length == 0) return;

        // Use the Shape's generated rotations directly
        _rotations = Shape.GetAllRotationGrids();

        //Debug.WriteLine($"ShapeViewModel '{Name}' using {Shape.GetAllRotationGrids().Count} unique rotations from ShapeInfo.");

        _currentRotationIndex = 0; // Reset index
        OnPropertyChanged(nameof(CanRotate)); // Notify CanRotate
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
        // Check index is valid
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
            return;
        }

        // Check index is valid
        _currentRotationIndex = _currentRotationIndex % _rotations.Count;
        CellTypeInfo[,] currentShape = _rotations[_currentRotationIndex];

        if (currentShape == null || currentShape.Rank != 2)
        {
            PreviewGrid = null; // Invalid shape data
            Debug.WriteLine($"Warning: Invalid shape data for preview in '{Name}'.");
            return;
        }


        int rows = currentShape.GetLength(0);
        int cols = currentShape.GetLength(1);

        var newGrid = new Grid
        {
            Background = PreviewDefaultBackground,
            Width = cols * PreviewCellSize,
            Height = rows * PreviewCellSize
        };

        // Define rows and columns 
        for (int r = 0; r < rows; r++) newGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(PreviewCellSize) });
        for (int c = 0; c < cols; c++) newGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(PreviewCellSize) });

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                CellTypeInfo type = currentShape[r, c];

                if (type == null) continue;

                if (!string.IsNullOrEmpty(type.IconPath))
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
                            Source = iconUri,
                            Stretch = Stretch.Uniform,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                        };
                        cellBorder.Add(image);
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

    // --- Property Change Notification ---
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(IsEnabled))
        {
            IsEnabledChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool _disposed = false;

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                IsEnabledChanged = null;
                PreviewGrid = null;
                _rotations.Clear();
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
