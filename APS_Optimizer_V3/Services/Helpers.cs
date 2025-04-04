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
    ImmutableList<ShapeViewModel> EnabledShapes,
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

