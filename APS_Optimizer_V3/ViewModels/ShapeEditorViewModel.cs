// ViewModels/ShapeEditorViewModel.cs
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using APS_Optimizer_V3.Helpers;
using System.Diagnostics;
using System; // For Action

namespace APS_Optimizer_V3.ViewModels;
public partial class ShapeEditorViewModel : ViewModelBase
{
    private string _shapeName = "New Shape";
    private int _editorGridWidth = 5; // Default size for new shapes
    private int _editorGridHeight = 5;
    private const double EditorCellSize = 20.0; // Larger cells for editing
    private const double EditorCellSpacing = 1.0;

    // Original shape reference (null if adding)
    private readonly ShapeViewModel? _originalShapeViewModel;
    public bool IsEditing => _originalShapeViewModel != null;

    public string ShapeName { get => _shapeName; set => SetProperty(ref _shapeName, value); }

    public int EditorGridWidth
    {
        get => _editorGridWidth;
        set
        {
            if (value < 1) value = 1; // Minimum size 1x1
            if (SetProperty(ref _editorGridWidth, value))
            {
                OnPropertyChanged(nameof(CalculatedEditorGridWidth));
                InitializeEditorGrid(true); // Reinitialize grid, try to preserve pattern
            }
        }
    }

    public int EditorGridHeight
    {
        get => _editorGridHeight;
        set
        {
            if (value < 1) value = 1; // Minimum size 1x1
            if (SetProperty(ref _editorGridHeight, value))
            {
                InitializeEditorGrid(true); // Reinitialize grid, try to preserve pattern
            }
        }
    }

    // Width calculation for the ItemsRepeater binding
    public double CalculatedEditorGridWidth => EditorGridWidth <= 0 ? EditorCellSize : (EditorGridWidth * EditorCellSize) + ((Math.Max(0, EditorGridWidth - 1)) * EditorCellSpacing);

    public ObservableCollection<CellViewModel> EditorCells { get; } = new();

    // Constructor for Adding a new shape
    public ShapeEditorViewModel()
    {
        _originalShapeViewModel = null;
        InitializeEditorGrid(false);
    }

    // Constructor for Editing an existing shape
    public ShapeEditorViewModel(ShapeViewModel shapeToEdit)
    {
        _originalShapeViewModel = shapeToEdit;
        ShapeName = shapeToEdit.Name;

        // Initialize size and pattern from the shape's *base* rotation (rotation 0)
        bool[,] basePattern = shapeToEdit.GetBaseRotationGrid(); // Need this method in ShapeViewModel
        EditorGridHeight = basePattern.GetLength(0);
        EditorGridWidth = basePattern.GetLength(1);
        // InitializeEditorGrid will be called by Width/Height setters, pass pattern
        InitializeEditorGrid(false, basePattern);
    }


    private void InitializeEditorGrid(bool preservePattern, bool[,]? initialPattern = null)
    {
        Debug.WriteLine($"Initializing editor grid {EditorGridWidth}x{EditorGridHeight}. Preserve: {preservePattern}, HasInitial: {initialPattern != null}");

        bool[,] oldPattern = new bool[0, 0];
        if (preservePattern && EditorCells.Any())
        {
            // Store old pattern before clearing (if preserving)
            oldPattern = GetCurrentPattern();
        }
        else if (initialPattern != null)
        {
            oldPattern = initialPattern; // Use provided pattern directly
        }


        EditorCells.Clear();
        if (EditorGridWidth <= 0 || EditorGridHeight <= 0) return;

        for (int r = 0; r < EditorGridHeight; r++)
        {
            for (int c = 0; c < EditorGridWidth; c++)
            {
                CellState initialState = CellState.Empty;
                // Restore state if preserving and within bounds, or from initial pattern
                if ((preservePattern || initialPattern != null) && r < oldPattern.GetLength(0) && c < oldPattern.GetLength(1) && oldPattern[r, c])
                {
                    initialState = CellState.ShapePreview;
                }
                EditorCells.Add(new CellViewModel(r, c, HandleEditorCellClick, initialState));
            }
        }
    }

    private void HandleEditorCellClick(CellViewModel cell)
    {
        // Toggle between Empty (White) and ShapePreview (Cyan)
        cell.State = cell.State == CellState.Empty ? CellState.ShapePreview : CellState.Empty;
        Debug.WriteLine($"Editor Cell ({cell.Row},{cell.Col}) state changed to: {cell.State}");
    }

    // Method to extract the boolean pattern from the editor cells
    public bool[,] GetCurrentPattern()
    {
        if (EditorGridHeight <= 0 || EditorGridWidth <= 0) return new bool[0, 0];

        bool[,] pattern = new bool[EditorGridHeight, EditorGridWidth];
        foreach (var cell in EditorCells)
        {
            if (cell.Row < EditorGridHeight && cell.Col < EditorGridWidth) // Bounds check
            {
                pattern[cell.Row, cell.Col] = (cell.State == CellState.ShapePreview);
            }
        }
        return TrimPattern(pattern); // Trim empty rows/cols before returning
    }

    // Helper to remove empty rows and columns from the pattern edges
    private bool[,] TrimPattern(bool[,] pattern)
    {
        if (pattern.Length == 0) return pattern;

        int minRow = pattern.GetLength(0), maxRow = -1, minCol = pattern.GetLength(1), maxCol = -1;

        for (int r = 0; r < pattern.GetLength(0); r++)
        {
            for (int c = 0; c < pattern.GetLength(1); c++)
            {
                if (pattern[r, c])
                {
                    minRow = Math.Min(minRow, r);
                    maxRow = Math.Max(maxRow, r);
                    minCol = Math.Min(minCol, c);
                    maxCol = Math.Max(maxCol, c);
                }
            }
        }

        // Check if any 'true' cell was found
        if (maxRow == -1) return new bool[0, 0]; // Empty pattern

        int newHeight = maxRow - minRow + 1;
        int newWidth = maxCol - minCol + 1;

        bool[,] trimmed = new bool[newHeight, newWidth];
        for (int r = 0; r < newHeight; r++)
        {
            for (int c = 0; c < newWidth; c++)
            {
                trimmed[r, c] = pattern[minRow + r, minCol + c];
            }
        }
        return trimmed;
    }

    // Note: Confirm/Cancel logic will be handled by the ContentDialog result
}