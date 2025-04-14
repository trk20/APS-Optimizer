using APS_Optimizer_V3.Helpers;
using APS_Optimizer_V3.ViewModels; // For ImmutableList if needed

namespace APS_Optimizer_V3.Services;



// Manages unique variable IDs for the SAT solver
public class VariableManager
{
    private int _nextVariable = 1;
    public int GetNextVariable() => _nextVariable++;
    // Renamed for clarity - returns the highest ID assigned so far.
    public int GetMaxVariableId() => _nextVariable - 1;
}

// Parameters for the solver
public record SolveParameters(
    int GridWidth,
    int GridHeight,
    ImmutableList<(int r, int c)> BlockedCells, // Absolute coordinates of blocked cells
    ImmutableList<ShapeInfo> EnabledShapes,
    SelectedSymmetryType SelectedSymmetry,
    bool UseSoftSymmetry
);

// Result from the solver
public record SolverResult(
    bool Success,
    string Message, // Error or success message
    int RequiredCells, // The number of cells required for the successful solution
    ImmutableList<Placement>? SolutionPlacements, // The list of placements in the solution
    ImmutableList<SolverIterationLog>? IterationLogs
);

public enum SymmetryType
{
    None, // Added for completeness
    ReflectHorizontal, // Reflection across horizontal center line
    ReflectVertical,   // Reflection across vertical center line
    Rotate90,          // Clockwise 90-degree rotation around center
    Rotate180          // 180-degree rotation around center
    // Potentially add Rotate270 if needed, though often covered by other symmetries/rotations
}

public record SolverIterationLog(
    int IterationNumber,
    int RequiredCells,
    int Variables,
    int Clauses,
    TimeSpan Duration,
    bool IsSatisfiable
);

public enum CellType
{
    Empty = 0,     // Represents an empty space in the shape definition grid
    Generic = 1,   // Standard filled cell
    Loader = 2,    // Cell with a hollow circle
    ClipN = 10,    // Clip attached to North side
    ClipE = 11,    // Clip attached to East side
    ClipS = 12,    // Clip attached to South side
    ClipW = 13,    // Clip attached to West side
    Cooler = 20    // Cell with circle-in-circle
}