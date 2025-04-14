// ViewModels/MainViewModel.cs
using System.Collections.ObjectModel;
using System.Diagnostics;
using APS_Optimizer_V3.Helpers;
using Microsoft.UI;
using APS_Optimizer_V3.Services; // Needed for DispatcherTimer and XamlRoot
using System.Text;
using WinRT.Interop;
using Windows.Storage.Pickers;
using Windows.UI;
namespace APS_Optimizer_V3.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    // --- Services ---
    private readonly SolverService _solverService;

    // Store the logs from the last solve operation
    private ImmutableList<SolverIterationLog>? _lastSolverLogs;

    // --- UI State & Timing ---
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SolveCommand))] // Disable Solve button when solving
    private bool _isSolving = false;

    private string _currentSolveTime = "00:00.000";
    public string CurrentSolveTime
    {
        get => _currentSolveTime;
        set => SetProperty(ref _currentSolveTime, value);
    }

    [ObservableProperty] private string _resultTitle = "Result";

    private Stopwatch? _solveStopwatch;
    private DispatcherTimer? _stopwatchTimer; // Timer for UI updates of stopwatch

    // --- Shared Rotation Timer ---
    private DispatcherTimer? _sharedRotationTimer;
    private readonly TimeSpan _sharedRotateInterval = TimeSpan.FromSeconds(1.2);

    // --- Configuration Properties ---
    public List<string> TemplateOptions { get; } = new List<string> { /* Options */ "Circle (Center Hole)", "Circle (No Hole)", "None" };
    public List<string> SymmetryOptions { get; } = new List<string> { /* Options */ "None", "Rotational (90°)", "Rotational (180°)", "Horizontal", "Vertical", "Quadrants" };
    public SelectedSymmetryType SelectedSymmetryType => SelectedSymmetry switch
    {
        "None" => SelectedSymmetryType.None,
        "Rotational (90°)" => SelectedSymmetryType.Rotational90,
        "Rotational (180°)" => SelectedSymmetryType.Rotational180,
        "Horizontal" => SelectedSymmetryType.Horizontal,
        "Vertical" => SelectedSymmetryType.Vertical,
        "Quadrants" => SelectedSymmetryType.Quadrants,
        _ => SelectedSymmetryType.None
    };

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CalculatedGridTotalWidth))] // Notify calculated property
    private int _gridWidth = 21;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CalculatedGridTotalHeight))] // Notify calculated property
    private int _gridHeight = 21;


    // --- Dependent/Calculated Properties ---
    private const double CellWidth = 15.0;
    private const double CellSpacing = 0;
    public const double PreviewCellSize = 15.0;
    public const double PreviewCellSpacing = 1.0;


    // Notify manually for calculated properties dependent on others
    public double CalculatedGridTotalWidth => GridWidth <= 0 ? CellWidth : (GridWidth * CellWidth) + ((Math.Max(0, GridWidth + 1)) * CellSpacing);
    public double CalculatedGridTotalHeight => GridHeight <= 0 ? CellWidth : (GridHeight * CellWidth) + ((Math.Max(0, GridHeight + 1)) * CellSpacing); // Assuming CellWidth is also CellHeight

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
            double width = (maxDimension * PreviewCellSize) + (Math.Max(0, maxDimension - 1) * PreviewCellSpacing);
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
        OnPropertyChanged(nameof(SelectedSymmetryType)); // Notify the symmetry type property
        // Add logic here if needed when symmetry changes
    }


    // --- Event Handlers ---

    private void AvailableShapes_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        // --- Cleanup removed items ---
        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is ShapeViewModel shapeVM)
                {
                    shapeVM.IsEnabledChanged -= ShapeViewModel_IsEnabledChanged;
                    shapeVM.Dispose(); // Dispose the ShapeViewModel
                }
            }
        }
        // --- Subscribe to new items ---
        if (e.NewItems != null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is ShapeViewModel shapeVM)
                {
                    shapeVM.IsEnabledChanged += ShapeViewModel_IsEnabledChanged;
                }
            }
        }

        OnPropertyChanged(nameof(MaxPreviewColumnWidth)); // Update calculated property for preview column width
        UpdateSharedRotationTimerState(); // Check if the timer needs to start/stop
        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
        { Debug.WriteLine("AvailableShapes collection Reset."); }
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
            if (shape.IsEnabled && shape.CanRotate())
            {
                shape.AdvanceRotation();
            }
        }
    }

    // --- Core Logic Methods ---

    private void ApplyTemplate(string templateName)
    {
        Debug.WriteLine($"Applying template: {templateName} to {GridWidth}x{GridHeight} grid.");
        GridEditorCells.Clear(); // Clear existing cells
        if (GridWidth <= 0 || GridHeight <= 0) return; // Ensure valid dimensions

        bool[,] blockedPattern = GenerateBlockedPattern(templateName, GridWidth, GridHeight);

        for (int r = 0; r < GridHeight; r++)
        {
            for (int c = 0; c < GridWidth; c++)
            {
                // Determine if the cell is blocked by the template
                bool isBlocked = r < blockedPattern.GetLength(0) && c < blockedPattern.GetLength(1) && blockedPattern[r, c];

                // Use BlockedCellType or EmptyCellType based on the pattern
                var cellType = isBlocked ? CellTypeInfo.BlockedCellType : CellTypeInfo.EmptyCellType;

                // Create CellViewModel for the editor grid, passing the click handler
                GridEditorCells.Add(new CellViewModel(r, c, HandleGridEditorClick, cellType));
            }
        }
        Debug.WriteLine($"Applied template. Grid Editor Cells Count: {GridEditorCells.Count}");
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

    // *** Update click handler for Editor Grid ***
    private void HandleGridEditorClick(CellViewModel cell)
    {
        // Toggle between Empty and Blocked
        cell.DisplayedCellType = cell.DisplayedCellType == CellTypeInfo.EmptyCellType
            ? CellTypeInfo.BlockedCellType
            : CellTypeInfo.EmptyCellType;
        // The CellViewModel's OnPropertyChanged handler for DisplayedCellType
        // will automatically call UpdateVisuals() to refresh the background.
        Debug.WriteLine($"Grid Editor Cell ({cell.Row},{cell.Col}) Type changed to: {cell.DisplayedCellType.Name}");
    }


    private void InitializeResultGrid()
    {
        ResultGridCells.Clear();
        if (GridWidth <= 0 || GridHeight <= 0) return;
        for (int r = 0; r < GridHeight; r++)
        {
            for (int c = 0; c < GridWidth; c++)
            {
                // Result grid cells start empty and are not clickable via the ViewModel action
                ResultGridCells.Add(new CellViewModel(r, c, CellTypeInfo.EmptyCellType));
            }
        }
        Debug.WriteLine($"Initialized Result Grid Placeholder: {GridWidth}x{GridHeight}");
    }


    private void InitializeShapes()
    {
        // Clear existing shapes safely
        while (AvailableShapes.Any())
        {
            AvailableShapes.RemoveAt(AvailableShapes.Count - 1); // Triggers CollectionChanged handler for cleanup
        }

        try
        {
            // Define base shapes using CellTypeInfo and FacingDirection
            CellTypeInfo[,] clip3Base = {
                { CellTypeInfo.ClipCellType.FacingDirection(RotationDirection.East), CellTypeInfo.LoaderCellType,                                          CellTypeInfo.ClipCellType.FacingDirection(RotationDirection.West) },
                { CellTypeInfo.EmptyCellType,                                        CellTypeInfo.ClipCellType.FacingDirection(RotationDirection.North),                                        CellTypeInfo.EmptyCellType   }
            };
            CellTypeInfo[,] clip4Base = {
                { CellTypeInfo.EmptyCellType,                                        CellTypeInfo.ClipCellType.FacingDirection(RotationDirection.South),                                        CellTypeInfo.EmptyCellType },
                { CellTypeInfo.ClipCellType.FacingDirection(RotationDirection.East), CellTypeInfo.LoaderCellType,                                        CellTypeInfo.ClipCellType.FacingDirection(RotationDirection.West) },
                { CellTypeInfo.EmptyCellType,                                        CellTypeInfo.ClipCellType.FacingDirection(RotationDirection.North),                                        CellTypeInfo.EmptyCellType }
            };
            CellTypeInfo[,] clip5Base = {
                { CellTypeInfo.EmptyCellType,                                        CellTypeInfo.ClipCellType.FacingDirection(RotationDirection.South),                                        CellTypeInfo.EmptyCellType },
                { CellTypeInfo.ClipCellType.FacingDirection(RotationDirection.East), CellTypeInfo.LoaderCellType,                                        CellTypeInfo.ClipCellType.FacingDirection(RotationDirection.West) },
                { CellTypeInfo.EmptyCellType,                                        CellTypeInfo.CoolerCellType,                                                                               CellTypeInfo.EmptyCellType }
            };


            // Add new ShapeViewModels (CollectionChanged handler will subscribe events)
            AvailableShapes.Add(new ShapeViewModel(new ShapeInfo("3-Clip", clip3Base) { IsRotatable = true }));
            AvailableShapes.Add(new ShapeViewModel(new ShapeInfo("4-Clip", clip4Base) { IsRotatable = false }) { IsEnabled = false }); // Enable for testing
            AvailableShapes.Add(new ShapeViewModel(new ShapeInfo("Cooler", clip5Base) { IsRotatable = true }) { IsEnabled = false });

            // No need to manually subscribe here, CollectionChanged does it.
            // OnPropertyChanged(nameof(MaxPreviewColumnWidth)); // Triggered by CollectionChanged
            // UpdateSharedRotationTimerState(); // Triggered by CollectionChanged

            Debug.WriteLine($"Initialized {AvailableShapes.Count} shapes.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error initializing shapes: {ex}");
            // Handle error appropriately (e.g., show message to user)
        }
    }




    [RelayCommand]
    public async Task ShowAddShapeDialog()
    {
        var xamlRoot = GetXamlRoot(); if (xamlRoot == null) return;

        var editorViewModel = new ShapeEditorViewModel(); // Assuming this exists and works with CellTypeInfo
        var dialog = new ShapeEditorDialog { DataContext = editorViewModel, XamlRoot = xamlRoot }; // Assuming ShapeEditorDialog exists

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            string newName = editorViewModel.ShapeName;
            CellTypeInfo[,] newPattern = editorViewModel.GetCurrentPattern(); // Assumes this returns CellTypeInfo[,]

            if (newPattern != null && newPattern.Length > 0)
            {
                var newShapeViewModel = new ShapeViewModel(new ShapeInfo(newName, newPattern));
                AvailableShapes.Add(newShapeViewModel); // Triggers CollectionChanged
                Debug.WriteLine($"Added new shape: {newName}");
            }
            else
            {
                Debug.WriteLine("Add shape cancelled or pattern empty.");
            }
        }
    }

    // *** CHANGE: Handle CellType[,] from dialog ***
    public async Task ShowEditShapeDialog(ShapeViewModel shapeToEdit, XamlRoot xamlRoot)
    {
        /*
        if (shapeToEdit == null || xamlRoot == null) return;
        var editorViewModel = new ShapeEditorViewModel(shapeToEdit); // Editor loads CellType[,]
        var dialog = new ShapeEditorDialog { DataContext = editorViewModel, XamlRoot = xamlRoot };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            string editedName = editorViewModel.ShapeName;
            // *** GET CellType[,] pattern ***
            CellTypeInfo[,] editedPattern = editorViewModel.GetCurrentPattern();
            if (editedPattern.Length > 0)
            {
                // *** UPDATE ShapeViewModel with CellType[,] ***
                shapeToEdit.UpdateShapeData(editedName, editedPattern);
                Debug.WriteLine($"Updated shape: {editedName}");
                OnPropertyChanged(nameof(MaxPreviewColumnWidth));
                UpdateSharedRotationTimerState();
            }
            else { }
        }
        else { }*/
    }

    private bool CanSolve() => !IsSolving;

    [RelayCommand(CanExecute = nameof(CanSolve))]
    public async Task Solve()
    {
        if (!CanSolve()) return; // Redundant check, but safe

        IsSolving = true;
        CurrentSolveTime = "00:00.000";
        ResultTitle = "Result: (Solving...)";
        Debug.WriteLine("Solve command initiated.");

        // --- Prepare Result Grid ---
        // Set cells to Empty or Blocked based on the Editor Grid state
        foreach (var resultCell in ResultGridCells)
        {
            // Find the corresponding editor cell
            var editorCell = GridEditorCells.FirstOrDefault(ec => ec.Row == resultCell.Row && ec.Col == resultCell.Col);

            if (editorCell != null && editorCell.DisplayedCellType == CellTypeInfo.BlockedCellType)
            {
                resultCell.SetBlocked(); // Sets type and visuals
            }
            else
            {
                resultCell.SetEmpty(); // Sets type and visuals
            }
            // Clear any previous placement number/display artifacts if needed
            // resultCell.DisplayNumber = null; // If you re-introduce DisplayNumber
        }
        Debug.WriteLine("Result grid cleared and prepared based on editor state.");

        // --- Start Timing ---
        _solveStopwatch = Stopwatch.StartNew();
        _stopwatchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _stopwatchTimer.Tick += StopwatchTimer_Tick;
        _stopwatchTimer.Start();

        // --- Prepare Solver Input ---
        SolverResult result = new SolverResult(false, "Initialization failed", 0, null, null);
        try
        {
            // Get blocked cells from the *editor* grid state
            var blockedCells = GridEditorCells
                            .Where(c => c.DisplayedCellType == CellTypeInfo.BlockedCellType)
                            .Select(c => (c.Row, c.Col))
                            .ToImmutableList();

            var enabledShapes = AvailableShapes
                            .Where(s => s.IsEnabled && s.Shape != null) // Ensure shape exists
                            .Select(vm => vm.Shape) // Pass the ShapeInfo
                            .ToImmutableList();

            if (!enabledShapes.Any())
            {
                result = new SolverResult(false, "No shapes enabled or available.", 0, null, null);
                // Use finally block for cleanup
            }
            else
            {
                var parameters = new SolveParameters(GridWidth, GridHeight, blockedCells, enabledShapes, SelectedSymmetryType, UseSoftSymmetry);

                Debug.WriteLine($"Calling SolverService.SolveAsync with {enabledShapes.Count} shapes and {blockedCells.Count} blocked cells...");
                result = await _solverService.SolveAsync(parameters);
                Debug.WriteLine($"Solver finished. Success: {result.Success}, Message: {result.Message}, Placements: {result.SolutionPlacements?.Count ?? 0}");
                _lastSolverLogs = result.IterationLogs; // Store logs regardless of success
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error during solver execution: {ex}");
            result = new SolverResult(false, $"Error: {ex.Message}", 0, null, null);
            _lastSolverLogs = null; // Clear logs on exception
        }
        finally
        {
            // --- Stop Timing ---
            _stopwatchTimer?.Stop();
            if (_stopwatchTimer != null) _stopwatchTimer.Tick -= StopwatchTimer_Tick;
            _solveStopwatch?.Stop();
            string finalTimeStr = _solveStopwatch?.Elapsed.ToString(@"mm\:ss\.fff") ?? "N/A";
            CurrentSolveTime = finalTimeStr; // Display final time

            // --- Process Result ---
            if (result.Success && result.SolutionPlacements != null && result.SolutionPlacements.Any())
            {
                ResultTitle = $"Result: Success (Took {finalTimeStr})";
                DisplaySolution(result.SolutionPlacements);
            }
            else
            {
                ResultTitle = $"Result: Failed ({result.Message} - {finalTimeStr})";
                // Result grid is already in the initial empty/blocked state, so no need to clear again unless DisplaySolution was partially run.
                Debug.WriteLine($"Solver failed or returned no solution. Message: {result.Message}");
                // Optionally show a message dialog to the user here
            }

            IsSolving = false; // Re-enable Solve button
            _stopwatchTimer = null;
            _solveStopwatch = null;
            Debug.WriteLine("Solve command finished.");
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
        if (solutionPlacements == null || !solutionPlacements.Any())
        {
            Debug.WriteLine("DisplaySolution called with null or empty placements.");
            return;
        }
        Debug.WriteLine($"DisplaySolution started for {solutionPlacements.Count} placements.");

        // Palette for coloring shape instances distinctly
        var colorPalette = new List<Color> {
            Colors.LightBlue, Colors.LightGreen, Colors.LightCoral, Colors.Plum, Colors.Orange,
            Colors.MediumPurple, Colors.Aquamarine, Colors.Khaki, Colors.Tomato, Colors.SpringGreen,
            Colors.Orchid, Colors.Gold, Colors.Turquoise, Colors.LightPink, Colors.YellowGreen
        };
        var random = new Random(); // Fallback if palette runs out

        // Keep track of colors assigned to each unique placement ID from the solver
        var placementInstanceColors = new Dictionary<int, Brush>();
        int colorIndex = -1;

        // --- Apply Placements to Result Grid ---
        foreach (var placement in solutionPlacements)
        {
            if (placement == null || placement.Grid == null || placement.CoveredCells == null)
            {
                Debug.WriteLine($"Warning: Skipping invalid placement data.");
                continue;
            }
            Brush? currentBrush = null; // Reset for each placement
            // --- Assign Color ---
            if (!placementInstanceColors.TryGetValue(placement.PlacementId, out currentBrush))
            {
                Color selectedColor;
                if (colorIndex < colorPalette.Count)
                {
                    colorIndex++;
                }
                else
                {
                    colorIndex = 0;
                }
                selectedColor = colorPalette[colorIndex];
                currentBrush = new SolidColorBrush(selectedColor);
                placementInstanceColors.Add(placement.PlacementId, currentBrush);
            }
            // --- End Assign Color ---

            CellTypeInfo[,] shapeGrid = placement.Grid; // The specific rotation used for this placement
            int gridRows = shapeGrid.GetLength(0);
            int gridCols = shapeGrid.GetLength(1);

            // Apply to result grid cells covered by this placement
            foreach (var (r, c) in placement.CoveredCells)
            {
                // Find the corresponding ViewModel in the Result Grid
                var cellViewModel = ResultGridCells.FirstOrDefault(cell => cell.Row == r && cell.Col == c);
                if (cellViewModel != null)
                {
                    // Calculate the relative position within the shape's grid
                    int relativeRow = r - placement.Row;
                    int relativeCol = c - placement.Col;

                    // Get the CellTypeInfo from the placed shape's grid rotation
                    CellTypeInfo placedType = CellTypeInfo.GenericCellType; // Default fallback
                    if (relativeRow >= 0 && relativeRow < gridRows && relativeCol >= 0 && relativeCol < gridCols)
                    {
                        placedType = shapeGrid[relativeRow, relativeCol];
                    }
                    else
                    {
                        Debug.WriteLine($"Warning: Cell ({r},{c}) is covered but outside the bounds of placed shape grid [{gridRows}x{gridCols}] at ({placement.Row},{placement.Col}). Relative ({relativeRow},{relativeCol}).");
                        // Decide how to handle this - skip, use generic, etc. Using GenericCellType for now.
                    }

                    // *** IMPORTANT ORDER ***
                    // 1. Set the Type: This triggers the CellViewModel's PropertyChanged, which calls UpdateVisuals
                    //    UpdateVisuals sets the default background and potentially creates a default icon (if not empty/blocked)
                    cellViewModel.DisplayedCellType = placedType ?? CellTypeInfo.EmptyCellType; // Ensure not null

                    // 2. Set the Placement Color: This overrides the background and regenerates the icon element
                    //    on top of the new background.
                    cellViewModel.SetResultPlacement(currentBrush);

                    // Optional: Add DisplayNumber logic back if needed
                    // cellViewModel.DisplayNumber = placementDisplayNumber;
                }
                else
                {
                    Debug.WriteLine($"Warning: Could not find ResultGridCell ViewModel for ({r},{c}).");
                }
            }
        } // End foreach placement

        int totalUniquePlacements = placementInstanceColors.Count;
        Debug.WriteLine($"Displayed {totalUniquePlacements} unique placements on the result grid.");
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
            // Remove from collection. CollectionChanged handler deals with unsubscribing and timer updates.
            bool removed = AvailableShapes.Remove(shapeToRemove);
            if (removed)
            {
                Debug.WriteLine($"Removed shape: {shapeToRemove.Name}");
            }
            else
            {
                Debug.WriteLine($"Failed to remove shape: {shapeToRemove.Name} (not found in collection).");
            }
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
            if (_sharedRotationTimer == null)
            {
                Debug.WriteLine("Starting shared rotation timer.");
                _sharedRotationTimer = new DispatcherTimer
                {
                    Interval = _sharedRotateInterval
                };
                _sharedRotationTimer.Tick += SharedRotationTimer_Tick;
                _sharedRotationTimer.Start();
            }
            // else: Timer is already running, do nothing
        }
        else // Timer should not run
        {
            if (_sharedRotationTimer != null)
            {
                Debug.WriteLine("Stopping shared rotation timer.");
                _sharedRotationTimer.Stop();
                _sharedRotationTimer.Tick -= SharedRotationTimer_Tick; // Unsubscribe
                _sharedRotationTimer = null; // Release the timer object
            }
            // else: Timer is already stopped or null, do nothing
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

    private XamlRoot? GetXamlRoot()
    {
        // Try to get XamlRoot from the main window content
        return ((App)Application.Current)?.MainWindow?.Content?.XamlRoot;
    }

}

public enum SelectedSymmetryType
{
    None,
    Rotational90,
    Rotational180,
    Horizontal,
    Vertical,
    Quadrants
}