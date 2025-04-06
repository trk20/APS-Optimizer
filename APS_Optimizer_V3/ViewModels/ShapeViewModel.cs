// ViewModels/ShapeViewModel.cs
using System.Diagnostics;
using APS_Optimizer_V3.Helpers;
using APS_Optimizer_V3.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml.Shapes; // Correct namespace

namespace APS_Optimizer_V3.ViewModels;
public partial class ShapeViewModel : ViewModelBase, IDisposable
{
    private string _name = "Unnamed Shape";
    private int _currentRotationIndex = 0;
    private List<CellType[,]> _rotations = new List<CellType[,]>();
    private bool _isEnabled = true;
    private Grid? _previewGrid;

    // --- Event for IsEnabled changes ---
    public event EventHandler? IsEnabledChanged;
    // -----------------------------------

    public Grid? PreviewGrid { get => _previewGrid; private set => SetProperty(ref _previewGrid, value); }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            // Only raise event if value actually changes
            if (SetProperty(ref _isEnabled, value))
            {
                // Notify MainViewModel that this shape's enabled status changed
                IsEnabledChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
    public string Name { get => _name; set => SetProperty(ref _name, value); }

    // PreviewWidth/Height might still be useful for other logic, keep if needed
    private int _previewWidth = 0;
    public int PreviewWidth { get => _previewWidth; private set => SetProperty(ref _previewWidth, value); }
    private int _previewHeight = 0;
    public int PreviewHeight { get => _previewHeight; private set => SetProperty(ref _previewHeight, value); }

    private const double PreviewCellSize = 10.0;

    public ShapeViewModel(string name, CellType[,] baseShape)
    {
        Name = name;
        GenerateRotations(baseShape);
        UpdatePreview(); // Initial grid generation
        // Timer is no longer managed here
    }

    // GenerateRotations, RotateMatrix, GetMatrixSignature remain the same...
    private void GenerateRotations(CellType[,] baseShape)
    {
        _rotations.Clear();
        if (baseShape == null || baseShape.Length == 0) return;

        // --- Simplified Uniqueness Check (Based on Non-Empty Cell Positions) ---
        // This doesn't distinguish shapes with same outline but different internal types.
        // A more robust signature would involve encoding the CellType values.
        HashSet<string> uniquePositionSignatures = new HashSet<string>();
        // ---

        CellType[,] current = baseShape;
        for (int i = 0; i < 4; i++) // Generate up to 4 rotations
        {
            // --- Simplified Signature ---
            string signature = GetPositionSignature(current);
            if (uniquePositionSignatures.Add(signature))
            {
                _rotations.Add((CellType[,])current.Clone());
            }
            // ---

            current = RotateMatrix(current);
        }
        Debug.WriteLine($"Generated {_rotations.Count} unique rotations (based on outline) for {Name}.");
    }

    private string GetPositionSignature(CellType[,] matrix)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        int rows = matrix.GetLength(0);
        int cols = matrix.GetLength(1);
        sb.Append($"{rows}x{cols}:");
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                sb.Append(matrix[r, c] != CellType.Empty ? '1' : '0');
            }
            sb.Append('|');
        }
        return sb.ToString();
    }


    private CellType[,] RotateMatrix(CellType[,] matrix)
    {
        if (matrix == null || matrix.Length == 0) return new CellType[0, 0];
        int rows = matrix.GetLength(0);
        int cols = matrix.GetLength(1);
        CellType[,] rotated = new CellType[cols, rows];
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < cols; j++)
            {
                CellType originalType = matrix[i, j];
                CellType rotatedType = originalType;

                // Rotate Clip types
                switch (originalType)
                {
                    case CellType.ClipN: rotatedType = CellType.ClipE; break;
                    case CellType.ClipE: rotatedType = CellType.ClipS; break;
                    case CellType.ClipS: rotatedType = CellType.ClipW; break;
                    case CellType.ClipW: rotatedType = CellType.ClipN; break;
                        // Other types remain the same relative to their cell
                }
                rotated[j, rows - 1 - i] = rotatedType;
            }
        }
        return rotated;
    }


    private void UpdatePreview()
    {
        if (_rotations.Count == 0) { PreviewGrid = null; return; }
        _currentRotationIndex = _currentRotationIndex % _rotations.Count;
        CellType[,] currentShape = _rotations[_currentRotationIndex];
        PreviewHeight = currentShape.GetLength(0);
        PreviewWidth = currentShape.GetLength(1);
        // Use SetProperty for PreviewWidth/Height if they are observable
        SetProperty(ref _previewWidth, PreviewWidth, nameof(PreviewWidth));
        SetProperty(ref _previewHeight, PreviewHeight, nameof(PreviewHeight));


        var newGrid = new Grid();
        // Define rows and columns based on PreviewCellSize
        for (int r = 0; r < PreviewHeight; r++) newGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(PreviewCellSize) });
        for (int c = 0; c < PreviewWidth; c++) newGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(PreviewCellSize) });

        for (int r = 0; r < PreviewHeight; r++)
        {
            for (int c = 0; c < PreviewWidth; c++)
            {
                CellType type = currentShape[r, c];
                if (type == CellType.Empty) continue; // Skip empty cells

                // Create a container (like Border or Grid) for each cell's visuals
                var cellContainer = new Grid // Use Grid to overlay elements easily
                {
                    Background = new SolidColorBrush(Colors.Transparent), // Base background
                    BorderBrush = new SolidColorBrush(Colors.DimGray),
                    BorderThickness = new Thickness(0.5)
                };

                // Add visuals based on type
                AddCellVisuals(cellContainer, type, PreviewCellSize);

                Grid.SetRow(cellContainer, r);
                Grid.SetColumn(cellContainer, c);
                newGrid.Children.Add(cellContainer);
            }
        }
        PreviewGrid = newGrid; // Update the UI
    }

    private void AddCellVisuals(Grid container, CellType type, double cellSize)
    {
        // Basic filled background for all non-empty types (adjust color if needed)
        container.Background = new SolidColorBrush(Colors.DarkCyan);

        // Add specific visuals based on type
        Brush iconBrush = new SolidColorBrush(Colors.White); // Icon color
        double iconStrokeThickness = 1.0;
        double padding = cellSize * 0.2; // Padding for icons inside cell
        double innerSize = cellSize - (2 * padding);

        switch (type)
        {
            case CellType.Generic:
                // Already has background, nothing more needed
                break;

            case CellType.Loader: // Hollow Circle
                var loaderCircle = new Ellipse
                {
                    Width = innerSize,
                    Height = innerSize,
                    Stroke = iconBrush,
                    StrokeThickness = iconStrokeThickness,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                container.Children.Add(loaderCircle);
                break;

            case CellType.Cooler: // Circle in Circle
                var outerCooler = new Ellipse { Width = innerSize, Height = innerSize, Fill = iconBrush, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                var innerCooler = new Ellipse { Width = innerSize * 0.5, Height = innerSize * 0.5, Fill = (Brush)container.Background, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center }; // Use background color for inner
                container.Children.Add(outerCooler);
                container.Children.Add(innerCooler);
                break;

            case CellType.ClipN:
            case CellType.ClipE:
            case CellType.ClipS:
            case CellType.ClipW:
                var clipRect = new Rectangle
                {
                    Fill = iconBrush,
                    Width = type is CellType.ClipN or CellType.ClipS ? innerSize : innerSize * 0.4, // Wider for N/S
                    Height = type is CellType.ClipE or CellType.ClipW ? innerSize : innerSize * 0.4, // Taller for E/W
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                // Adjust alignment to attach to side
                switch (type)
                {
                    case CellType.ClipN: clipRect.VerticalAlignment = VerticalAlignment.Top; break;
                    case CellType.ClipE: clipRect.HorizontalAlignment = HorizontalAlignment.Right; break;
                    case CellType.ClipS: clipRect.VerticalAlignment = VerticalAlignment.Bottom; break;
                    case CellType.ClipW: clipRect.HorizontalAlignment = HorizontalAlignment.Left; break;
                }
                container.Children.Add(clipRect);
                break;
        }
    }



    // --- Method called by MainViewModel's timer ---
    public void AdvanceRotation()
    {
        if (_rotations.Count > 1)
        {
            _currentRotationIndex++; // Increment index first
            UpdatePreview(); // Update the visual preview
        }
    }
    // --------------------------------------------

    public CellType[,] GetBaseRotationGrid()
    {
        if (_rotations.Count > 0) return _rotations[0];
        return new CellType[0, 0]; // Fallback
    }


    // Add method to update data after editing
    public void UpdateShapeData(string newName, CellType[,] newBaseShape)
    {
        Name = newName;
        GenerateRotations(newBaseShape);
        _currentRotationIndex = 0;
        UpdatePreview();
        // MainViewModel needs to re-evaluate timer state
    }


    [RelayCommand]
    private void RequestEdit()
    {
        // This command primarily exists to be bound to the UI.
        // The actual dialog showing logic will be in MainViewModel,
        // triggered by the UI interaction (e.g., context menu click).
        Debug.WriteLine($"Edit requested for shape: {Name}");
    }

    // -----------------------------

    // --- IDisposable Implementation for Cleanup ---
    private bool _disposed = false; // To detect redundant calls

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed state (managed objects).
                // No timer to stop here anymore
                IsEnabledChanged = null; // Remove event subscribers
            }

            // Free unmanaged resources (unmanaged objects) and override finalizer
            // Set large fields to null
            _previewGrid = null; // Allow the UI grid to be garbage collected
            _rotations.Clear();

            _disposed = true;
        }
    }

    // Public Dispose method
    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this); // Suppress finalization check by the GC
    }

    // Optional Finalizer (usually not needed for purely managed resources)
    // ~ShapeViewModel()
    // {
    //     Dispose(disposing: false);
    // }
    // -------------------------------------------

    public CellType[,] GetCurrentRotationGrid()
    {
        if (_rotations.Count == 0) return new CellType[0, 0];
        _currentRotationIndex = _currentRotationIndex % _rotations.Count;
        return _rotations[_currentRotationIndex];
    }

    public List<CellType[,]> GetAllRotationGrids() => _rotations;

    // Helper to check if rotation is possible
    public bool CanRotate() => _rotations.Count > 1;

    public int GetArea()
    {
        if (_rotations.Count == 0) return 0;
        // Calculate area from the base rotation
        CellType[,] baseGrid = _rotations[0];
        int area = 0;
        foreach (CellType cell in baseGrid)
        {
            if (cell != CellType.Empty)
            {
                area++;
            }
        }
        return area;
    }

}