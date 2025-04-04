// ViewModels/ShapeEditorViewModel.cs
using System.Collections.ObjectModel;
using APS_Optimizer_V3.Helpers;
using System.Diagnostics;

namespace APS_Optimizer_V3.ViewModels;
public partial class ShapeEditorViewModel : ViewModelBase
{
    private string _shapeName = "New Shape";
    private int _editorGridWidth = 3;
    private int _editorGridHeight = 3;
    private const double EditorCellSize = 20.0;
    private const double EditorCellSpacing = 1.0;

    // --- Internal state for the pattern ---
    private bool[,] _currentPattern = new bool[3, 3]; // Initialize matching default size
    // --------------------------------------

    private readonly ShapeViewModel? _originalShapeViewModel;
    public bool IsEditing => _originalShapeViewModel != null;

    public string ShapeName { get => _shapeName; set => SetProperty(ref _shapeName, value); }

    public int EditorGridWidth
    {
        get => _editorGridWidth;
        set
        {
            int targetValue = Math.Max(1, value);
            if (targetValue == _editorGridWidth) return; // No actual change

            bool[,] oldPattern = _currentPattern; // Capture internal state BEFORE changing dimensions

            // Update property FIRST
            if (SetProperty(ref _editorGridWidth, targetValue))
            {
                OnPropertyChanged(nameof(CalculatedEditorGridWidth));

                // Create new pattern and copy old data
                bool[,] newPattern = new bool[_editorGridHeight, targetValue]; // Use current height, new width
                int rowsToCopy = Math.Min(_editorGridHeight, oldPattern.GetLength(0));
                int colsToCopy = Math.Min(targetValue, oldPattern.GetLength(1)); // Use new width for cols bound
                Debug.WriteLine($"Resizing Width: Copying {rowsToCopy}x{colsToCopy} from {oldPattern.GetLength(0)}x{oldPattern.GetLength(1)} to {_editorGridHeight}x{targetValue}");
                for (int r = 0; r < rowsToCopy; r++)
                {
                    for (int c = 0; c < colsToCopy; c++)
                    {
                        newPattern[r, c] = oldPattern[r, c];
                    }
                }
                _currentPattern = newPattern; // Update internal state

                RebuildEditorCellsFromPattern(); // Update UI collection
            }
        }
    }

    public int EditorGridHeight
    {
        get => _editorGridHeight;
        set
        {
            int targetValue = Math.Max(1, value);
            if (targetValue == _editorGridHeight) return; // No actual change

            bool[,] oldPattern = _currentPattern; // Capture internal state BEFORE changing dimensions

            // Update property FIRST
            if (SetProperty(ref _editorGridHeight, targetValue))
            {
                // Create new pattern and copy old data
                bool[,] newPattern = new bool[targetValue, _editorGridWidth]; // Use new height, current width
                int rowsToCopy = Math.Min(targetValue, oldPattern.GetLength(0)); // Use new height for rows bound
                int colsToCopy = Math.Min(_editorGridWidth, oldPattern.GetLength(1));
                Debug.WriteLine($"Resizing Height: Copying {rowsToCopy}x{colsToCopy} from {oldPattern.GetLength(0)}x{oldPattern.GetLength(1)} to {targetValue}x{_editorGridWidth}");
                for (int r = 0; r < rowsToCopy; r++)
                {
                    for (int c = 0; c < colsToCopy; c++)
                    {
                        newPattern[r, c] = oldPattern[r, c];
                    }
                }
                _currentPattern = newPattern; // Update internal state

                RebuildEditorCellsFromPattern(); // Update UI collection
            }
        }
    }

    public double CalculatedEditorGridWidth
    {
        get
        {
            if (EditorGridWidth <= 0) return EditorCellSize;
            double calculated = (EditorGridWidth * EditorCellSize) + ((Math.Max(0, EditorGridWidth - 1)) * EditorCellSpacing);
            return calculated + 1; // Add a small fraction
        }
    }

    public ObservableCollection<CellViewModel> EditorCells { get; } = new();

    // Constructor for Adding
    public ShapeEditorViewModel()
    {
        _originalShapeViewModel = null;
        // Initialize internal pattern to default size
        _currentPattern = new bool[_editorGridHeight, _editorGridWidth];
        RebuildEditorCellsFromPattern(); // Build initial UI
    }

    // Constructor for Editing
    public ShapeEditorViewModel(ShapeViewModel shapeToEdit)
    {
        _originalShapeViewModel = shapeToEdit;
        ShapeName = shapeToEdit.Name;
        bool[,] basePattern = shapeToEdit.GetBaseRotationGrid();

        // Set fields directly, update internal pattern, notify, then build UI
        _editorGridHeight = basePattern.GetLength(0);
        _editorGridWidth = basePattern.GetLength(1);
        _currentPattern = (bool[,])basePattern.Clone(); // Use a clone for internal state

        OnPropertyChanged(nameof(EditorGridHeight));
        OnPropertyChanged(nameof(EditorGridWidth));
        OnPropertyChanged(nameof(CalculatedEditorGridWidth));

        RebuildEditorCellsFromPattern(); // Build UI from loaded pattern
    }

    // Rebuilds the entire EditorCells collection based on _currentPattern
    private void RebuildEditorCellsFromPattern()
    {
        Debug.WriteLine($"Rebuilding editor cells. Grid Size={EditorGridWidth}x{EditorGridHeight}, Pattern Size={_currentPattern.GetLength(1)}x{_currentPattern.GetLength(0)}");
        EditorCells.Clear();
        // Use property dimensions (_editorGridWidth/_Height) for the visual grid size
        if (EditorGridWidth <= 0 || EditorGridHeight <= 0) return;

        for (int r = 0; r < EditorGridHeight; r++) // Loop to NEW Height
        {
            for (int c = 0; c < EditorGridWidth; c++) // Loop to NEW Width
            {
                CellState initialState = CellState.Empty;
                // Check within bounds of the _currentPattern array when reading state
                if (r < _currentPattern.GetLength(0) && c < _currentPattern.GetLength(1) && _currentPattern[r, c])
                {
                    initialState = CellState.ShapePreview;
                }
                EditorCells.Add(new CellViewModel(r, c, HandleEditorCellClick, initialState));
            }
        }
        // Ensure calculated width updates if it relies on EditorCells somehow (it shouldn't now)
        OnPropertyChanged(nameof(CalculatedEditorGridWidth));
        Debug.WriteLine($"Rebuilt {EditorCells.Count} cells.");
    }


    private void HandleEditorCellClick(CellViewModel cell)
    {
        // Check bounds against the current _editorGridHeight/Width properties,
        // as these define the valid clickable area represented by EditorCells.
        if (cell.Row < _editorGridHeight && cell.Col < _editorGridWidth)
        {
            // Also check bounds against the internal pattern BEFORE trying to modify it
            if (cell.Row < _currentPattern.GetLength(0) && cell.Col < _currentPattern.GetLength(1))
            {
                // Toggle the internal state FIRST
                _currentPattern[cell.Row, cell.Col] = !_currentPattern[cell.Row, cell.Col];

                // THEN update the cell's visual state
                cell.State = _currentPattern[cell.Row, cell.Col] ? CellState.ShapePreview : CellState.Empty;

                Debug.WriteLine($"Editor Cell ({cell.Row},{cell.Col}) state changed to: {cell.State}");
            }
            else
            {
                Debug.WriteLine($"WARN: Clicked cell ({cell.Row},{cell.Col}) is within UI grid but out of bounds for internal pattern {_currentPattern.GetLength(0)}x{_currentPattern.GetLength(1)} - This might happen briefly during resize.");
                // Optionally, just update the visual state to Empty if it was clicked outside the pattern bounds?
                // cell.State = CellState.Empty;
            }
        }
        else
        {
            Debug.WriteLine($"WARN: Clicked cell ({cell.Row},{cell.Col}) out of bounds for UI grid {_editorGridHeight}x{_editorGridWidth}");
        }
    }

    // Gets FINAL pattern for saving (trims the internal state)
    public bool[,] GetCurrentPattern()
    {
        // Pass the current dimensions to ensure the untrimmed pattern matches the grid size
        bool[,] patternToTrim = new bool[_editorGridHeight, _editorGridWidth];
        int rows = Math.Min(_editorGridHeight, _currentPattern.GetLength(0));
        int cols = Math.Min(_editorGridWidth, _currentPattern.GetLength(1));
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                patternToTrim[r, c] = _currentPattern[r, c];
            }
        }

        return TrimPattern(patternToTrim);
    }

    // TrimPattern helper (no changes needed)
    private bool[,] TrimPattern(bool[,] pattern)
    {
        if (pattern.Length == 0) return pattern;
        int minRow = pattern.GetLength(0), maxRow = -1, minCol = pattern.GetLength(1), maxCol = -1;
        for (int r = 0; r < pattern.GetLength(0); r++) for (int c = 0; c < pattern.GetLength(1); c++) if (pattern[r, c]) { minRow = Math.Min(minRow, r); maxRow = Math.Max(maxRow, r); minCol = Math.Min(minCol, c); maxCol = Math.Max(maxCol, c); }
        if (maxRow == -1) return new bool[0, 0]; // Empty pattern
        int newHeight = maxRow - minRow + 1; int newWidth = maxCol - minCol + 1;
        bool[,] trimmed = new bool[newHeight, newWidth];
        for (int r = 0; r < newHeight; r++) for (int c = 0; c < newWidth; c++) trimmed[r, c] = pattern[minRow + r, minCol + c];
        return trimmed;
    }
}