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
using Microsoft.UI.Xaml; // Needed for DispatcherTimer and XamlRoot

namespace APS_Optimizer_V3.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    // --- Shared Rotation Timer ---
    private DispatcherTimer? _sharedRotationTimer;
    private readonly TimeSpan _sharedRotateInterval = TimeSpan.FromSeconds(1.2); // Adjust interval as needed
    // -----------------------------

    // --- Template Selection ---
    public List<string> TemplateOptions { get; } = new List<string>
    {
        "Circle (Center Hole)", // Default
        "Circle (No Hole)",
        "None"
    };

    public List<string> SymmetryOptions { get; } = new List<string>
    {
        "None",
        "Rotational (order 4 / 90°)",
        "Rotational (order 2 / 180°)",
        "One Line Reflexive (horizontal)",
        "One Line Reflexive (vertical)",
        "Two Line Reflexive"
    };

    private string _selectedTemplate = "Circle (Center Hole)"; // Default selection
    public string SelectedTemplate
    {
        get => _selectedTemplate;
        set
        {
            if (SetProperty(ref _selectedTemplate, value))
            {
                Debug.WriteLine($"Template selected: {_selectedTemplate}");
                ApplyTemplate(_selectedTemplate); // Apply the new template
            }
        }
    }
    // -------------------------

    private string _selectedSymmetry = "None"; // Default selection
    public string SelectedSymmetry
    {
        get => _selectedSymmetry;
        set
        {
            if (SetProperty(ref _selectedSymmetry, value))
            {
                Debug.WriteLine($"Symmetry selected: {_selectedSymmetry}");
            }
        }
    }

    private int _gridWidth = 23;
    private int _gridHeight = 23;
    private const double CellWidth = 15.0;
    private const double CellSpacing = 1.0;
    public const double PreviewCellSize = 10.0;
    public const double PreviewCellSpacing = 1.0;

    public int GridWidth
    {
        get => _gridWidth;
        set
        {
            // Ensure odd for circle templates? Or adapt circle logic? Let's adapt.
            int targetValue = Math.Max(3, value); // Min size 3x3?
            if (SetProperty(ref _gridWidth, targetValue))
            {
                OnPropertyChanged(nameof(CalculatedGridTotalWidth));
                // Re-apply current template when size changes
                ApplyTemplate(SelectedTemplate);
                InitializeResultGrid(); // Also resize result grid placeholder
            }
        }
    }
    public int GridHeight
    {
        get => _gridHeight;
        set
        {
            int targetValue = Math.Max(3, value); // Min size 3x3?
            if (SetProperty(ref _gridHeight, targetValue))
            {
                // Re-apply current template when size changes
                ApplyTemplate(SelectedTemplate);
                InitializeResultGrid(); // Also resize result grid placeholder
            }
        }
    }

    public double MaxPreviewColumnWidth
    { /* ... implementation ... */
        get
        {
            if (!AvailableShapes.Any()) return PreviewCellSize * 4;
            // Consider only the base rotation for max width calculation maybe? Or keep as is?
            // Let's keep checking all rotations for now.
            int maxDimension = AvailableShapes
                .SelectMany(s => s.GetAllRotationGrids())
                .Select(g => Math.Max(g.GetLength(0), g.GetLength(1)))
                .DefaultIfEmpty(4)
                .Max();
            double width = (maxDimension * PreviewCellSize) + ((Math.Max(0, maxDimension - 1)) * PreviewCellSpacing);
            return width + 2; // Border padding
        }
    }
    public double CalculatedGridTotalWidth => GridWidth <= 0 ? CellWidth : (GridWidth * CellWidth) + ((Math.Max(0, GridWidth - 1)) * CellSpacing);

    public ObservableCollection<CellViewModel> GridEditorCells { get; } = new();
    public ObservableCollection<CellViewModel> ResultGridCells { get; } = new();
    public ObservableCollection<ShapeViewModel> AvailableShapes { get; } = new();

    public MainViewModel()
    {
        AvailableShapes.CollectionChanged += AvailableShapes_CollectionChanged;
        // InitializeEditorGrid is now replaced by ApplyTemplate in constructor
        ApplyTemplate(SelectedTemplate); // Apply default template on startup
        InitializeResultGrid();
        InitializeShapes(); // This now subscribes events and updates timer state
    }

    private void AvailableShapes_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        // Unsubscribe and dispose old items
        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is ShapeViewModel shapeVM)
                {
                    shapeVM.IsEnabledChanged -= ShapeViewModel_IsEnabledChanged; // Unsubscribe
                    shapeVM.Dispose(); // Dispose
                }
            }
        }

        // Subscribe to new items
        if (e.NewItems != null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is ShapeViewModel shapeVM)
                {
                    shapeVM.IsEnabledChanged += ShapeViewModel_IsEnabledChanged; // Subscribe
                }
            }
        }

        // Update max width and timer state regardless of action
        OnPropertyChanged(nameof(MaxPreviewColumnWidth));
        UpdateSharedRotationTimerState(); // Check if timer needs starting/stopping

        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
        {
            Debug.WriteLine("AvailableShapes collection Reset.");
            // If reset, theoretically all event subscriptions are gone, re-subscribe if needed
            // However, clearing and re-adding is more common than Reset.
            // If using Reset, ensure cleanup/re-subscription logic is robust.
        }
    }

    // --- Handler for IsEnabled changes in individual shapes ---
    private void ShapeViewModel_IsEnabledChanged(object? sender, EventArgs e)
    {
        // When a shape is enabled/disabled, the condition for running the timer might change.
        UpdateSharedRotationTimerState();
    }
    // ---------------------------------------------------------

    // --- Template Application Logic ---
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
                var cellState = isBlocked ? CellState.Blocked : CellState.Empty;
                GridEditorCells.Add(new CellViewModel(r, c, HandleGridEditorClick, cellState));
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
    { /* ... as before ... */
        cell.State = cell.State == CellState.Empty ? CellState.Blocked : CellState.Empty; Debug.WriteLine($"Grid Editor Cell ({cell.Row},{cell.Col}) state changed to: {cell.State}");
    }
    private void InitializeResultGrid()
    { /* ... as before ... */
        ResultGridCells.Clear(); if (GridWidth <= 0 || GridHeight <= 0) return;
        for (int r = 0; r < GridHeight; r++) for (int c = 0; c < GridWidth; c++) ResultGridCells.Add(new CellViewModel(r, c, null, CellState.Empty));
        Debug.WriteLine($"Initialized Result Grid Placeholder: {GridWidth}x{GridHeight}");
    }
    private void InitializeShapes()
    {
        // Clear existing shapes and subscriptions first
        while (AvailableShapes.Any())
        {
            var shape = AvailableShapes.First();
            shape.IsEnabledChanged -= ShapeViewModel_IsEnabledChanged; // Unsubscribe
            shape.Dispose();
            AvailableShapes.RemoveAt(0); // Remove without triggering collection changed for each one
        }
        AvailableShapes.Clear(); // Ensure it's clear if loop fails

        // Add default shapes
        bool[,] tShapeBase = { { true, true, true }, { false, true, false } }; bool[,] crossShapeBase = { { false, true, false }, { true, true, true }, { false, true, false } }; bool[,] lShapeBase = { { true, false }, { true, false }, { true, true } }; bool[,] lineShapeBase = { { true, true, true, true } };
        AvailableShapes.Add(new ShapeViewModel("T-Shape", tShapeBase));
        AvailableShapes.Add(new ShapeViewModel("Cross", crossShapeBase));
        AvailableShapes.Add(new ShapeViewModel("L-Shape", lShapeBase));
        AvailableShapes.Add(new ShapeViewModel("Line-4", lineShapeBase));

        // Subscribe to events for the newly added shapes
        foreach (var shape in AvailableShapes)
        {
            shape.IsEnabledChanged += ShapeViewModel_IsEnabledChanged;
        }

        Debug.WriteLine($"Initialized {AvailableShapes.Count} shapes with rotations.");
        OnPropertyChanged(nameof(MaxPreviewColumnWidth));
        UpdateSharedRotationTimerState(); // Initial timer check
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
    public async Task Solve()
    { /* ... implementation ... */
        Debug.WriteLine("Solve button clicked."); List<(int r, int c)> blockedCells = GridEditorCells.Where(cell => cell.State == CellState.Blocked).Select(cell => (cell.Row, cell.Col)).ToList(); List<ShapeViewModel> shapesToUse = AvailableShapes.Where(s => s.IsEnabled).ToList(); if (!shapesToUse.Any()) { Debug.WriteLine("No shapes enabled/available to solve."); return; }
        Debug.WriteLine($"Solving with {shapesToUse.Count} enabled shapes:"); shapesToUse.ForEach(shape => Debug.WriteLine($"- {shape.Name}"));
        var allPossiblePlacements = new Dictionary<int, (string Name, int RotationIndex, bool[,] Grid)>(); int placementIdCounter = 0; foreach (var shapeVM in shapesToUse) { var rotations = shapeVM.GetAllRotationGrids(); for (int rotIdx = 0; rotIdx < rotations.Count; rotIdx++) { var grid = rotations[rotIdx]; Debug.WriteLine($"Considering {shapeVM.Name}, Rotation {rotIdx} ({grid.GetLength(0)}x{grid.GetLength(1)})"); allPossiblePlacements.Add(placementIdCounter++, (shapeVM.Name, rotIdx, grid)); } }
        Debug.WriteLine($"Total unique rotations considered (pre-placement): {allPossiblePlacements.Count}");
        await System.Threading.Tasks.Task.Delay(100); Random rand = new Random(); if (ResultGridCells.Count != GridEditorCells.Count) InitializeResultGrid(); foreach (var cell in ResultGridCells) { cell.State = CellState.Empty; cell.DisplayNumber = null; cell.UpdateColor(); }
        int cellsToColor = Math.Min(15, ResultGridCells.Count); var indices = Enumerable.Range(0, ResultGridCells.Count).OrderBy(x => rand.Next()).Take(cellsToColor).ToList(); int currentNumber = 1; var colorPalette = new List<Windows.UI.Color> { Colors.LightBlue, Colors.LightGreen, Colors.LightCoral, Colors.LightGoldenrodYellow, Colors.Plum, Colors.Orange, Colors.MediumPurple, Colors.Aquamarine, Colors.Bisque }; foreach (int index in indices) { var cell = ResultGridCells[index]; cell.State = CellState.Placed; cell.DisplayNumber = currentNumber; cell.Background = new SolidColorBrush(colorPalette[(currentNumber - 1) % colorPalette.Count]); currentNumber++; }
        Debug.WriteLine("Placeholder: Simulated updating result grid display.");
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

    [RelayCommand]
    public async Task Export()
    {
        // Placeholder for export logic
        await Task.CompletedTask; // Example async signature
        Debug.WriteLine("Export command executed (placeholder).");
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
            // Optional: If timer exists but was stopped, restart it.
            // else if (!_sharedRotationTimer.IsEnabled)
            // {
            //     Debug.WriteLine("Restarting shared rotation timer.");
            //     _sharedRotationTimer.Start();
            // }
        }
        else
        {
            // If timer shouldn't run but is running, stop it.
            if (_sharedRotationTimer != null) // Check if it exists before trying to stop
            {
                Debug.WriteLine("Stopping shared rotation timer.");
                _sharedRotationTimer.Stop();
                _sharedRotationTimer.Tick -= SharedRotationTimer_Tick; // Unsubscribe
                _sharedRotationTimer = null; // Release the timer object
            }
        }
    }

    private void SharedRotationTimer_Tick(object? sender, object e)
    {
        // This runs on the UI thread
        // Tell all enabled shapes that can rotate to advance
        foreach (var shape in AvailableShapes)
        {
            if (shape.IsEnabled && shape.CanRotate())
            {
                shape.AdvanceRotation();
            }
        }
    }
    // -------------------------


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