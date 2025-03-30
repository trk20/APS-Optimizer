// ViewModels/ShapeViewModel.cs
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using APS_Optimizer_V3.Helpers;
using Microsoft.UI; // Correct namespace

namespace APS_Optimizer_V3.ViewModels;
using Point = System.ValueTuple<int, int>;

public partial class ShapeViewModel : ViewModelBase
{
    private string _name = "Unnamed Shape";
    private int _currentRotationIndex = 0;
    private List<bool[,]> _rotations = new List<bool[,]>();
    private bool _isEnabled = true;

    // --- Property to hold the generated UI Grid ---
    private Grid? _previewGrid;
    public Grid? PreviewGrid { get => _previewGrid; private set => SetProperty(ref _previewGrid, value); }
    // --------------------------------------------

    public bool IsEnabled { get => _isEnabled; set => SetProperty(ref _isEnabled, value); }
    public string Name { get => _name; set => SetProperty(ref _name, value); }

    // Keep dimensions for reference if needed, but not primary layout drivers now
    private int _previewWidth = 0;
    public int PreviewWidth { get => _previewWidth; private set => SetProperty(ref _previewWidth, value); }
    private int _previewHeight = 0;
    public int PreviewHeight { get => _previewHeight; private set => SetProperty(ref _previewHeight, value); }

    private const double PreviewCellSize = 10.0; // Size of each cell Border

    public ShapeViewModel(string name, bool[,] baseShape)
    {
        Name = name;
        GenerateRotations(baseShape);
        UpdatePreview(); // Initial grid generation
    }

    // GenerateRotations, RotateMatrix, GetMatrixSignature remain the same...
    private void GenerateRotations(bool[,] baseShape)
    {
        _rotations.Clear();
        if (baseShape == null || baseShape.Length == 0) return;
        HashSet<string> uniqueSignatures = new HashSet<string>();
        bool[,] current = baseShape;
        for (int i = 0; i < 4; i++)
        {
            string signature = GetMatrixSignature(current);
            if (uniqueSignatures.Add(signature)) _rotations.Add((bool[,])current.Clone());
            current = RotateMatrix(current);
        }
        Debug.WriteLine($"Generated {_rotations.Count} unique rotations for {Name}.");
        // Force CanExecute update after rotations are known
        RotateCommand.NotifyCanExecuteChanged();
    }
    private string GetMatrixSignature(bool[,] matrix)
    { /* ... as before ... */
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.Append($"{matrix.GetLength(0)}x{matrix.GetLength(1)}:");
        for (int i = 0; i < matrix.GetLength(0); i++)
        {
            for (int j = 0; j < matrix.GetLength(1); j++) sb.Append(matrix[i, j] ? '1' : '0');
            sb.Append('|');
        }
        return sb.ToString();
    }
    private bool[,] RotateMatrix(bool[,] matrix)
    { /* ... as before ... */
        int rows = matrix.GetLength(0); int cols = matrix.GetLength(1);
        bool[,] rotated = new bool[cols, rows];
        for (int i = 0; i < rows; i++) for (int j = 0; j < cols; j++) rotated[j, rows - 1 - i] = matrix[i, j];
        return rotated;
    }


    private void UpdatePreview()
    {
        if (_rotations.Count == 0)
        {
            PreviewGrid = null; // Clear grid if no rotations
            return;
        }

        _currentRotationIndex = _currentRotationIndex % _rotations.Count;
        bool[,] currentShape = _rotations[_currentRotationIndex];

        // Update dimension properties (might be useful elsewhere)
        PreviewHeight = currentShape.GetLength(0);
        PreviewWidth = currentShape.GetLength(1);
        OnPropertyChanged(nameof(PreviewWidth));
        OnPropertyChanged(nameof(PreviewHeight));

        // --- Create the Grid UI Element Programmatically ---
        var newGrid = new Grid
        {
            // Set overall MinWidth/MinHeight to prevent grid collapsing if empty,
            // but actual size will be determined by row/col definitions.
            // Optional: Add small padding or margin if needed
            // Background = new SolidColorBrush(Colors.Transparent) // For debugging layout
        };

        // Add Row Definitions
        for (int r = 0; r < PreviewHeight; r++)
        {
            // Use fixed size for preview cells
            newGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(PreviewCellSize) });
        }
        // Add Column Definitions
        for (int c = 0; c < PreviewWidth; c++)
        {
            newGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(PreviewCellSize) });
        }

        // Add Border elements for EACH cell in the bounding box
        for (int r = 0; r < PreviewHeight; r++)
        {
            for (int c = 0; c < PreviewWidth; c++)
            {
                // Determine background color based on shape data
                Brush backgroundBrush;
                if (currentShape[r, c]) // If part of the shape
                {
                    backgroundBrush = new SolidColorBrush(Colors.DarkCyan); // Shape color
                }
                else // Empty cell within the shape's bounding box
                {
                    // Transparent makes it blend with the background
                    backgroundBrush = new SolidColorBrush(Colors.Transparent);
                }

                var border = new Border
                {
                    Background = backgroundBrush,
                    // Add a subtle border to all cells for visual structure
                    BorderBrush = new SolidColorBrush(Colors.DimGray),
                    BorderThickness = new Thickness(0.5)
                    // Width/Height set by Grid Row/Column Definition
                };

                // Set the row and column for the Border within the Grid
                Grid.SetRow(border, r);
                Grid.SetColumn(border, c);

                // Add the configured Border to the Grid's children
                newGrid.Children.Add(border);
            }
        }

        // Update the public property, triggering UI update
        PreviewGrid = newGrid;
        // ------------------------------------------------

        Debug.WriteLine($"Updated preview grid for {Name} to rotation {_currentRotationIndex} ({PreviewWidth}x{PreviewHeight})");
    }


    [RelayCommand(CanExecute = nameof(CanRotate))]
    private void Rotate()
    {
        if (!CanRotate()) return; // Guard clause
        _currentRotationIndex++;
        UpdatePreview();
    }

    // CanExecute depends only on the number of rotations
    private bool CanRotate() => _rotations.Count > 1;

    // GetCurrentRotationGrid, GetAllRotationGrids remain the same...
    public bool[,] GetCurrentRotationGrid()
    { /* ... as before ... */
        if (_rotations.Count == 0) return new bool[0, 0];
        _currentRotationIndex = _currentRotationIndex % _rotations.Count;
        return _rotations[_currentRotationIndex];
    }
    public List<bool[,]> GetAllRotationGrids() => _rotations;
}