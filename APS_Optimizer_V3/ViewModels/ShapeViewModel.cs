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
public partial class ShapeViewModel : ViewModelBase, IDisposable
{
    private string _name = "Unnamed Shape";
    private int _currentRotationIndex = 0;
    private List<bool[,]> _rotations = new List<bool[,]>();
    private bool _isEnabled = true;
    private Grid? _previewGrid;

    // --- Timer for auto-rotation ---
    private DispatcherTimer? _autoRotateTimer;
    private readonly TimeSpan _rotateInterval = TimeSpan.FromSeconds(1.2); // Adjust interval as needed
                                                                           // --------------------------------

    public Grid? PreviewGrid { get => _previewGrid; private set => SetProperty(ref _previewGrid, value); }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetProperty(ref _isEnabled, value))
            {
                // Start/Stop timer based on whether the shape is enabled
                if (_isEnabled)
                {
                    StartAutoRotation();
                }
                else
                {
                    StopAutoRotation();
                }
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
                         // Start timer only if enabled and has rotations
        if (IsEnabled)
        {
            StartAutoRotation();
        }
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


    private void UpdatePreview() // No functional changes needed here
    {
        if (_rotations.Count == 0) { PreviewGrid = null; return; }
        _currentRotationIndex = _currentRotationIndex % _rotations.Count;
        bool[,] currentShape = _rotations[_currentRotationIndex];
        PreviewHeight = currentShape.GetLength(0);
        PreviewWidth = currentShape.GetLength(1);
        OnPropertyChanged(nameof(PreviewWidth)); OnPropertyChanged(nameof(PreviewHeight));

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
        PreviewGrid = newGrid;
        Debug.WriteLine($"Updated preview grid for {Name} to rotation {_currentRotationIndex} ({PreviewWidth}x{PreviewHeight})");
    }

    // --- Timer Management Methods ---
    private void StartAutoRotation()
    {
        // Only start if rotations > 1 and timer isn't already running
        if (_rotations.Count <= 1 || _autoRotateTimer != null)
        {
            return;
        }

        Debug.WriteLine($"Starting auto-rotation for {Name}");
        _autoRotateTimer = new DispatcherTimer();
        _autoRotateTimer.Interval = _rotateInterval;
        _autoRotateTimer.Tick += Timer_Tick;
        _autoRotateTimer.Start();
    }

    private void StopAutoRotation()
    {
        if (_autoRotateTimer != null)
        {
            Debug.WriteLine($"Stopping auto-rotation for {Name}");
            _autoRotateTimer.Stop();
            _autoRotateTimer.Tick -= Timer_Tick; // Unsubscribe event handler
            _autoRotateTimer = null;
        }
    }

    private void Timer_Tick(object? sender, object e)
    {
        // This code runs on the UI thread thanks to DispatcherTimer
        if (_rotations.Count > 1)
        {
            _currentRotationIndex++; // Increment index first
            UpdatePreview(); // Update the visual preview
        }
        else
        {
            // Should not happen if timer started correctly, but stop just in case
            StopAutoRotation();
        }
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
                StopAutoRotation(); // Ensure timer is stopped and resources released
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

    // Optional Finalizer (uncomment if you have unmanaged resources)
    // ~ShapeViewModel()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }
    // -------------------------------------------


    // GetCurrentRotationGrid, GetAllRotationGrids remain the same...
    public bool[,] GetCurrentRotationGrid()
    { /* ... as before ... */
        if (_rotations.Count == 0) return new bool[0, 0];
        _currentRotationIndex = _currentRotationIndex % _rotations.Count;
        return _rotations[_currentRotationIndex];
    }
    public List<bool[,]> GetAllRotationGrids() => _rotations;
}