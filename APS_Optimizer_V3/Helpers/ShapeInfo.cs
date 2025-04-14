using System.Diagnostics;

namespace APS_Optimizer_V3.Helpers;

public class ShapeInfo
{
    public string Name { get; set; } = string.Empty;
    public CellTypeInfo[,] Pattern { get; set; } = new CellTypeInfo[0, 0]; // Default empty pattern
    public bool IsRotatable { get; set; } = true;
    private readonly List<CellTypeInfo[,]> _rotations = new(); // Store all rotations of the shape

    public ShapeInfo(string name, CellTypeInfo[,] pattern, bool isRotatable = true)
    {
        Name = name;
        Pattern = pattern;
        IsRotatable = isRotatable;
        GenerateRotations(pattern);
    }

    public List<CellTypeInfo[,]> GetAllRotationGrids() => _rotations;

    public int GetArea()
    {
        int area = 0;
        foreach (var cell in Pattern)
        {
            if (!cell.IsEmpty) area++;
        }
        return area;
    }

    public bool CouldSelfIntersect()
    {
        foreach (var cell in Pattern)
        {
            if (cell.CanSelfIntersect) return true;
        }
        return false;
    }

    public static CellTypeInfo[,] RotateMatrix(CellTypeInfo[,] matrix)
    {
        if (matrix == null || matrix.Length == 0) return new CellTypeInfo[0, 0];

        int rows = matrix.GetLength(0);
        int cols = matrix.GetLength(1);
        CellTypeInfo[,] rotated = new CellTypeInfo[cols, rows];

        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < cols; j++)
            {
                CellTypeInfo originalType = matrix[i, j];
                if (originalType == null) continue; // Skip null cells if any

                CellTypeInfo rotatedType;
                if (originalType.IsRotatable)
                {
                    RotationDirection nextRotation = originalType.CurrentRotation switch
                    {
                        RotationDirection.North => RotationDirection.East,
                        RotationDirection.East => RotationDirection.South,
                        RotationDirection.South => RotationDirection.West,
                        RotationDirection.West => RotationDirection.North,
                        _ => originalType.CurrentRotation // Should not happen
                    };
                    // Use FacingDirection to get a NEW instance with the updated rotation
                    rotatedType = originalType.FacingDirection(nextRotation);
                }
                else
                {
                    // If not rotatable, ensure we still place a valid object (clone if necessary, FacingDirection handles this)
                    rotatedType = originalType.FacingDirection(originalType.CurrentRotation);
                }

                rotated[j, rows - 1 - i] = rotatedType;
            }
        }
        return rotated;
    }


    private void GenerateRotations(CellTypeInfo[,] baseShape)
    {
        _rotations.Clear();
        if (baseShape == null || baseShape.Length == 0) return;


        if (!IsRotatable)
        {
            _rotations.Add((CellTypeInfo[,])baseShape.Clone());
            return;
        }

        // --- Simplified Uniqueness Check (Based on Non-Empty Cell Positions) ---
        // This doesn't distinguish shapes with same outline but different internal types.
        // A more robust signature would involve encoding the CellType values.
        HashSet<string> uniquePositionSignatures = new HashSet<string>();
        // ---

        CellTypeInfo[,] current = baseShape;
        for (int i = 0; i < 4; i++) // Generate up to 4 rotations
        {
            // --- Simplified Signature ---
            string signature = GetPositionSignature(current);
            if (uniquePositionSignatures.Add(signature))
            {
                _rotations.Add((CellTypeInfo[,])current.Clone());
            }
            // ---

            current = ShapeInfo.RotateMatrix(current);
        }
        Debug.WriteLine($"Generated {_rotations.Count} unique rotations (based on outline) for {Name}.");
    }

    private string GetPositionSignature(CellTypeInfo[,] matrix)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        int rows = matrix.GetLength(0);
        int cols = matrix.GetLength(1);
        sb.Append($"{rows}x{cols}:");
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                sb.Append($"{matrix[r, c].Name}/{matrix[r, c].CurrentRotation}");
                sb.Append('-');
            }
            sb.Append('|');
        }
        return sb.ToString();
    }
}