using System.Diagnostics.CodeAnalysis; // For NotNullWhen attribute

namespace APS_Optimizer_V3.Helpers; // Or appropriate namespace

public class CellTypeInfoComparer : IEqualityComparer<CellTypeInfo>
{
    public bool Equals(CellTypeInfo? x, CellTypeInfo? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;

        // Consider two CellTypeInfos equal if they have the same Name and CurrentRotation
        // Other properties like IsRotatable, CanSelfIntersect should be inherent to the Name
        // but comparing Name and Rotation is sufficient to distinguish identical contributions at a cell.
        return x.Name == y.Name && x.CurrentRotation == y.CurrentRotation;
    }

    public int GetHashCode([DisallowNull] CellTypeInfo obj)
    {
        // Generate hash code based on the properties used in Equals
        return HashCode.Combine(obj.Name, obj.CurrentRotation);
    }
}