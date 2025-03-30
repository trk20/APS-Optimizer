using System.Collections.ObjectModel;
using APS_Optimizer_V3.Helpers;
using Microsoft.UI;
using Windows.UI;

namespace APS_Optimizer_V3.ViewModels;
public partial class MainViewModel : ViewModelBase
{
    private int _gridWidth = 23;
    private int _gridHeight = 23;
    private const double CellWidth = 15.0; // Define constants for clarity
    private const double CellSpacing = 1.0;

    public int GridWidth
    {
        get => _gridWidth;
        set
        {
            // Add validation if needed (e.g., must be positive)
            if (SetProperty(ref _gridWidth, value))
            {
                OnPropertyChanged(nameof(CalculatedGridTotalWidth)); // Notify dependent property change
                                                                     // Reinitialize grids when size changes
                InitializeGridEditor();
                InitializeResultGrid();
            }
        }
    }
    public int GridHeight
    {
        get => _gridHeight;
        set
        {
            if (SetProperty(ref _gridHeight, value))
            {
                // No calculated width depends on height, but maybe needed later
                InitializeGridEditor();
                InitializeResultGrid();
            }
        }
    }

    // Calculated property for binding to ItemsRepeater Width
    public double CalculatedGridTotalWidth
    {
        // Calculate width: (Number of Columns * Item Width) + ((Number of Columns - 1) * Spacing)
        // Add small tolerance maybe? Or rely on parent container padding.
        get
        {
            if (GridWidth <= 0) return CellWidth; // Avoid negative width if GridWidth is 0 or less
                                                  // Ensure GridWidth is at least 1 for spacing calculation
            int columns = Math.Max(1, GridWidth);
            return (columns * CellWidth) + ((columns - 1) * CellSpacing);
        }
    }


    // Collections bound to the ItemsRepeater/ListView controls in XAML
    public ObservableCollection<CellViewModel> GridEditorCells { get; } = new();
    public ObservableCollection<CellViewModel> ResultGridCells { get; } = new();
    public ObservableCollection<ShapeViewModel> AvailableShapes { get; } = new();

    // Constructor and other methods remain the same...
    public MainViewModel()
    {
        InitializeGridEditor();
        InitializeResultGrid();
        InitializeShapes();
    }

    // Rest of the ViewModel code (Initialize methods, Commands etc)...
    // Ensure InitializeGridEditor and InitializeResultGrid use GridWidth and GridHeight
    // Make sure HandleGridEditorClick, AddShape, Solve methods are present
    private void InitializeGridEditor()
    {
        GridEditorCells.Clear();
        if (GridWidth <= 0 || GridHeight <= 0) return; // Safety check

        int center = (GridWidth - 1) / 2;
        double radius = GridWidth / 2.0;

        for (int r = 0; r < GridHeight; r++)
        {
            for (int c = 0; c < GridWidth; c++)
            {
                bool isBlocked = false;
                double distance = Math.Sqrt(Math.Pow(r - center, 2) + Math.Pow(c - center, 2));
                // Original logic for blocked cells
                if (distance >= radius || (r == center && c == center))
                {
                    isBlocked = true;
                }
                var cellState = isBlocked ? CellState.Blocked : CellState.Empty;
                GridEditorCells.Add(new CellViewModel(r, c, HandleGridEditorClick, cellState));
            }
        }
        Console.WriteLine($"Initialized Grid Editor: {GridWidth}x{GridHeight}");
    }

    private void HandleGridEditorClick(CellViewModel cell)
    {
        cell.State = cell.State == CellState.Empty ? CellState.Blocked : CellState.Empty;
        Console.WriteLine($"Grid Editor Cell ({cell.Row},{cell.Col}) state changed to: {cell.State}");
    }


    private void InitializeResultGrid()
    {
        ResultGridCells.Clear();
        if (GridWidth <= 0 || GridHeight <= 0) return; // Safety check

        for (int r = 0; r < GridHeight; r++)
        {
            for (int c = 0; c < GridWidth; c++)
            {
                ResultGridCells.Add(new CellViewModel(r, c, _ => { /* No action */ }, CellState.Empty));
            }
        }
        Console.WriteLine($"Initialized Result Grid Placeholder: {GridWidth}x{GridHeight}");
    }

    private void InitializeShapes()
    {
        AvailableShapes.Clear();

        // Define base shapes (using true for filled cells)
        // T-Shape (T0 from your python code)
        bool[,] tShapeBase = new bool[,] {
                { true, true, true },
                { false, true, false }
            };

        // Cross Shape
        bool[,] crossShapeBase = new bool[,] {
                { false, true, false },
                { true, true, true },
                { false, true, false }
            };

        // Add more shapes as needed...
        // L-Shape Example
        bool[,] lShapeBase = new bool[,] {
                 { true, false },
                 { true, false },
                 { true, true }
             };


        AvailableShapes.Add(new ShapeViewModel("T-Shape", tShapeBase));
        AvailableShapes.Add(new ShapeViewModel("Cross", crossShapeBase));
        AvailableShapes.Add(new ShapeViewModel("L-Shape", lShapeBase));

        Console.WriteLine($"Initialized {AvailableShapes.Count} shapes with rotations.");
    }

    [RelayCommand] // AddShape now needs to handle creating a base shape, maybe via dialog
    private void AddShape()
    {
        // Placeholder - A real implementation would need a shape editor UI
        // For now, add a simple square shape
        bool[,] squareShape = new bool[,] { { true, true }, { true, true } };
        var newShape = new ShapeViewModel($"New Shape {AvailableShapes.Count + 1}", squareShape);
        AvailableShapes.Add(newShape);
        Console.WriteLine($"Add Shape button clicked. Added: {newShape.Name}");
    }


    [RelayCommand]
    private async void Solve() // Make async
    {
        Console.WriteLine("Solve button clicked.");

        // --- Gather Inputs ---
        // 1. Grid State (Example: Get list of blocked cells)
        List<(int r, int c)> blockedCells = GridEditorCells
            .Where(cell => cell.State == CellState.Blocked)
            .Select(cell => (cell.Row, cell.Col))
            .ToList();
        Console.WriteLine($"Grid: {GridWidth}x{GridHeight}, Blocked Cells: {blockedCells.Count}");

        // 2. Selected Shapes and ALL their rotations
        // Assuming all shapes in the list are used for now. Could add checkboxes later.
        List<ShapeViewModel> shapesToUse = AvailableShapes.ToList();
        if (!shapesToUse.Any())
        {
            Console.WriteLine("No shapes selected/available to solve.");
            // Optionally show a message to the user
            return;
        }

        // Dictionary to hold shape info: Key = unique placement ID, Value = (shapeName, rotationIndex, bool[,])
        // This structure mimics roughly what your python code generates internally for placements.
        // The actual SAT solver input generation will be more complex.
        var allPossiblePlacements = new Dictionary<int, (string Name, int RotationIndex, bool[,] Grid)>();
        int placementIdCounter = 0;
        foreach (var shapeVM in shapesToUse)
        {
            var rotations = shapeVM.GetAllRotationGrids();
            for (int rotIdx = 0; rotIdx < rotations.Count; rotIdx++)
            {
                // Here, you would iterate through all possible (x, y) positions on the main grid
                // For each position, check if placing this rotation is valid (doesn't overlap blocked cells)
                // If valid, add it as a potential placement for the SAT solver.
                // This part needs the full port of your Python placement generation logic.
                // For now, just log the shape rotations:
                allPossiblePlacements.Add(placementIdCounter++, (shapeVM.Name, rotIdx, rotations[rotIdx]));
            }
        }
        Console.WriteLine($"Gathered {shapesToUse.Count} shapes. Total unique rotations considered (pre-placement): {allPossiblePlacements.Count}");


        // --- Placeholder for Solver Logic ---
        Console.WriteLine("TODO: Implement CNF Generation based on grid, blocked cells, and all valid shape placements (including rotations).");
        Console.WriteLine("TODO: Run cryptominisat5.exe asynchronously.");
        Console.WriteLine("TODO: Parse the solution (map variable assignments back to placements).");
        Console.WriteLine("TODO: Update ResultGridCells based on the actual solution.");


        // --- Placeholder Simulation (Remains the same for now) ---
        await System.Threading.Tasks.Task.Delay(100); // Simulate work
        Random rand = new Random();
        if (ResultGridCells.Count != GridEditorCells.Count) InitializeResultGrid();
        foreach (var cell in ResultGridCells) { cell.State = CellState.Empty; cell.DisplayNumber = null; cell.UpdateColor(); } // Reset
        int cellsToColor = Math.Min(15, ResultGridCells.Count);
        var indices = Enumerable.Range(0, ResultGridCells.Count).OrderBy(x => rand.Next()).Take(cellsToColor).ToList();
        int currentNumber = 1;
        var colorPalette = new List<Color> {
                 Colors.LightBlue, Colors.LightGreen, Colors.LightCoral,
                 Colors.LightGoldenrodYellow, Colors.Plum, Colors.Orange,
                 Colors.MediumPurple, Colors.Aquamarine, Colors.Bisque
             };
        foreach (int index in indices)
        {
            var cell = ResultGridCells[index];
            cell.State = CellState.Placed;
            cell.DisplayNumber = currentNumber;
            // Cycle through palette for colors
            cell.Background = new SolidColorBrush(colorPalette[(currentNumber - 1) % colorPalette.Count]);
            currentNumber++;
        }
        Console.WriteLine("Placeholder: Simulated updating result grid display.");
    }

    // Need UpdateColor() in CellViewModel as public or internal if called from here,
    // or better, make Background update automatically when State/DisplayNumber changes.
    // The provided CellViewModel already does this via UpdateColor() called from State setter.
}
