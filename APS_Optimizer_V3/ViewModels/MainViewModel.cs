// ViewModels/MainViewModel.cs
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using APS_Optimizer_V3.Helpers; // Correct namespace
using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml.Media; // For SolidColorBrush
using Microsoft.UI;
using Windows.UI; // For Colors


namespace APS_Optimizer_V3.ViewModels;
public partial class MainViewModel : ViewModelBase
{
    private int _gridWidth = 23;
    private int _gridHeight = 23;
    private const double CellWidth = 15.0; // Define constants for clarity
    private const double CellSpacing = 1.0;

    public int GridWidth // Make sure setter notifies CalculatedGridTotalWidth
    {
        get => _gridWidth;
        set
        {
            if (SetProperty(ref _gridWidth, value))
            {
                OnPropertyChanged(nameof(CalculatedGridTotalWidth));
                InitializeGridEditor();
                InitializeResultGrid();
            }
        }
    }
    public int GridHeight // Make sure setter reinitializes grids
    {
        get => _gridHeight;
        set
        {
            if (SetProperty(ref _gridHeight, value))
            {
                InitializeGridEditor();
                InitializeResultGrid();
            }
        }
    }

    public ObservableCollection<CellViewModel> GridEditorCells { get; } = new();
    public ObservableCollection<CellViewModel> ResultGridCells { get; } = new();
    public ObservableCollection<ShapeViewModel> AvailableShapes { get; } = new();

    // Constructor and InitializeShapes etc. remain the same
    public MainViewModel()
    {
        // Default values
        _gridWidth = 23;
        _gridHeight = 23;

        InitializeGridEditor();
        InitializeResultGrid();
        InitializeShapes();
    }


    private void InitializeShapes()
    {
        AvailableShapes.Clear();

        bool[,] tShapeBase = new bool[,] { { true, true, true }, { false, true, false } };
        bool[,] crossShapeBase = new bool[,] { { false, true, false }, { true, true, true }, { false, true, false } };
        bool[,] lShapeBase = new bool[,] { { true, false }, { true, false }, { true, true } };
        bool[,] lineShapeBase = new bool[,] { { true, true, true, true } }; // Example Line

        AvailableShapes.Add(new ShapeViewModel("T-Shape", tShapeBase));
        AvailableShapes.Add(new ShapeViewModel("Cross", crossShapeBase));
        AvailableShapes.Add(new ShapeViewModel("L-Shape", lShapeBase));
        AvailableShapes.Add(new ShapeViewModel("Line-4", lineShapeBase));

        Debug.WriteLine($"Initialized {AvailableShapes.Count} shapes with rotations.");
    }

    // InitializeGridEditor, InitializeResultGrid, HandleGridEditorClick, AddShape remain the same

    private void InitializeGridEditor()
    {
        GridEditorCells.Clear();
        if (GridWidth <= 0 || GridHeight <= 0) return;

        int center = (GridWidth - 1) / 2;
        double radius = GridWidth / 2.0;

        for (int r = 0; r < GridHeight; r++)
        {
            for (int c = 0; c < GridWidth; c++)
            {
                bool isBlocked = false;
                double distance = Math.Sqrt(Math.Pow(r - center, 2) + Math.Pow(c - center, 2));
                if (distance >= radius || (r == center && c == center)) isBlocked = true;
                var cellState = isBlocked ? CellState.Blocked : CellState.Empty;
                GridEditorCells.Add(new CellViewModel(r, c, HandleGridEditorClick, cellState));
            }
        }
        Debug.WriteLine($"Initialized Grid Editor: {GridWidth}x{GridHeight}");
    }

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
        {
            for (int c = 0; c < GridWidth; c++)
            {
                ResultGridCells.Add(new CellViewModel(r, c, null, CellState.Empty)); // Null action for result grid
            }
        }
        Debug.WriteLine($"Initialized Result Grid Placeholder: {GridWidth}x{GridHeight}");
    }


    [RelayCommand]
    private void AddShape()
    {
        bool[,] squareShape = new bool[,] { { true, true }, { true, true } };
        var newShape = new ShapeViewModel($"New Shape {AvailableShapes.Count + 1}", squareShape);
        AvailableShapes.Add(newShape);
        Debug.WriteLine($"Add Shape button clicked. Added: {newShape.Name}");
    }


    [RelayCommand]
    private async Task Solve() // Async recommended
    {
        Debug.WriteLine("Solve button clicked.");

        // --- Gather Inputs ---
        List<(int r, int c)> blockedCells = GridEditorCells
            .Where(cell => cell.State == CellState.Blocked)
            .Select(cell => (cell.Row, cell.Col))
            .ToList();

        // --- Filter shapes based on IsEnabled ---
        List<ShapeViewModel> shapesToUse = AvailableShapes.Where(s => s.IsEnabled).ToList();
        // ----------------------------------------

        if (!shapesToUse.Any())
        {
            Debug.WriteLine("No shapes enabled/available to solve.");
            // TODO: Show message to user via a dialog or status bar
            return;
        }

        Debug.WriteLine($"Solving with {shapesToUse.Count} enabled shapes:");
        foreach (var shape in shapesToUse) { Debug.WriteLine($"- {shape.Name}"); }


        var allPossiblePlacements = new Dictionary<int, (string Name, int RotationIndex, bool[,] Grid)>();
        int placementIdCounter = 0;
        foreach (var shapeVM in shapesToUse) // Iterate over ENABLED shapes
        {
            var rotations = shapeVM.GetAllRotationGrids(); // Get only unique rotations
            for (int rotIdx = 0; rotIdx < rotations.Count; rotIdx++)
            {
                // Placeholder: Log shape details
                var grid = rotations[rotIdx];
                Debug.WriteLine($"Considering {shapeVM.Name}, Rotation {rotIdx} ({grid.GetLength(0)}x{grid.GetLength(1)})");
                // TODO: Implement actual placement generation logic here
                // This involves iterating grid positions (x,y) and checking validity against blockedCells
                allPossiblePlacements.Add(placementIdCounter++, (shapeVM.Name, rotIdx, grid));
            }
        }
        Debug.WriteLine($"Total unique rotations considered (pre-placement): {allPossiblePlacements.Count}");

        // --- Placeholder for Solver Logic & Simulation ---
        Debug.WriteLine("TODO: Implement CNF Generation, Solver Execution, Solution Parsing...");
        await System.Threading.Tasks.Task.Delay(100); // Simulate work
        Random rand = new Random();
        if (ResultGridCells.Count != GridEditorCells.Count) InitializeResultGrid();
        foreach (var cell in ResultGridCells) { cell.State = CellState.Empty; cell.DisplayNumber = null; cell.UpdateColor(); } // Reset
        int cellsToColor = Math.Min(15, ResultGridCells.Count);
        var indices = Enumerable.Range(0, ResultGridCells.Count).OrderBy(x => rand.Next()).Take(cellsToColor).ToList();
        int currentNumber = 1;
        var colorPalette = new List<Color> { Colors.LightBlue, Colors.LightGreen, Colors.LightCoral, Colors.LightGoldenrodYellow, Colors.Plum, Colors.Orange, Colors.MediumPurple, Colors.Aquamarine, Colors.Bisque };
        foreach (int index in indices)
        {
            var cell = ResultGridCells[index];
            cell.State = CellState.Placed;
            cell.DisplayNumber = currentNumber;
            cell.Background = new SolidColorBrush(colorPalette[(currentNumber - 1) % colorPalette.Count]);
            currentNumber++;
        }
        Debug.WriteLine("Placeholder: Simulated updating result grid display.");
    }
    // Calculated property for grid width
    public double CalculatedGridTotalWidth => GridWidth <= 0 ? CellWidth : (GridWidth * CellWidth) + ((Math.Max(0, GridWidth - 1)) * CellSpacing);
}