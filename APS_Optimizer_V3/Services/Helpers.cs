using System.Collections.Generic;
using System.Collections.Immutable;
using APS_Optimizer_V3.ViewModels; // For ImmutableList if needed

namespace APS_Optimizer_V3.Services;

// Represents a single valid placement of a shape rotation
public record Placement(
    int PlacementId,      // Unique ID for this specific placement instance
    int ShapeId,          // Identifier for the original ShapeViewModel (e.g., its index or a unique ID)
    string ShapeName,     // Name for debugging/results
    int RotationIndex,    // Index of the rotation used
    int Row,              // Top-left row position on the main grid
    int Col,              // Top-left column position on the main grid
    bool[,] Grid,         // The actual bool[,] grid of the placed shape rotation
    ImmutableList<(int r, int c)> CoveredCells // List of absolute grid coordinates covered by this placement
);

// Represents a group of placements equivalent under symmetry
public record SymmetryGroup(
    int GroupId,          // Unique ID for this group (will be the CNF variable)
    ImmutableList<Placement> Placements // The placements belonging to this group
);

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
    string SelectedSymmetry // Use the string from MainViewModel for now
);

// Result from the solver
public record SolverResult(
    bool Success,
    string Message, // Error or success message
    int RequiredCells, // The number of cells required for the successful solution
    ImmutableList<Placement>? SolutionPlacements // The list of placements in the solution
);