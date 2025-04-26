using APS_Optimizer_V3.Helpers;
using APS_Optimizer_V3.ViewModels;

namespace APS_Optimizer_V3.Services;



// Manages unique variable IDs for the SAT solver
public class VariableManager
{
    private int _nextVariable = 1;
    public int GetNextVariable() => _nextVariable++;
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
    int RequiredCells, // Number of cells required for the successful solution
    ImmutableList<Placement>? SolutionPlacements, // List of placements in the solution
    ImmutableList<SolverIterationLog>? IterationLogs
);

public enum SymmetryType
{
    None,
    ReflectHorizontal,
    ReflectVertical,
    Rotate90,
    Rotate180
}

public record SolverIterationLog(
    int IterationNumber,
    int RequiredCells,
    int Variables,
    int Clauses,
    TimeSpan Duration,
    bool IsSatisfiable
);

