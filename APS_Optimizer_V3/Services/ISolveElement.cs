using APS_Optimizer_V3.Helpers;

namespace APS_Optimizer_V3.Services;

public interface ISolveElement
{
    // Unique CNF variable ID assigned to this element
    int VariableId { get; }

    // Gets all unique grid cells covered by any placement within this element
    IEnumerable<(int r, int c)> GetAllCoveredCells();

    /// Gets all underlying Placement objects represented by this element
    IEnumerable<Placement> GetPlacements();
}

public record Placement(
    int PlacementId,
    int ShapeId,
    string ShapeName,
    int RotationIndex,
    int Row,
    int Col,
    CellTypeInfo[,] Grid,
    ImmutableList<(int r, int c)> CoveredCells
) : ISolveElement
{
    // VariableId assigned externally after grouping
    public int VariableId { get; internal set; } = -1;

    public IEnumerable<(int r, int c)> GetAllCoveredCells() => CoveredCells;

    public IEnumerable<Placement> GetPlacements() => new[] { this };
}

public record SymmetryGroup : ISolveElement
{
    public int VariableId { get; } // Use assigned CNF variable ID as GroupId
    public ImmutableList<Placement> Placements { get; }
    private readonly Lazy<ImmutableHashSet<(int r, int c)>> _allCoveredCellsLazy;

    public SymmetryGroup(int variableId, ImmutableList<Placement> placements)
    {
        VariableId = variableId;
        Placements = placements;
        // Initialize lazy calculation for all covered cells
        _allCoveredCellsLazy = new Lazy<ImmutableHashSet<(int r, int c)>>(() =>
            Placements.SelectMany(p => p.CoveredCells).ToImmutableHashSet());
    }

    public IEnumerable<(int r, int c)> GetAllCoveredCells() => _allCoveredCellsLazy.Value;

    public IEnumerable<Placement> GetPlacements() => Placements;
}
