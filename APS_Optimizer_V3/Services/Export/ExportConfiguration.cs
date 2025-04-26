using Newtonsoft.Json;

namespace APS_Optimizer_V3.Services.Export;

// --- Root Configuration Object ---
public class ExportConfiguration
{
    // --- ADDED: Dictionary for shared data ---
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

    // Use EITHER DefaultRotationCode OR RotationMap
    [JsonProperty("DefaultRotationCode")]
    public int? DefaultRotationCode { get; set; } // Nullable if RotationMap is used

    [JsonProperty("RotationMap")]
    public Dictionary<LogicalOrientation, int>? RotationMap { get; set; } // Logical Orientation (string) -> BLR Code (int)

    // Use EITHER BlockData OR BlockDataMap OR UseSharedBlockData
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
    public ExportMode Mode { get; set; } = ExportMode.ScaleBasic; // "ScaleBasic", "StackBasic", "StackPerCell"

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
}

public class BlockStackEntry
{
    [JsonProperty("BlockDefKey")]
    public string BlockDefKey { get; set; } = string.Empty; // Prefix or full key for BasicBlockDefinitions

    [JsonProperty("FixedOrientation")]
    public LogicalOrientation? FixedOrientation { get; set; } // e.g., "Up", "South", "West" (if rotation doesn't come from cell)

    [JsonProperty("OrientationSource")]
    public RotationSource? OrientationSource { get; set; } // "FromCell" if rotation depends on CellTypeInfo.CurrentRotation
}


// --- Export Defaults ---
public class ExportDefaults
{
    [JsonProperty("VehicleData")]
    public string VehicleData { get; set; } = "sct0AAAAAAAA";

    [JsonProperty("DefaultBCIValue")]
    public int DefaultBCIValue { get; set; } = 0;

    [JsonProperty("CoordinateSystem")]
    public CoordinateSystemMapping CoordinateSystem { get; set; } = new();

    // Add other fixed values if needed (like CSI)
}

public class CoordinateSystemMapping
{
    [JsonProperty("SolverRow")]
    public string SolverRow { get; set; } = "GameZ"; // Maps to X, Y, or Z

    [JsonProperty("SolverCol")]
    public string SolverCol { get; set; } = "GameX"; // Maps to X, Y, or Z

    [JsonProperty("HeightAxis")]
    public string HeightAxis { get; set; } = "GameY"; // X, Y, or Z

    [JsonProperty("Origin")]
    public string Origin { get; set; } = "MinCorner"; // "MinCorner" or potentially "Center"
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