using Newtonsoft.Json;

namespace APS_Optimizer_V3.Services.Export;

// --- Root Configuration Object ---
public class ExportConfiguration
{
    // --- Dictionary for shared data ---
    [JsonProperty("SharedBlockData")]
    public Dictionary<string, string> SharedBlockData { get; set; } = new();

    [JsonProperty("BasicBlockDefinitions")]
    public Dictionary<string, BasicBlockDefinition> BasicBlockDefinitions { get; set; } = new();

    [JsonProperty("ShapeExportMapping")]
    public Dictionary<string, ShapeExportMapping> ShapeExportMapping { get; set; } = new();

    [JsonProperty("KnownItemMappings")]
    public Dictionary<string, string> KnownItemMappings { get; set; } = new();

    [JsonProperty("ExportDefaults")]
    public ExportDefaults ExportDefaults { get; set; } = new();
}

// --- Basic Block Definition ---
public class BasicBlockDefinition
{
    [JsonProperty("BlockId")]
    public int BlockId { get; set; }

    [JsonProperty("MaterialCost")]
    public double MaterialCost { get; set; }

    // Use either DefaultRotationCode or RotationMap
    [JsonProperty("DefaultRotationCode")]
    public int? DefaultRotationCode { get; set; } // Nullable if RotationMap is used

    [JsonProperty("RotationMap")]
    public Dictionary<LogicalOrientation, int>? RotationMap { get; set; } // Logical Orientation (string) -> BLR Code (int)

    // Use either BlockData, BlockDataMap, or UseSharedBlockData
    [JsonProperty("BlockData")]
    public string? BlockData { get; set; } // Single Base64 string or empty

    [JsonProperty("BlockDataMap")]
    public Dictionary<LogicalOrientation, string>? BlockDataMap { get; set; } // Logical Orientation (string) -> Base64 string

    [JsonProperty("UseSharedBlockData")]
    public string? UseSharedBlockData { get; set; } // Key to look up shared BlockData (e.g., "Clip_BlockData")
}

// --- Shape Export Mapping ---
public class ShapeExportMapping
{
    [JsonProperty("Mode")]
    public ExportMode Mode { get; set; } = ExportMode.ScaleBasic;

    // Used by ScaleBasic, StackBasic
    [JsonProperty("HeightRange")]
    public List<int>? HeightRange { get; set; } // [Min, Max]

    // Used by ScaleBasic
    [JsonProperty("CellTypeMap")]
    public Dictionary<string, string>? CellTypeMap { get; set; } // Solver CellType Name -> BasicBlockDefinition Prefix

    // Used by StackBasic
    [JsonProperty("BlockDefKey")]
    public string? BlockDefKey { get; set; } // Key for BasicBlockDefinitions

    // Used by StackPerCell
    [JsonProperty("Height")]
    public int? Height { get; set; } // Fixed height

    [JsonProperty("CellStackDefinitions")]
    public Dictionary<string, List<BlockStackEntry>>? CellStackDefinitions { get; set; } // Solver CellType Name -> Vertical Stack

    // Used for configurable additional layers
    [JsonProperty("AdditionalLayers")]
    public List<LayerDefinition>? AdditionalLayers { get; set; } // Optional additional layers
}

public class BlockStackEntry
{
    [JsonProperty("BlockDefKey")]
    public string BlockDefKey { get; set; } = string.Empty; // Prefix or full key for BasicBlockDefinitions

    [JsonProperty("FixedOrientation")]
    public LogicalOrientation? FixedOrientation { get; set; }

    [JsonProperty("OrientationSource")]
    public RotationSource? OrientationSource { get; set; } // FromCell if rotation depends on CellTypeInfo.CurrentRotation
}

// --- Layer Definition for Additional Layers ---
public class LayerDefinition
{
    [JsonProperty("Name")]
    public string Name { get; set; } = string.Empty; // Layer name for identification

    [JsonProperty("RelativePosition")]
    public LayerPosition RelativePosition { get; set; } = LayerPosition.Below; // Where to place this layer

    [JsonProperty("ApplyConditions")]
    public List<LayerCondition>? ApplyConditions { get; set; } // Conditions for when to apply this layer

    [JsonProperty("BlockPlacements")]
    public List<LayerBlockPlacement> BlockPlacements { get; set; } = new(); // Block placement rules for this layer
}

public class LayerCondition
{
    [JsonProperty("Type")]
    public LayerConditionType Type { get; set; } = LayerConditionType.ContainsCellType;

    [JsonProperty("CellTypeName")]
    public string? CellTypeName { get; set; } // Used with ContainsCellType condition

    [JsonProperty("ShapeNames")]
    public List<string>? ShapeNames { get; set; } // Used with ShapeName condition
}

public class LayerBlockPlacement
{
    [JsonProperty("PlacementType")]
    public LayerPlacementType PlacementType { get; set; } = LayerPlacementType.UnderCellType;

    [JsonProperty("SourceCellType")]
    public string? SourceCellType { get; set; } // Cell type to place under/extend from

    [JsonProperty("BlockDefKey")]
    public string BlockDefKey { get; set; } = string.Empty; // Block definition key

    [JsonProperty("Orientation")]
    public LogicalOrientation? Orientation { get; set; } // Fixed orientation

    [JsonProperty("OrientationSource")]
    public RotationSource? OrientationSource { get; set; } // Get orientation from source cell

    [JsonProperty("ExtensionDirection")]
    public LogicalOrientation? ExtensionDirection { get; set; } // Direction to extend (for extending blocks)

    [JsonProperty("ExtensionDistance")]
    public int? ExtensionDistance { get; set; } // How far to extend

    [JsonProperty("FillPattern")]
    public LayerFillPattern? FillPattern { get; set; } // How to fill remaining spaces

    [JsonProperty("FillBlockDefKey")]
    public string? FillBlockDefKey { get; set; } // Block to use for filling

    [JsonProperty("FillOrientation")]
    public LogicalOrientation? FillOrientation { get; set; } // Orientation for fill blocks
}


// --- Export Defaults ---
public class ExportDefaults
{
    [JsonProperty("VehicleData")]
    public string VehicleData { get; set; } = "sct0AAAAAAAA";

    [JsonProperty("DefaultBCIValue")]
    public int DefaultBCIValue { get; set; } = 0;
}

// --- Helper Enums ---
public enum LogicalOrientation
{
    North,
    East,
    South,
    West,
    Up,
    Down
}
public enum ExportMode
{
    ScaleBasic,
    StackBasic,
    StackPerCell
}
public enum RotationSource
{
    FromCell,
    Fixed
}

public enum LayerPosition
{
    Below,
    Above
}

public enum LayerConditionType
{
    ContainsCellType,
    ShapeName
}

public enum LayerPlacementType
{
    UnderCellType,
    ExtendingFrom,
    FillRemaining
}

public enum LayerFillPattern
{
    All,
    ExcludeOccupied
}