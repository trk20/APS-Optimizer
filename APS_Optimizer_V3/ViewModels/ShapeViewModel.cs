// ViewModels/ShapeViewModel.cs
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using APS_Optimizer_V3.Helpers;
using Microsoft.UI; // Correct namespace
using Microsoft.UI.Xaml.Controls; // For Grid
using Microsoft.UI.Xaml; // For Thickness, GridLength etc.
using Microsoft.UI.Xaml.Media; // For SolidColorBrush
using System; // For EventArgs, IDisposable

namespace APS_Optimizer_V3.ViewModels;
using Point = System.ValueTuple<int, int>;
public partial class ShapeViewModel : ViewModelBase, IDisposable
{
    private string _name = "Unnamed Shape";
    private int _currentRotationIndex = 0;
    private List<bool[,]> _rotations = new List<bool[,]>();
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

    public ShapeViewModel(string name, bool[,] baseShape)
    {
        Name = name;
        GenerateRotations(baseShape);
        UpdatePreview(); // Initial grid generation
        // Timer is no longer managed here
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
    }
    private string GetMatrixSignature(bool[,] matrix)
    { /* ... implementation ... */
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
    { /* ... implementation ... */
        int rows = matrix.GetLength(0); int cols = matrix.GetLength(1);
        bool[,] rotated = new bool[cols, rows];
        for (int i = 0; i < rows; i++) for (int j = 0; j < cols; j++) rotated[j, rows - 1 - i] = matrix[i, j];
        return rotated;
    }


    private void UpdatePreview() // No functional changes needed here
    {
        if (_rotations.Count == 0) { PreviewGrid = null; return; }
        _currentRotationIndex = _currentRotationIndex % _rotations.Count; // Ensure index is valid
        bool[,] currentShape = _rotations[_currentRotationIndex];
        PreviewHeight = currentShape.GetLength(0);
        PreviewWidth = currentShape.GetLength(1);
        // No need to raise property changed manually if SetProperty is used, but keep if direct field access happens
        // OnPropertyChanged(nameof(PreviewWidth)); OnPropertyChanged(nameof(PreviewHeight));

        var newGrid = new Grid();
        for (int r = 0; r < PreviewHeight; r++) newGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(PreviewCellSize) });
        for (int c = 0; c < PreviewWidth; c++) newGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(PreviewCellSize) });

        for (int r = 0; r < PreviewHeight; r++)
        {
            for (int c = 0; c < PreviewWidth; c++)
            {
                Brush backgroundBrush = currentShape[r, c] ? new SolidColorBrush(Colors.DarkCyan) : new SolidColorBrush(Colors.Transparent);
                var border = new Border { Background = backgroundBrush, BorderBrush = new SolidColorBrush(Colors.DimGray), BorderThickness = new Thickness(0.5) };
                Grid.SetRow(border, r); Grid.SetColumn(border, c);
                newGrid.Children.Add(border);
            }
        }
        // Use SetProperty for PreviewGrid to ensure UI updates
        SetProperty(ref _previewGrid, newGrid, nameof(PreviewGrid));
        // Debug.WriteLine($"Updated preview grid for {Name} to rotation {_currentRotationIndex} ({PreviewWidth}x{PreviewHeight})"); // Less verbose logging
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

    public bool[,] GetBaseRotationGrid()
    {
        if (_rotations.Count > 0)
        {
            // Assuming the first generated rotation is the "base" one used for editing
            return _rotations[0];
        }
        // Fallback if no rotations generated (shouldn't happen with valid input)
        return new bool[0, 0];
    }

    // Add method to update data after editing
    public void UpdateShapeData(string newName, bool[,] newBaseShape)
    {
        Name = newName;
        // Regenerate rotations based on the new base shape
        GenerateRotations(newBaseShape);
        // Reset rotation index and update preview
        _currentRotationIndex = 0;
        UpdatePreview();
        // Timer is managed by MainViewModel, no action needed here
        // But MainViewModel needs to re-evaluate timer state after edit
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


    // GetCurrentRotationGrid, GetAllRotationGrids remain the same...
    public bool[,] GetCurrentRotationGrid()
    { /* ... implementation ... */
        if (_rotations.Count == 0) return new bool[0, 0];
        _currentRotationIndex = _currentRotationIndex % _rotations.Count;
        return _rotations[_currentRotationIndex];
    }
    public List<bool[,]> GetAllRotationGrids() => _rotations;

    // Helper to check if rotation is possible
    public bool CanRotate() => _rotations.Count > 1;
}