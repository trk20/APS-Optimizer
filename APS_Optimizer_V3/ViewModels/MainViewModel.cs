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
using Windows.UI; // Check if this is needed or if Microsoft.UI.Colors is sufficient
using Microsoft.UI.Xaml.Controls;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace APS_Optimizer_V3.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
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
    { /* ... as before ... */
        get
        {
            if (!AvailableShapes.Any()) return PreviewCellSize * 4;
            int maxDimension = AvailableShapes.SelectMany(s => s.GetAllRotationGrids()).Select(g => Math.Max(g.GetLength(0), g.GetLength(1))).DefaultIfEmpty(4).Max();
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
        InitializeShapes();
    }

    private void AvailableShapes_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    { /* ... as before ... */
        if (e.OldItems != null) foreach (var item in e.OldItems) if (item is IDisposable disposable) disposable.Dispose();
        OnPropertyChanged(nameof(MaxPreviewColumnWidth));
        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset) Debug.WriteLine("AvailableShapes collection Reset.");
    }

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
                // Calculate geometric center (can be between cells for even sizes)
                double centerX = (width - 1.0) / 2.0;
                double centerY = (height - 1.0) / 2.0;
                // Radius based on the smaller dimension to fit the circle
                double radius = Math.Min(width, height) / 2.0;

                for (int r = 0; r < height; r++)
                {
                    for (int c = 0; c < width; c++)
                    {
                        // Calculate distance from cell's center (r+0.5, c+0.5) to grid's geometric center
                        double distSq = Math.Pow((r + 0.5) - (centerY + 0.5), 2) + Math.Pow((c + 0.5) - (centerX + 0.5), 2);
                        double radiusSq = radius * radius;

                        // Block if outside the radius (or very close to edge)
                        if (distSq >= radiusSq - 0.01) // Use squared distance, add tolerance
                        {
                            pattern[r, c] = true;
                        }

                        // Handle center blocking for odd sizes specifically
                        if (blockCenter && width % 2 != 0 && height % 2 != 0 && r == (int)centerY && c == (int)centerX)
                        {
                            pattern[r, c] = true;
                        }
                        // For even sizes, blocking the geometric "center" is tricky.
                        // Option: Block the 2x2 cells around the geometric center?
                        // For now, only blocking the single center cell for odd dimensions.
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
    { /* ... as before ... */
        AvailableShapes.Clear();
        bool[,] tShapeBase = { { true, true, true }, { false, true, false } }; bool[,] crossShapeBase = { { false, true, false }, { true, true, true }, { false, true, false } }; bool[,] lShapeBase = { { true, false }, { true, false }, { true, true } }; bool[,] lineShapeBase = { { true, true, true, true } };
        AvailableShapes.Add(new ShapeViewModel("T-Shape", tShapeBase)); AvailableShapes.Add(new ShapeViewModel("Cross", crossShapeBase)); AvailableShapes.Add(new ShapeViewModel("L-Shape", lShapeBase)); AvailableShapes.Add(new ShapeViewModel("Line-4", lineShapeBase));
        Debug.WriteLine($"Initialized {AvailableShapes.Count} shapes with rotations.");
        OnPropertyChanged(nameof(MaxPreviewColumnWidth));
    }

    // Add/Edit Dialog methods, Solve, Dispose... remain the same
    [RelayCommand]
    public async Task ShowAddShapeDialog()
    { /* ... as before ... */
        var xamlRoot = ((App)Application.Current)?.MainWindow?.Content?.XamlRoot; if (xamlRoot == null) { Debug.WriteLine("Cannot show AddShapeDialog: Failed to get XamlRoot."); return; }
        var editorViewModel = new ShapeEditorViewModel(); var dialog = new ShapeEditorDialog { DataContext = editorViewModel, XamlRoot = xamlRoot }; var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary) { string newName = editorViewModel.ShapeName; bool[,] newPattern = editorViewModel.GetCurrentPattern(); if (newPattern.Length > 0) { var newShapeViewModel = new ShapeViewModel(newName, newPattern); AvailableShapes.Add(newShapeViewModel); Debug.WriteLine($"Added new shape: {newName}"); } else { Debug.WriteLine("Add shape cancelled - empty pattern."); } } else { Debug.WriteLine("Add shape cancelled."); }
    }
    public async Task ShowEditShapeDialog(ShapeViewModel shapeToEdit, XamlRoot xamlRoot)
    { /* ... as before ... */
        if (shapeToEdit == null) { Debug.WriteLine("ShowEditShapeDialog called with null shapeToEdit."); return; }
        if (xamlRoot == null) { Debug.WriteLine("ShowEditShapeDialog called with null xamlRoot."); return; }
        Debug.WriteLine($"Showing edit dialog for: {shapeToEdit.Name}"); var editorViewModel = new ShapeEditorViewModel(shapeToEdit); var dialog = new ShapeEditorDialog { DataContext = editorViewModel, XamlRoot = xamlRoot }; var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary) { string editedName = editorViewModel.ShapeName; bool[,] editedPattern = editorViewModel.GetCurrentPattern(); if (editedPattern.Length > 0) { shapeToEdit.UpdateShapeData(editedName, editedPattern); Debug.WriteLine($"Updated shape: {editedName}"); OnPropertyChanged(nameof(MaxPreviewColumnWidth)); } else { Debug.WriteLine($"Edit shape cancelled for {editedName} - resulting pattern empty."); } } else { Debug.WriteLine($"Edit shape cancelled for {shapeToEdit.Name}."); }
    }
    [RelayCommand]
    public async Task Solve()
    { /* ... as before ... */
        Debug.WriteLine("Solve button clicked."); List<(int r, int c)> blockedCells = GridEditorCells.Where(cell => cell.State == CellState.Blocked).Select(cell => (cell.Row, cell.Col)).ToList(); List<ShapeViewModel> shapesToUse = AvailableShapes.Where(s => s.IsEnabled).ToList(); if (!shapesToUse.Any()) { Debug.WriteLine("No shapes enabled/available to solve."); return; }
        Debug.WriteLine($"Solving with {shapesToUse.Count} enabled shapes:"); shapesToUse.ForEach(shape => Debug.WriteLine($"- {shape.Name}"));
        var allPossiblePlacements = new Dictionary<int, (string Name, int RotationIndex, bool[,] Grid)>(); int placementIdCounter = 0; foreach (var shapeVM in shapesToUse) { var rotations = shapeVM.GetAllRotationGrids(); for (int rotIdx = 0; rotIdx < rotations.Count; rotIdx++) { var grid = rotations[rotIdx]; Debug.WriteLine($"Considering {shapeVM.Name}, Rotation {rotIdx} ({grid.GetLength(0)}x{grid.GetLength(1)})"); allPossiblePlacements.Add(placementIdCounter++, (shapeVM.Name, rotIdx, grid)); } }
        Debug.WriteLine($"Total unique rotations considered (pre-placement): {allPossiblePlacements.Count}");
        await System.Threading.Tasks.Task.Delay(100); Random rand = new Random(); if (ResultGridCells.Count != GridEditorCells.Count) InitializeResultGrid(); foreach (var cell in ResultGridCells) { cell.State = CellState.Empty; cell.DisplayNumber = null; cell.UpdateColor(); }
        int cellsToColor = Math.Min(15, ResultGridCells.Count); var indices = Enumerable.Range(0, ResultGridCells.Count).OrderBy(x => rand.Next()).Take(cellsToColor).ToList(); int currentNumber = 1; var colorPalette = new List<Windows.UI.Color> { Colors.LightBlue, Colors.LightGreen, Colors.LightCoral, Colors.LightGoldenrodYellow, Colors.Plum, Colors.Orange, Colors.MediumPurple, Colors.Aquamarine, Colors.Bisque }; foreach (int index in indices) { var cell = ResultGridCells[index]; cell.State = CellState.Placed; cell.DisplayNumber = currentNumber; cell.Background = new SolidColorBrush(colorPalette[(currentNumber - 1) % colorPalette.Count]); currentNumber++; }
        Debug.WriteLine("Placeholder: Simulated updating result grid display.");
    }

    [RelayCommand]
    public async Task Export()
    {
    }
    private bool _disposed = false; protected virtual void Dispose(bool disposing) { if (!_disposed) { if (disposing) { Debug.WriteLine("Disposing MainViewModel"); AvailableShapes.CollectionChanged -= AvailableShapes_CollectionChanged; foreach (var shape in AvailableShapes) shape.Dispose(); AvailableShapes.Clear(); } _disposed = true; } }
    public void Dispose() { Dispose(disposing: true); GC.SuppressFinalize(this); }

}