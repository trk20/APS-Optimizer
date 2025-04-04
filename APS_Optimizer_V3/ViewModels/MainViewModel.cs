// ViewModels/MainViewModel.cs
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using APS_Optimizer_V3.Helpers;
using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
// using Windows.UI; // Check if this is needed or if Microsoft.UI.Colors is sufficient
using Microsoft.UI.Xaml.Controls;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using APS_Optimizer_V3.Services; // Needed for DispatcherTimer and XamlRoot
using CommunityToolkit.Mvvm.ComponentModel;
namespace APS_Optimizer_V3.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    // --- Services ---
    private readonly SolverService _solverService;

    // --- UI State & Timing ---
    private bool _isSolving = false;
    public bool IsSolving
    {
        get => _isSolving;
        set => SetProperty(ref _isSolving, value); // Use SetProperty from base
    }

    private string _currentSolveTime = "00:00.000";
    public string CurrentSolveTime
    {
        get => _currentSolveTime;
        set => SetProperty(ref _currentSolveTime, value);
    }

    private string _resultTitle = "Result";
    public string ResultTitle
    {
        get => _resultTitle;
        set => SetProperty(ref _resultTitle, value);
    }

    private Stopwatch? _solveStopwatch;
    private DispatcherTimer? _stopwatchTimer; // Timer for UI updates of stopwatch

    // --- Shared Rotation Timer ---
    private DispatcherTimer? _sharedRotationTimer;
    private readonly TimeSpan _sharedRotateInterval = TimeSpan.FromSeconds(1.2);

    // --- Configuration Properties ---
    public List<string> TemplateOptions { get; } = new List<string> { /* Options */ "Circle (Center Hole)", "Circle (No Hole)", "None" };
    public List<string> SymmetryOptions { get; } = new List<string> { /* Options */ "None", "Rotational (90°)", "Rotational (180°)", "Horizontal", "Vertical", "Quadrants" };

    private string _selectedTemplate = "Circle (Center Hole)";
    public string SelectedTemplate
    {
        get => _selectedTemplate;
        set
        {
            // Use SetProperty and then call dependent logic
            if (SetProperty(ref _selectedTemplate, value))
            {
                OnSelectedTemplateChanged(value); // Call the logic method
            }
        }
    }

    private string _selectedSymmetry = "None";
    public string SelectedSymmetry
    {
        get => _selectedSymmetry;
        set
        {
            if (SetProperty(ref _selectedSymmetry, value))
            {
                OnSelectedSymmetryChanged(value);
            }
        }
    }

    private bool _useSoftSymmetry = true; // Default to Soft
    public bool UseSoftSymmetry
    {
        get => _useSoftSymmetry;
        set => SetProperty(ref _useSoftSymmetry, value); // Use SetProperty from base
    }

    private int _gridWidth = 21;
    // REMOVE [NotifyPropertyChangedFor] attribute
    public int GridWidth
    {
        get => _gridWidth;
        set
        {
            if (SetProperty(ref _gridWidth, value))
            {
                // *** MANUALLY notify dependent property ***
                OnPropertyChanged(nameof(CalculatedGridTotalWidth));
                // Call other logic
                OnGridWidthChanged(value);
            }
        }
    }


    private int _gridHeight = 21;
    public int GridHeight
    {
        get => _gridHeight;
        set
        {
            if (SetProperty(ref _gridHeight, value))
            {
                OnGridHeightChanged(value);
            }
        }
    }

    // --- Dependent/Calculated Properties ---
    private const double CellWidth = 15.0;
    private const double CellSpacing = 1.0;
    public const double PreviewCellSize = 10.0;
    public const double PreviewCellSpacing = 1.0;

    // Notify manually for calculated properties dependent on others
    public double CalculatedGridTotalWidth => GridWidth <= 0 ? CellWidth : (GridWidth * CellWidth) + ((Math.Max(0, GridWidth - 1)) * CellSpacing);

    public double MaxPreviewColumnWidth
    {
        get
        {
            if (!AvailableShapes.Any()) return PreviewCellSize * 4;
            int maxDimension = AvailableShapes
                .SelectMany(s => s.GetAllRotationGrids())
                .Select(g => Math.Max(g.GetLength(0), g.GetLength(1)))
                .DefaultIfEmpty(4)
                .Max();
            double width = (maxDimension * PreviewCellSize) + ((Math.Max(0, maxDimension - 1)) * PreviewCellSpacing);
            return width + 2; // Border padding
        }
    }

    // --- Collections ---
    public ObservableCollection<CellViewModel> GridEditorCells { get; } = new();
    public ObservableCollection<CellViewModel> ResultGridCells { get; } = new();
    public ObservableCollection<ShapeViewModel> AvailableShapes { get; } = new();

    // --- Constructor ---
    public MainViewModel()
    {
        _solverService = new SolverService();
        AvailableShapes.CollectionChanged += AvailableShapes_CollectionChanged;
        ApplyTemplate(SelectedTemplate); // Apply default template on startup
        InitializeResultGrid();
        InitializeShapes();
    }

    // --- Partial Methods for Property Changes (Generated by [ObservableProperty]) ---

    private void OnSelectedTemplateChanged(string value)
    {
        Debug.WriteLine($"Template selected: {value}");
        ApplyTemplate(value); // Apply the new template
        InitializeResultGrid(); // Also resize result grid placeholder
    }

    private void OnSelectedSymmetryChanged(string value)
    {
        Debug.WriteLine($"Symmetry selected: {value}");
        // Add logic here if needed when symmetry changes
    }

    private void OnGridWidthChanged(int value)
    {
        // Optional: Add validation if needed beyond NumberBox Min/Max
        // int targetValue = Math.Max(3, value);
        // if (targetValue != value) { GridWidth = targetValue; return; } // Avoid re-entry if value corrected

        Debug.WriteLine($"GridWidth changed to: {value}");
        OnPropertyChanged(nameof(CalculatedGridTotalWidth)); // Notify calculated property
        ApplyTemplate(SelectedTemplate); // Re-apply template
        InitializeResultGrid();
    }

    private void OnGridHeightChanged(int value)
    {
        // Optional: Add validation if needed
        Debug.WriteLine($"GridHeight changed to: {value}");
        // No need to notify CalculatedGridTotalWidth as it only depends on Width
        ApplyTemplate(SelectedTemplate); // Re-apply template
        InitializeResultGrid();
    }


    // --- Event Handlers ---

    private void AvailableShapes_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems)
                if (item is ShapeViewModel shapeVM) { shapeVM.IsEnabledChanged -= ShapeViewModel_IsEnabledChanged; shapeVM.Dispose(); }
        }
        if (e.NewItems != null)
        {
            foreach (var item in e.NewItems)
                if (item is ShapeViewModel shapeVM) { shapeVM.IsEnabledChanged += ShapeViewModel_IsEnabledChanged; }
        }
        OnPropertyChanged(nameof(MaxPreviewColumnWidth)); // Update calculated property
        UpdateSharedRotationTimerState();
        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset) { Debug.WriteLine("AvailableShapes collection Reset."); }
    }

    private void ShapeViewModel_IsEnabledChanged(object? sender, EventArgs e)
    {
        UpdateSharedRotationTimerState();
    }

    private void StopwatchTimer_Tick(object? sender, object e)
    {
        if (_solveStopwatch != null)
        {
            CurrentSolveTime = _solveStopwatch.Elapsed.ToString(@"mm\:ss\.fff");
        }
    }

    private void SharedRotationTimer_Tick(object? sender, object e)
    {
        foreach (var shape in AvailableShapes)
        {
            if (shape.IsEnabled && shape.CanRotate()) { shape.AdvanceRotation(); }
        }
    }

    // --- Core Logic Methods ---

    private void ApplyTemplate(string templateName)
    {
        Debug.WriteLine($"Applying template: {templateName} to {GridWidth}x{GridHeight} grid.");
        GridEditorCells.Clear();
        if (GridWidth <= 0 || GridHeight <= 0) return;
        bool[,] blockedPattern = GenerateBlockedPattern(templateName, GridWidth, GridHeight);
        for (int r = 0; r < GridHeight; r++)
        {
            for (int c = 0; c < GridWidth; c++)
            {
                bool isBlocked = (r < blockedPattern.GetLength(0) && c < blockedPattern.GetLength(1) && blockedPattern[r, c]);
                GridEditorCells.Add(new CellViewModel(r, c, HandleGridEditorClick, isBlocked ? CellState.Blocked : CellState.Empty));
            }
        }
        Debug.WriteLine($"Applied template. Grid Cells Count: {GridEditorCells.Count}");
    }

    private bool[,] GenerateBlockedPattern(string templateName, int width, int height)
    {
        bool[,] pattern = new bool[height, width]; // Initialize all false (Empty)

        switch (templateName)
        {
            case "Circle (Center Hole)":
            case "Circle (No Hole)":
                bool blockCenter = templateName == "Circle (Center Hole)";
                double centerX = (width - 1.0) / 2.0;
                double centerY = (height - 1.0) / 2.0;
                double radius = Math.Min(width, height) / 2.0;
                double radiusSq = radius * radius;

                for (int r = 0; r < height; r++)
                {
                    for (int c = 0; c < width; c++)
                    {
                        double distSq = Math.Pow((r + 0.5) - (centerY + 0.5), 2) + Math.Pow((c + 0.5) - (centerX + 0.5), 2);

                        if (distSq >= radiusSq - 0.01) // Block outside radius
                        {
                            pattern[r, c] = true;
                        }

                        // Block center cell only if requested and dimensions are odd
                        if (blockCenter && width % 2 != 0 && height % 2 != 0 && r == (int)centerY && c == (int)centerX)
                        {
                            pattern[r, c] = true;
                        }
                    }
                }
                break;

            case "None":
            default:
                // pattern is already all false (Empty)
                break;
        }
        return pattern;
    }
    // --------------------------------

    private void HandleGridEditorClick(CellViewModel cell)
    {
        cell.State = cell.State == CellState.Empty ? CellState.Blocked : CellState.Empty;
        Debug.WriteLine($"Grid Editor Cell ({cell.Row},{cell.Col}) state changed to: {cell.State}");
    }

    private void InitializeResultGrid()
    {
        ResultGridCells.Clear();
        if (GridWidth <= 0 || GridHeight <= 0) return;
        for (int r = 0; r < GridHeight; r++)
            for (int c = 0; c < GridWidth; c++)
                ResultGridCells.Add(new CellViewModel(r, c, null, CellState.Empty));
        Debug.WriteLine($"Initialized Result Grid Placeholder: {GridWidth}x{GridHeight}");
    }


    private void InitializeShapes()
    {
        // ... (Implementation remains the same) ...
        while (AvailableShapes.Any()) { /* ... remove and dispose ... */ }
        AvailableShapes.Clear();
        // Add default shapes...
        bool[,] tShapeBase = { { true, true, true }, { false, true, false } }; bool[,] crossShapeBase = { { false, true, false }, { true, true, true }, { false, true, false } };
        AvailableShapes.Add(new ShapeViewModel("3-Clip", tShapeBase)); AvailableShapes.Add(new ShapeViewModel("4-Clip", crossShapeBase) { IsEnabled = false });
        foreach (var shape in AvailableShapes)
        {
            shape.IsEnabledChanged += ShapeViewModel_IsEnabledChanged;
        }
        OnPropertyChanged(nameof(MaxPreviewColumnWidth));
        UpdateSharedRotationTimerState();
    }


    // Add/Edit Dialog methods, Solve, Dispose... remain the same
    [RelayCommand]
    public async Task ShowAddShapeDialog()
    {
        var xamlRoot = ((App)Application.Current)?.MainWindow?.Content?.XamlRoot;
        if (xamlRoot == null) { Debug.WriteLine("Cannot show AddShapeDialog: Failed to get XamlRoot."); return; }

        var editorViewModel = new ShapeEditorViewModel();
        var dialog = new ShapeEditorDialog { DataContext = editorViewModel, XamlRoot = xamlRoot };
        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            string newName = editorViewModel.ShapeName;
            bool[,] newPattern = editorViewModel.GetCurrentPattern();
            if (newPattern.Length > 0)
            {
                var newShapeViewModel = new ShapeViewModel(newName, newPattern);
                // Subscription happens via CollectionChanged handler
                AvailableShapes.Add(newShapeViewModel);
                Debug.WriteLine($"Added new shape: {newName}");
                // Timer state update also happens via CollectionChanged handler
            }
            else { Debug.WriteLine("Add shape cancelled - empty pattern."); }
        }
        else { Debug.WriteLine("Add shape cancelled."); }
    }

    public async Task ShowEditShapeDialog(ShapeViewModel shapeToEdit, XamlRoot xamlRoot)
    {
        if (shapeToEdit == null) { Debug.WriteLine("ShowEditShapeDialog called with null shapeToEdit."); return; }
        if (xamlRoot == null) { Debug.WriteLine("ShowEditShapeDialog called with null xamlRoot."); return; }

        Debug.WriteLine($"Showing edit dialog for: {shapeToEdit.Name}");
        var editorViewModel = new ShapeEditorViewModel(shapeToEdit);
        var dialog = new ShapeEditorDialog { DataContext = editorViewModel, XamlRoot = xamlRoot };
        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            string editedName = editorViewModel.ShapeName;
            bool[,] editedPattern = editorViewModel.GetCurrentPattern();
            if (editedPattern.Length > 0)
            {
                shapeToEdit.UpdateShapeData(editedName, editedPattern);
                Debug.WriteLine($"Updated shape: {editedName}");
                OnPropertyChanged(nameof(MaxPreviewColumnWidth)); // Max width might change
                UpdateSharedRotationTimerState(); // Rotation ability might have changed
            }
            else { Debug.WriteLine($"Edit shape cancelled for {editedName} - resulting pattern empty."); }
        }
        else { Debug.WriteLine($"Edit shape cancelled for {shapeToEdit.Name}."); }
    }

    [RelayCommand]
    public async Task Solve() // Updated Solve command from previous step
    {
        if (IsSolving) return;

        IsSolving = true;
        CurrentSolveTime = "00:00.000";
        ResultTitle = "Result: (Solving...)";
        Debug.WriteLine("Solve command initiated.");

        // Clear previous results visually
        foreach (var cell in ResultGridCells)
        {
            var editorCell = GridEditorCells.FirstOrDefault(ec => ec.Row == cell.Row && ec.Col == cell.Col);
            cell.State = editorCell?.State == CellState.Blocked ? CellState.Blocked : CellState.Empty;
            cell.DisplayNumber = null; cell.UpdateColor();
        }

        _solveStopwatch = Stopwatch.StartNew();
        _stopwatchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _stopwatchTimer.Tick += StopwatchTimer_Tick;
        _stopwatchTimer.Start();

        SolverResult result = new SolverResult(false, "Initialization failed", 0, null);
        try
        {
            var blockedCells = GridEditorCells.Where(c => c.State == CellState.Blocked).Select(c => (c.Row, c.Col)).ToImmutableList();
            var enabledShapes = AvailableShapes.Where(s => s.IsEnabled).ToImmutableList();
            if (!enabledShapes.Any()) { result = new SolverResult(false, "No shapes enabled.", 0, null); return; }
            var parameters = new SolveParameters(GridWidth, GridHeight, blockedCells, enabledShapes, SelectedSymmetry, UseSoftSymmetry);

            Debug.WriteLine("Calling SolverService.SolveAsync...");
            result = await _solverService.SolveAsync(parameters);
            Debug.WriteLine($"Solver finished. Success: {result.Success}, Message: {result.Message}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error during solver execution: {ex}");
            result = new SolverResult(false, $"Error: {ex.Message}", 0, null);
        }
        finally
        {
            _stopwatchTimer?.Stop();
            if (_stopwatchTimer != null) _stopwatchTimer.Tick -= StopwatchTimer_Tick;
            _solveStopwatch?.Stop();
            string finalTimeStr = _solveStopwatch?.Elapsed.ToString(@"mm\:ss\.fff") ?? "N/A";

            if (result.Success && result.SolutionPlacements != null)
            {
                ResultTitle = $"Result: (Took {finalTimeStr})";
                DisplaySolution(result.SolutionPlacements);
            }
            else
            {
                ResultTitle = $"Result: (Failed - {finalTimeStr})";
                foreach (var cell in ResultGridCells.Where(c => c.State == CellState.Placed)) { cell.State = CellState.Empty; cell.UpdateColor(); }
                // TODO: Show error message to user
            }
            IsSolving = false;
            _stopwatchTimer = null;
            _solveStopwatch = null;
        }
    }

    [RelayCommand]
    public async Task Export()
    {
        await Task.CompletedTask;
        Debug.WriteLine("Export command executed (placeholder).");
    }

    private void DisplaySolution(ImmutableList<Placement> solutionPlacements)
    {
        // Assign unique numbers and colors to each placed shape instance
        var colorPalette = new List<Windows.UI.Color> {
            Colors.LightBlue, Colors.LightGreen, Colors.LightCoral, Colors.LightGoldenrodYellow,
            Colors.Plum, Colors.Orange, Colors.MediumPurple, Colors.Aquamarine, Colors.Bisque,
            Colors.Tomato, Colors.SpringGreen, Colors.Orchid, Colors.Gold, Colors.Turquoise
            // Add more colors if needed
        };
        var random = new Random();

        // Shuffle palette for variety if desired
        // colorPalette = colorPalette.OrderBy(c => random.Next()).ToList();

        int placementNumber = 1;
        var shapeInstanceColors = new Dictionary<int, Brush>(); // Map PlacementId to Color Brush

        // First pass: Assign colors/numbers
        foreach (var placement in solutionPlacements)
        {
            if (!shapeInstanceColors.ContainsKey(placement.PlacementId))
            {
                var color = colorPalette[(placementNumber - 1) % colorPalette.Count];
                shapeInstanceColors[placement.PlacementId] = new SolidColorBrush(color);
                placementNumber++;
            }
        }
        int totalPlacements = placementNumber - 1; // Correct count


        // Second pass: Update the ResultGridCells
        placementNumber = 1; // Reset for display numbering
        var placementNumbers = new Dictionary<int, int>(); // Map PlacementId to Display Number
        foreach (var placement in solutionPlacements)
        {
            if (!placementNumbers.ContainsKey(placement.PlacementId))
            {
                placementNumbers[placement.PlacementId] = placementNumber++;
            }

            var brush = shapeInstanceColors[placement.PlacementId];
            var displayNum = placementNumbers[placement.PlacementId];

            foreach (var (r, c) in placement.CoveredCells)
            {
                // Find the corresponding cell in the ResultGridCells collection
                var cellViewModel = ResultGridCells.FirstOrDefault(cell => cell.Row == r && cell.Col == c);
                if (cellViewModel != null)
                {
                    cellViewModel.State = CellState.Placed;
                    cellViewModel.Background = brush;
                    cellViewModel.DisplayNumber = displayNum; // Assign the unique number
                                                              // No need to call UpdateColor here as we are setting Background directly
                }
                else
                {
                    Debug.WriteLine($"Warning: Could not find ResultGridCell at ({r},{c}) to display placement.");
                }
            }
        }
        Debug.WriteLine($"Displayed {totalPlacements} placements on the result grid.");
    }


    public async Task RequestRemoveShape(ShapeViewModel shapeToRemove, XamlRoot xamlRoot)
    {
        if (shapeToRemove == null || xamlRoot == null)
        {
            Debug.WriteLine($"Error: RequestRemoveShape called with null arguments.");
            return;
        }

        var confirmDialog = new ContentDialog
        {
            Title = "Confirm Removal",
            Content = $"Are you sure you want to remove the shape '{shapeToRemove.Name}'?",
            PrimaryButtonText = "Remove",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot
        };

        var result = await confirmDialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            // Removal from collection triggers CollectionChanged handler,
            // which handles unsubscription and timer update.
            AvailableShapes.Remove(shapeToRemove);
            Debug.WriteLine($"Removed shape: {shapeToRemove.Name}");
        }
        else { Debug.WriteLine($"Removal cancelled for shape: {shapeToRemove.Name}"); }
    }

    // --- Shared Timer Logic ---
    private void UpdateSharedRotationTimerState()
    {
        // Condition: Are there any shapes that are enabled AND can rotate?
        bool shouldTimerRun = AvailableShapes.Any(s => s.IsEnabled && s.CanRotate());

        if (shouldTimerRun)
        {
            // If timer should run but isn't created or running, start it.
            if (_sharedRotationTimer == null)
            {
                Debug.WriteLine("Starting shared rotation timer.");
                _sharedRotationTimer = new DispatcherTimer();
                _sharedRotationTimer.Interval = _sharedRotateInterval;
                _sharedRotationTimer.Tick += SharedRotationTimer_Tick;
                _sharedRotationTimer.Start();
            }
        }
        else
        {
            if (_sharedRotationTimer != null) // Check if it exists before trying to stop
            {
                Debug.WriteLine("Stopping shared rotation timer.");
                _sharedRotationTimer.Stop();
                _sharedRotationTimer.Tick -= SharedRotationTimer_Tick; // Unsubscribe
                _sharedRotationTimer = null; // Release the timer object
            }
        }
    }


    private bool _disposed = false;
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                Debug.WriteLine("Disposing MainViewModel");

                // Stop and clean up the shared timer
                if (_sharedRotationTimer != null)
                {
                    _sharedRotationTimer.Stop();
                    _sharedRotationTimer.Tick -= SharedRotationTimer_Tick;
                    _sharedRotationTimer = null;
                }

                // Unsubscribe from collection changed
                AvailableShapes.CollectionChanged -= AvailableShapes_CollectionChanged;

                // Unsubscribe from individual shape events and dispose them
                foreach (var shape in AvailableShapes)
                {
                    shape.IsEnabledChanged -= ShapeViewModel_IsEnabledChanged;
                    shape.Dispose();
                }
                AvailableShapes.Clear();
                GridEditorCells.Clear();
                ResultGridCells.Clear();
            }
            _disposed = true;
        }
    }
    public void Dispose() { Dispose(disposing: true); GC.SuppressFinalize(this); }

}