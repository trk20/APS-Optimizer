namespace APS_Optimizer_V3.Services;

public interface ISolveElement
{
    /// <summary>
    /// The unique CNF variable ID assigned to this element.
    /// </summary>
    int VariableId { get; }

    /// <summary>
    /// Gets all unique grid cells covered by any placement within this element.
    /// </summary>
    /// <returns>An enumerable collection of (row, column) tuples.</returns>
    IEnumerable<(int r, int c)> GetAllCoveredCells();

    /// <summary>
    /// Gets all the underlying Placement objects represented by this element.
    /// For a single Placement element, this contains only itself.
    /// For a SymmetryGroup, this contains all placements in the group.
    /// </summary>
    IEnumerable<Placement> GetPlacements();
}

// Modify Placement record
public record Placement(
    int PlacementId,
    int ShapeId,
    string ShapeName,
    int RotationIndex,
    int Row,
    int Col,
    bool[,] Grid,
    ImmutableList<(int r, int c)> CoveredCells
) : ISolveElement // Implement the interface
{
    // VariableId will be assigned externally after grouping
    public int VariableId { get; internal set; } = -1; // Default to invalid

    public IEnumerable<(int r, int c)> GetAllCoveredCells() => CoveredCells;

    public IEnumerable<Placement> GetPlacements() => new[] { this };
}

public record SymmetryGroup : ISolveElement // Implement the interface
{
    public int VariableId { get; } // Use the assigned CNF variable ID as the GroupId
    public ImmutableList<Placement> Placements { get; }
    private readonly Lazy<ImmutableHashSet<(int r, int c)>> _allCoveredCellsLazy;

    // Constructor to initialize all fields
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
