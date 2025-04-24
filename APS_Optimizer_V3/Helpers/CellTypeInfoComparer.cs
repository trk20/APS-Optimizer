using System.Diagnostics.CodeAnalysis;

namespace APS_Optimizer_V3.Helpers;

public class CellTypeInfoComparer : IEqualityComparer<CellTypeInfo>
{
    public bool Equals(CellTypeInfo? x, CellTypeInfo? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;
        return x.Name == y.Name && x.CurrentRotation == y.CurrentRotation;
    }

    public int GetHashCode([DisallowNull] CellTypeInfo obj)
    {
        return HashCode.Combine(obj.Name, obj.CurrentRotation);
    }
}