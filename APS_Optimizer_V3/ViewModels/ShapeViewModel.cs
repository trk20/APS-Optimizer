using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using APS_Optimizer_V3.Helpers; // Correct namespace

namespace APS_Optimizer_V3.ViewModels;
// Represents a point (row, col) or (x, y)
using Point = System.ValueTuple<int, int>;

public partial class ShapeViewModel : ViewModelBase
{
    private string _name = "Unnamed Shape";
    private int _currentRotationIndex = 0;
    private List<bool[,]> _rotations = new List<bool[,]>(); // Store boolean grids for each rotation

    // --- Public Properties for Binding ---
    public string Name { get => _name; set => SetProperty(ref _name, value); }

    // Collection of CellViewModels for the *currently selected rotation* to display
    public ObservableCollection<CellViewModel> PreviewCells { get; } = new();

    // Width and Height of the *current rotation's* bounding box for layout
    private int _previewWidth = 0;
    public int PreviewWidth { get => _previewWidth; private set => SetProperty(ref _previewWidth, value); }

    private int _previewHeight = 0;
    public int PreviewHeight { get => _previewHeight; private set => SetProperty(ref _previewHeight, value); }

    // Calculated Width for the ItemsRepeater in XAML
    private const double PreviewCellSize = 10.0; // Smaller cells for preview
    private const double PreviewCellSpacing = 1.0;
    public double CalculatedPreviewWidth => PreviewWidth <= 0 ? PreviewCellSize : (PreviewWidth * PreviewCellSize) + ((Math.Max(0, PreviewWidth - 1)) * PreviewCellSpacing);

    // --- Constructor and Methods ---
    public ShapeViewModel(string name, bool[,] baseShape)
    {
        Name = name;
        GenerateRotations(baseShape);
        UpdatePreview(); // Initialize preview with the first rotation
    }

    private void GenerateRotations(bool[,] baseShape)
    {
        _rotations.Clear();
        if (baseShape == null || baseShape.Length == 0) return;

        bool[,] current = baseShape;
        for (int i = 0; i < 4; i++) // Max 4 rotations
        {
            // Add if unique (simple check based on dimensions and content)
            if (!_rotations.Any(r => AreMatricesEqual(r, current)))
            {
                _rotations.Add(current);
            }
            current = RotateMatrix(current);
        }
        Debug.WriteLine($"Generated {_rotations.Count} unique rotations for {Name}.");
    }

    // Helper to rotate a 2D boolean array 90 degrees clockwise
    private bool[,] RotateMatrix(bool[,] matrix)
    {
        int rows = matrix.GetLength(0);
        int cols = matrix.GetLength(1);
        bool[,] rotated = new bool[cols, rows];
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < cols; j++)
            {
                rotated[j, rows - 1 - i] = matrix[i, j];
            }
        }
        return rotated;
    }

    // Helper to check if two matrices are identical
    private bool AreMatricesEqual(bool[,] m1, bool[,] m2)
    {
        if (m1.GetLength(0) != m2.GetLength(0) || m1.GetLength(1) != m2.GetLength(1))
            return false;
        for (int i = 0; i < m1.GetLength(0); i++)
            for (int j = 0; j < m1.GetLength(1); j++)
                if (m1[i, j] != m2[i, j]) return false;
        return true;
    }


    private void UpdatePreview()
    {
        if (_rotations.Count == 0) return;

        _currentRotationIndex = _currentRotationIndex % _rotations.Count; // Wrap around index
        bool[,] currentShape = _rotations[_currentRotationIndex];
        PreviewHeight = currentShape.GetLength(0);
        PreviewWidth = currentShape.GetLength(1);

        PreviewCells.Clear();
        for (int r = 0; r < PreviewHeight; r++)
        {
            for (int c = 0; c < PreviewWidth; c++)
            {
                var state = currentShape[r, c] ? CellState.ShapePreview : CellState.Empty;
                // Pass null for click action - preview cells aren't interactive
                PreviewCells.Add(new CellViewModel(r, c, null, state));
            }
        }

        // Notify UI that layout-dependent properties have changed
        OnPropertyChanged(nameof(PreviewWidth));
        OnPropertyChanged(nameof(PreviewHeight));
        OnPropertyChanged(nameof(CalculatedPreviewWidth));
        Debug.WriteLine($"Updated preview for {Name} to rotation {_currentRotationIndex} ({PreviewWidth}x{PreviewHeight})");

    }

    // Command to cycle through rotations
    [RelayCommand]
    private void Rotate()
    {
        if (_rotations.Count > 1)
        {
            _currentRotationIndex++;
            UpdatePreview(); // Update the cells displayed
        }
        Debug.WriteLine($"Rotate command executed for {Name}. New index: {_currentRotationIndex}");
    }

    // Method to get the actual boolean grid for the current rotation (used by solver logic)
    public bool[,] GetCurrentRotationGrid()
    {
        if (_rotations.Count == 0) return new bool[0, 0];
        _currentRotationIndex = _currentRotationIndex % _rotations.Count; // Ensure valid index
        return _rotations[_currentRotationIndex];
    }

    // Method to get ALL unique rotation grids (used by solver logic)
    public List<bool[,]> GetAllRotationGrids()
    {
        return _rotations;
    }
}
