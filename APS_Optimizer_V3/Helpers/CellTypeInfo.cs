namespace APS_Optimizer_V3.Helpers;

public class CellTypeInfo
{
    public string Name { get; set; } = string.Empty;
    public bool IsRotatable { get; set; } = false;
    public bool CanSelfIntersect { get; set; } = false;
    public bool IsEmpty { get; set; } = false;
    public string IconPath { get; set; } = string.Empty;
    public RotationDirection CurrentRotation { get; set; } = RotationDirection.North;

    public CellTypeInfo(string name, string iconPath, bool rotatable = false, bool canSelfIntersect = false, bool isEmpty = false)
    {
        Name = name;
        IconPath = iconPath;
        IsRotatable = rotatable;
        CanSelfIntersect = canSelfIntersect;
        IsEmpty = isEmpty;
    }

    public CellTypeInfo FacingDirection(RotationDirection direction)
    {
        if (!IsRotatable) return this; // No rotation needed
        if (direction == CurrentRotation) return this; // No change in direction
        return new CellTypeInfo(Name, IconPath, IsRotatable, CanSelfIntersect)
        {
            CurrentRotation = direction
        };
    }

    public static CellTypeInfo GenericCellType => new("Generic", "Images/generic.png", false, false);
    public static CellTypeInfo EmptyCellType => new("Empty", "Images/clear.png", false, false, true);
    public static CellTypeInfo BlockedCellType => new("Blocked", "Images/blocked.png", false, false);
    public static CellTypeInfo LoaderCellType => new("Loader", "Images/loader.png", false, false);
    public static CellTypeInfo ClipCellType => new("Clip", "Images/clip.png", true, false);
    public static CellTypeInfo CoolerCellType => new("Cooler", "Images/cooler.png", false, true);



}

public enum RotationDirection
{
    North = 0,
    East = 1,
    South = 2,
    West = 3
}