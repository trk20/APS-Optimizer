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
using Uno.Extensions.Specialized;
using APS_Optimizer_V3.Services.Export;
using APS_Optimizer_V3.Controls;
namespace APS_Optimizer_V3.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    // --- Services ---
    private readonly SolverService _solverService;
    private readonly ExportService _exportService;

    // Store the logs from the last solve operation - unused currently
    private ImmutableList<SolverIterationLog>? _lastSolverLogs;

    private ImmutableList<Placement>? _lastSolution;

    // --- UI State & Timing ---
    private bool _isSolving = false;
    public bool IsSolving
    {
        get => _isSolving;
        private set // Often controlled internally
        {
            if (SetProperty(ref _isSolving, value))
            {
                // Manually notify dependent commands
                SolveCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private string _currentSolveTime = "00:00.000";
    public string CurrentSolveTime
    {
        get => _currentSolveTime;
        set => SetProperty(ref _currentSolveTime, value);
    }

    private string _resultTitle = "Result";
    public string ResultTitle { get => _resultTitle; private set => SetProperty(ref _resultTitle, value); }

    private string _solverProgressText = "";
    public string SolverProgressText
    {
        get => _solverProgressText; private set => SetProperty(ref _solverProgressText, value);
    }

    private string _resultDisplayText = "";
    public string ResultDisplayText { get => _resultDisplayText; private set => SetProperty(ref _resultDisplayText, value); }

    private Stopwatch? _solveStopwatch;
    private DispatcherTimer? _stopwatchTimer; // Timer for UI updates of stopwatch

    // --- Shared Rotation Timer ---
    private DispatcherTimer? _sharedRotationTimer;
    private readonly TimeSpan _sharedRotateInterval = TimeSpan.FromSeconds(1.2);

    // --- Configuration Properties ---
    public List<string> TemplateOptions { get; } = new List<string> { "Circle (Center Hole)", "Circle (No Hole)", "None" };
    public List<string> SymmetryOptions { get; } = new List<string> { "None", "Rotational (90°)", "Rotational (180°)", "Horizontal", "Vertical", "Quadrants" };
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
            // Use SetProperty then call dependent logic
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

    private int _gridWidth = 15;
    private int _gridHeight = 15;

    public int GridWidth
    {
        get => _gridWidth;
        set
        {
            if (_gridWidth == value) return; // Don't do anything if the value hasn't actually changed

            Debug.WriteLine($"GridWidth setter called with value: {value}. Current _gridWidth: {_gridWidth}");
            if (SetProperty(ref _gridWidth, value)) // SetProperty already includes a check, but this adds safety
            {
                OnPropertyChanged(nameof(CalculatedGridTotalWidth)); // Update calculated size immediately maybe? Or debounce this too? Let's keep it immediate for now.
                DebounceGridWidthUpdate(value); // Trigger the debounced update
            }
        }
    }

    public int GridHeight
    {
        get => _gridHeight;
        set
        {
            if (_gridHeight == value) return; // Don't do anything if the value hasn't actually changed

            Debug.WriteLine($"GridHeight setter called with value: {value}. Current _gridHeight: {_gridHeight}");
            if (SetProperty(ref _gridHeight, value))
            {
                OnPropertyChanged(nameof(CalculatedGridTotalHeight));
                DebounceGridHeightUpdate(value); // Trigger the debounced update
            }
        }
    }



    // --- Dependent/Calculated Properties ---
    private double _cellSize = 15.0;
    public double CellSize
    {
        get => _cellSize * UIScaleFactor;
        set
        {
            if (SetProperty(ref _cellSize, value))
            {
                OnPropertyChanged(nameof(CalculatedGridTotalWidth));
                OnPropertyChanged(nameof(CalculatedGridTotalHeight));
            }
        }
    }
    private double _cellSpacing = 1.0;
    public double CellSpacing
    {
        get => _cellSpacing * UIScaleFactor;
        set
        {
            if (SetProperty(ref _cellSpacing, value))
            {
                OnPropertyChanged(nameof(CalculatedGridTotalWidth));
                OnPropertyChanged(nameof(CalculatedGridTotalHeight));
            }
        }
    }

    private double _previewCellSize = 25.0;
    public double PreviewCellSize
    {
        get => _previewCellSize * UIScaleFactor;
        set
        {
            if (SetProperty(ref _previewCellSize, value))
            {
                OnPropertyChanged(nameof(MaxPreviewColumnWidth));
            }
        }
    }
    private double _previewCellSpacing = 0;

    public double PreviewCellSpacing
    {
        get => _previewCellSpacing * UIScaleFactor;
        set
        {
            if (SetProperty(ref _previewCellSpacing, value))
            {
                OnPropertyChanged(nameof(MaxPreviewColumnWidth));
            }
        }
    }


    public double CalculatedGridTotalWidth => GridWidth <= 0 ? CellSize : (GridWidth * CellSize) + (Math.Max(0, GridWidth - 1) * CellSpacing) + 4;
    public double CalculatedGridTotalHeight => GridHeight <= 0 ? CellSize : (GridHeight * CellSize) + (Math.Max(0, GridHeight - 1) * CellSpacing) + 2;
    private double _uiScaleFactor = 1.0;
    public double UIScaleFactor
    {
        get => _uiScaleFactor;
        set
        {
            if (SetProperty(ref _uiScaleFactor, value))
            {
                OnPropertyChanged(nameof(CellSize));
                OnPropertyChanged(nameof(CellSpacing));
                OnPropertyChanged(nameof(CalculatedGridTotalWidth));
                OnPropertyChanged(nameof(CalculatedGridTotalHeight));
            }
        }
    }

    private CancellationTokenSource? _gridWidthDebounceCts;
    private CancellationTokenSource? _gridHeightDebounceCts;
    private const int DebounceDelayMilliseconds = 150;

    public double MaxPreviewColumnWidth
    {
        get
        {

            Debug.WriteLine($"Calculating MaxPreviewColumnWidth for {AvailableShapes.Count} shapes.");
            if (!AvailableShapes.Any()) return PreviewCellSize * 4;
            int maxDimension = AvailableShapes
                .SelectMany(s => s.GetAllRotationGrids())
                .Select(g => Math.Max(g.GetLength(0), g.GetLength(1)))
                .DefaultIfEmpty(4)
                .Max();
            double width = (maxDimension * PreviewCellSize) + (Math.Max(0, maxDimension - 1) * PreviewCellSpacing);
            return width; // Border padding
        }
    }

    // --- Collections ---
    private ObservableCollection<CellViewModel> _gridEditorCells = new();
    public ObservableCollection<CellViewModel> GridEditorCells
    {
        get => _gridEditorCells;
        private set => SetProperty(ref _gridEditorCells, value); // Usually set internally
    }

    private ObservableCollection<CellViewModel> _resultGridCells = new();
    public ObservableCollection<CellViewModel> ResultGridCells
    {
        get => _resultGridCells;
        private set => SetProperty(ref _resultGridCells, value); // Usually set internally
    }
    public ObservableCollection<ShapeViewModel> AvailableShapes { get; } = new();

    // --- Constructor ---
    public MainViewModel()
    {
        _solverService = new SolverService();
        try
        {
            ExportConfiguration exportConfig = ConfigurationLoader.LoadExportConfiguration();
            _exportService = new ExportService(exportConfig);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"FATAL: Failed to initialize ExportService: {ex}");
            throw;
        }
        AvailableShapes.CollectionChanged += AvailableShapes_CollectionChanged;
        RebuildGrids(SelectedTemplate);
        InitializeShapes();
    }

    // --- Methods for Property Changes ---

    private void OnSelectedTemplateChanged(string value)
    {
        //Debug.WriteLine($"Template selected: {value}");
        RebuildGrids(value);
    }

    private void OnSelectedSymmetryChanged(string value)
    {
        //Debug.WriteLine($"Symmetry selected: {value}");
        OnPropertyChanged(nameof(SelectedSymmetryType));
    }


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

    private void RebuildGrids(string templateName)
    {
        int newWidth = GridWidth;
        int newHeight = GridHeight;

        Debug.WriteLine($"Rebuilding grids for template '{templateName}' at {newWidth}x{newHeight}.");

        if (newWidth <= 0 || newHeight <= 0)
        {
            GridEditorCells = new ObservableCollection<CellViewModel>();
            ResultGridCells = new ObservableCollection<CellViewModel>();
            return;
        }

        // 1. Prepare data for both grids in temporary lists
        var editorList = new List<CellViewModel>(newWidth * newHeight);
        var resultList = new List<CellViewModel>(newWidth * newHeight);
        bool[,] blockedPattern = GenerateBlockedPattern(templateName, newWidth, newHeight);

        for (int r = 0; r < newHeight; r++)
        {
            for (int c = 0; c < newWidth; c++)
            {
                bool isBlocked = r < blockedPattern.GetLength(0) && c < blockedPattern.GetLength(1) && blockedPattern[r, c];
                var editorCellType = isBlocked ? CellTypeInfo.BlockedCellType : CellTypeInfo.EmptyCellType;
                var resultCellType = isBlocked ? CellTypeInfo.BlockedCellType : CellTypeInfo.EmptyCellType; // Result grid starts empty or blocked

                editorList.Add(new CellViewModel(r, c, HandleGridEditorClick, editorCellType));
                resultList.Add(new CellViewModel(r, c, resultCellType)); // No click handler for result
            }
        }

        // Create new ObservableCollections from the lists and assign them
        // Triggers PropertyChanged once for each collection property
        GridEditorCells = new ObservableCollection<CellViewModel>(editorList);
        ResultGridCells = new ObservableCollection<CellViewModel>(resultList);

        Debug.WriteLine($"Rebuilt grids. Editor Cells: {GridEditorCells.Count}, Result Cells: {ResultGridCells.Count}");
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
                        double distSq = Math.Pow(r + 0.5 - (centerY + 0.5), 2) + Math.Pow(c + 0.5 - (centerX + 0.5), 2);

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

    private void HandleGridEditorClick(CellViewModel cell)
    {
        cell.DisplayedCellType = cell.DisplayedCellType.Name == CellTypeInfo.EmptyCellType.Name
            ? CellTypeInfo.BlockedCellType
            : CellTypeInfo.EmptyCellType;

        UpdateResultGridCellFromEditor(cell); // Update the corresponding result cell
        Debug.WriteLine($"Grid Editor Cell ({cell.Row},{cell.Col}) Type changed to: {cell.DisplayedCellType.Name}");
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


            AvailableShapes.Add(new ShapeViewModel(new ShapeInfo("3-Clip", clip3Base) { IsRotatable = true }));
            AvailableShapes.Add(new ShapeViewModel(new ShapeInfo("4-Clip", clip4Base) { IsRotatable = false }) { IsEnabled = false });
            AvailableShapes.Add(new ShapeViewModel(new ShapeInfo("5-Clip", clip5Base) { IsRotatable = true }) { IsEnabled = false });

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

    private async void DebounceGridWidthUpdate(int newValue)
    {
        try
        {
            _gridWidthDebounceCts?.Cancel(); // Cancel any previous pending update
            _gridWidthDebounceCts = new CancellationTokenSource();
            var token = _gridWidthDebounceCts.Token;

            await Task.Delay(DebounceDelayMilliseconds, token);
            if (token.IsCancellationRequested) return; // Check if cancelled
            // If not cancelled, proceed with the actual update logic
            Debug.WriteLine($"--> Debounced OnGridWidthChanged executing for value: {newValue}");
            RebuildGrids(SelectedTemplate);
            Debug.WriteLine($"<-- Debounced OnGridWidthChanged END");

        }
        catch (OperationCanceledException)
        {
            //Debug.WriteLine($"GridWidth update debounced/cancelled for value: {newValue}");
            // Expected when a new value comes in too quickly
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error during debounced GridWidth update: {ex}");
        }
    }


    private async void DebounceGridHeightUpdate(int newValue)
    {
        try
        {
            _gridHeightDebounceCts?.Cancel(); // Cancel any previous pending update
            _gridHeightDebounceCts = new CancellationTokenSource();
            var token = _gridHeightDebounceCts.Token;

            await Task.Delay(DebounceDelayMilliseconds, token);
            if (token.IsCancellationRequested) return; // Check if cancelled
            // If not cancelled, proceed with the actual update logic
            //Debug.WriteLine($"--> Debounced OnGridHeightChanged executing for value: {newValue}");
            RebuildGrids(SelectedTemplate);
            //Debug.WriteLine($"<-- Debounced OnGridHeightChanged END");
        }
        catch (OperationCanceledException)
        {
            //Debug.WriteLine($"GridHeight update debounced/cancelled for value: {newValue}");
            // Expected when a new value comes in too quickly
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error during debounced GridHeight update: {ex}");
        }
    }

    public void UpdateResultGridCell(CellViewModel resultCell)
    {
        var editorCell = GridEditorCells.FirstOrDefault(ec => ec.Row == resultCell.Row && ec.Col == resultCell.Col);
        if (editorCell != null && editorCell.DisplayedCellType.Name == CellTypeInfo.BlockedCellType.Name) // Compare Name
        {
            resultCell.SetBlocked();
        }
        else
        {
            resultCell.SetEmpty();
        }
    }

    public void UpdateResultGridCellFromEditor(CellViewModel editorCell)
    {
        var resultCell = ResultGridCells.FirstOrDefault(ec => ec.Row == editorCell.Row && ec.Col == editorCell.Col);
        if (resultCell == null) return;

        if (editorCell.DisplayedCellType.Name == CellTypeInfo.BlockedCellType.Name) // Compare Name
        {
            resultCell.SetBlocked();
        }
        else
        {
            resultCell.SetEmpty();
        }
    }


    private bool CanSolve() => !IsSolving;

    [RelayCommand(CanExecute = nameof(CanSolve))]
    public async Task Solve()
    {
        if (!CanSolve()) return;

        foreach (var resultCell in ResultGridCells)
        {
            if (!(resultCell.DisplayedCellType.Name == CellTypeInfo.BlockedCellType.Name)) UpdateResultGridCell(resultCell);
        }

        IsSolving = true;
        _lastSolution = null;
        CurrentSolveTime = "00:00.000";
        ResultTitle = "Result: (Solving...)";
        ResultDisplayText = "";
        Debug.WriteLine("Solve command initiated.");

        // --- Start Timing ---
        _solveStopwatch = Stopwatch.StartNew();
        _stopwatchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _stopwatchTimer.Tick += StopwatchTimer_Tick;
        _stopwatchTimer.Start();

        var progressIndicator = new Progress<string>(update => SolverProgressText = update);

        // --- Prepare Solver Input ---
        SolverResult result = new SolverResult(false, "Initialization failed", 0, null, null);
        try
        {
            // Get blocked cells from the editor grid state
            var blockedCells = GridEditorCells
                            .Where(c => c.DisplayedCellType.Name == CellTypeInfo.BlockedCellType.Name)
                            .Select(c => (c.Row, c.Col))
                            .ToImmutableList();


            var enabledShapes = AvailableShapes
                            .Where(s => s.IsEnabled && s.Shape != null) // Check shape exists
                            .Select(vm => vm.Shape) // Pass ShapeInfo
                            .ToImmutableList();

            if (!enabledShapes.Any())
            {
                result = new SolverResult(false, "No shapes enabled or available.", 0, null, null);
            }
            else
            {
                var parameters = new SolveParameters(GridWidth, GridHeight, blockedCells, enabledShapes, SelectedSymmetryType, UseSoftSymmetry);

                //Debug.WriteLine($"Calling SolverService.SolveAsync with {enabledShapes.Count} shapes and {blockedCells.Count} blocked cells...");
                result = await _solverService.SolveAsync(parameters, progressIndicator);
                //Debug.WriteLine($"Solver finished. Success: {result.Success}, Message: {result.Message}, Placements: {result.SolutionPlacements?.Count ?? 0}");
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
            CurrentSolveTime = finalTimeStr;

            // --- Process Result ---
            if (result.Success && result.SolutionPlacements != null && result.SolutionPlacements.Any())
            {
                ResultTitle = $"Result: Success (Took {finalTimeStr})";
                ResultDisplayText = "Placed " + GetPlacementCountPerNameText(result.SolutionPlacements.ToList());
                _lastSolution = result.SolutionPlacements;
                //Debug.WriteLine($"Last solution now has {_lastSolution.Count} placements");
                DisplaySolution(result.SolutionPlacements);
            }
            else
            {
                ResultTitle = $"Result: Failed ({result.Message} - {finalTimeStr})";
                Debug.WriteLine($"Solver failed or returned no solution. Message: {result.Message}");
            }
            SolverProgressText = "";

            IsSolving = false;
            _stopwatchTimer = null;
            _solveStopwatch = null;
            //Debug.WriteLine("Solve command finished.");
        }
    }

    [RelayCommand]
    private async Task ShowExportDialogAsync()
    {
        Debug.WriteLine("Showing export dialog");

        if (_lastSolution == null || !_lastSolution.Any())
        {
            Debug.WriteLine("Last solution was null or empty");
            return;
        }

        // Get text for ViewModel
        string summary = GetPlacementCountPerNameText(_lastSolution.ToList());
        // Get Min/Max height based on height rules
        (int minHeight, int maxHeight, int heightStep) = _exportService.CalculateHeightRules(_lastSolution);

        // Create Export Dialog
        var dialogViewModel = new ExportDialogViewModel(
            _lastSolution,
            _exportService,
            summary,
            minHeight,
            maxHeight,
            heightStep
        );

        var dialog = new ExportDialog
        {
            DataContext = dialogViewModel,
            XamlRoot = ((App)Application.Current).MainWindow!.Content!.XamlRoot
        };

        if (dialog.XamlRoot == null)
        {
            Debug.WriteLine("Error: Cannot show ExportDialog - XamlRoot is null.");
            return;
        }


        // Wait for dialog result
        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            // User clicked "Save"
            int finalHeight = dialogViewModel.TargetHeight; // Get selected height
            string blueprintName = $"Generated_{finalHeight}m"; // Example name

            (string jsonResult, double totalCost, int blockCount) exportData;
            try
            {
                // Generate the final JSON using the selected height
                exportData = _exportService.GenerateBlueprintJson(_lastSolution, finalHeight, blueprintName);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during final blueprint generation: {ex}");
                return;
            }

            // File Save Picker
            try
            {
                var savePicker = new FileSavePicker();
                InitializeWithWindow.Initialize(savePicker, WindowNative.GetWindowHandle(((App)Application.Current).MainWindow));

                savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                savePicker.FileTypeChoices.Add("Blueprint", new List<string>() { ".blueprint" });
                savePicker.SuggestedFileName = $"APS_{GridHeight}x{GridWidth}_{finalHeight}m_{summary}";

                StorageFile file = await savePicker.PickSaveFileAsync();
                if (file != null)
                {
                    await FileIO.WriteTextAsync(file, exportData.jsonResult);
                    Debug.WriteLine($"Blueprint saved to: {file.Path}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving blueprint file: {ex}");
            }
        }
        else
        {
            Debug.WriteLine("Export cancelled by user.");
        }
    }

    private string GetPlacementCountPerNameText(List<Placement> placements)
    {
        List<string> placementStrings = [];
        //Debug.WriteLine($"Getting result str for {placements.Count} placements and {placements.Select(p => p.ShapeName).Distinct().Count()} shapes");

        foreach (var shapeName in placements.Select(p => p.ShapeName).Distinct())
        {
            var placementCount = placements.Where(p => p.ShapeName == shapeName).Count();

            //Debug.WriteLine($"Shape {shapeName} has {placementCount} placements");
            placementStrings.Add($"{placementCount}x {shapeName}");
        }

        return string.Join(", ", placementStrings);
    }

    private void DisplaySolution(ImmutableList<Placement> solutionPlacements)
    {
        if (solutionPlacements == null || !solutionPlacements.Any())
        {
            Debug.WriteLine("DisplaySolution called with null or empty placements.");
            return;
        }
        //Debug.WriteLine($"DisplaySolution started for {solutionPlacements.Count} placements.");

        // Palette for coloring shape instances distinctly
        var colorPalette = new List<Color> {
            Colors.Red, Colors.Yellow, Colors.Orange, Colors.ForestGreen, Colors.Blue,
            Colors.Violet, Colors.Cyan, Colors.Magenta, Colors.LimeGreen, Colors.Pink,
            Colors.Gray, Colors.Brown, Colors.Chartreuse, Colors.OrangeRed, Colors.DarkMagenta,
            Colors.LightSeaGreen, Colors.MediumTurquoise, Colors.MidnightBlue, Colors.OliveDrab, Colors.PaleVioletRed
        };
        var random = new Random();

        // Keep track of colors assigned to each unique placement ID from the solver
        var placementInstanceColors = new Dictionary<int, Brush>();
        // --- Apply Placements to Result Grid ---
        foreach (var placement in solutionPlacements)
        {
            if (placement == null || placement.Grid == null || placement.CoveredCells == null) { continue; }

            Brush? currentBrush;
            if (!placementInstanceColors.TryGetValue(placement.PlacementId, out currentBrush))
            {
                // Use modulo for color cycling
                int colorIndexToUse = placementInstanceColors.Count % colorPalette.Count;
                Color selectedColor = colorPalette[colorIndexToUse];
                currentBrush = new SolidColorBrush(selectedColor);
                placementInstanceColors.Add(placement.PlacementId, currentBrush);
            }

            CellTypeInfo[,] shapeGrid = placement.Grid;
            int gridRows = shapeGrid.GetLength(0);
            int gridCols = shapeGrid.GetLength(1);


            // Apply to result grid cells covered by this placement
            foreach (var (r, c) in placement.CoveredCells)
            {
                // Find the corresponding ViewModel in the Result Grid
                var cellViewModel = ResultGridCells.FirstOrDefault(cell => cell.Row == r && cell.Col == c);
                if (cellViewModel != null)
                {
                    if (cellViewModel.DisplayedCellType.Name != CellTypeInfo.EmptyCellType.Name && cellViewModel.DisplayedCellType.Name != CellTypeInfo.BlockedCellType.Name)
                    {
                        Debug.WriteLine($"WARNING: Overwriting placement at ({r},{c})");
                    }
                    // Calculate the relative position within the shape's grid
                    int relativeRow = r - placement.Row;
                    int relativeCol = c - placement.Col;

                    // Get the CellTypeInfo from the placed shape's grid rotation
                    CellTypeInfo placedType = CellTypeInfo.GenericCellType;
                    if (relativeRow >= 0 && relativeRow < gridRows && relativeCol >= 0 && relativeCol < gridCols)
                    {
                        placedType = shapeGrid[relativeRow, relativeCol];
                    }
                    else
                    {
                        Debug.WriteLine($"Warning: Cell ({r},{c}) is covered but outside the bounds of placed shape grid [{gridRows}x{gridCols}] at ({placement.Row},{placement.Col}). Relative ({relativeRow},{relativeCol}).");
                    }

                    cellViewModel.DisplayedCellType = placedType ?? CellTypeInfo.EmptyCellType; // Ensure not null

                    cellViewModel.SetResultPlacement(currentBrush);
                }
                else
                {
                    Debug.WriteLine($"Warning: Could not find ResultGridCell ViewModel for ({r},{c}).");
                }
            }
        }

        int totalUniquePlacements = placementInstanceColors.Count;
        //Debug.WriteLine($"Displayed {totalUniquePlacements} unique placements on the result grid.");
    }

    // --- Shared Timer Logic ---
    private void UpdateSharedRotationTimerState()
    {
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
        }
        else
        {
            if (_sharedRotationTimer != null)
            {
                Debug.WriteLine("Stopping shared rotation timer.");
                _sharedRotationTimer.Stop();
                _sharedRotationTimer.Tick -= SharedRotationTimer_Tick;
                _sharedRotationTimer = null;
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

                if (_sharedRotationTimer != null)
                {
                    _sharedRotationTimer.Stop();
                    _sharedRotationTimer.Tick -= SharedRotationTimer_Tick;
                    _sharedRotationTimer = null;
                }

                AvailableShapes.CollectionChanged -= AvailableShapes_CollectionChanged;

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

public enum SelectedSymmetryType
{
    None,
    Rotational90,
    Rotational180,
    Horizontal,
    Vertical,
    Quadrants
}