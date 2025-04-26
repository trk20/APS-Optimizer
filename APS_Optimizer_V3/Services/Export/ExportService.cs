using APS_Optimizer_V3.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace APS_Optimizer_V3.Services.Export;

// Represents a block during processing before overlap resolution
internal record PotentialBlock(
    int X, int Y, int Z,
    int BlockId,
    int RotationCode,
    string? BlockDataSegment, // Individual segment, might be null/empty
    double Cost // Store cost per potential block
);

// Represents the final chosen block info for a coordinate
internal record ResolvedBlockInfo(
    int BlockId,
    int RotationCode,
    string? BlockDataSegment,
    double Cost // Store cost here too for verification if needed
);

// Represents the root structure of the exported JSON file
internal class BlueprintRoot
{
    public FileModelVersion FileModelVersion { get; set; } = new();
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; } = 0;
    public int SavedTotalBlockCount { get; set; }
    public double SavedMaterialCost { get; set; }
    public double ContainedMaterialCost { get; set; } = 0.0; // Seems fixed
    public Dictionary<string, string> ItemDictionary { get; set; } = new();
    public Blueprint Blueprint { get; set; } = new();
}

internal class FileModelVersion
{
    public int Major { get; set; } = 1;
    public int Minor { get; set; } = 0;
}

internal class Blueprint
{
    public double ContainedMaterialCost { get; set; } = 0.0;
    // CSI, COL, SCs likely fixed/null based on examples
    public List<string>? COL { get; set; } = null;
    public List<object> SCs { get; set; } = new(); // Assuming empty array []
    [JsonProperty("BLP")] public List<string> BlockPositions { get; set; } = new();
    [JsonProperty("BLR")] public List<int> BlockRotations { get; set; } = new();
    public object? BP1 { get; set; } = null; // Seems null
    public object? BP2 { get; set; } = null; // Seems null
    [JsonProperty("BCI")] public List<int> BlockColorIndices { get; set; } = new();
    public object? BEI { get; set; } = null; // Seems null
    public string BlockData { get; set; } = string.Empty;
    public string VehicleData { get; set; } = string.Empty;
    public bool designChanged { get; set; } = false;
    public int blueprintVersion { get; set; } = 0;
    public string blueprintName { get; set; } = string.Empty;
    public SerializedInfo SerializedInfo { get; set; } = new();
    // Name, ItemNumber, LocalPosition, LocalRotation, ForceId seem metadata/defaults
    public object? Name { get; set; } = null;
    public int ItemNumber { get; set; } = 0;
    public string LocalPosition { get; set; } = "0,0,0";
    public string LocalRotation { get; set; } = "0,0,0,0";
    public int ForceId { get; set; } = 0;
    public int TotalBlockCount { get; set; }
    public string MaxCords { get; set; } = string.Empty;
    public string MinCords { get; set; } = string.Empty;
    [JsonProperty("BlockIds")] public List<int> BlockIds { get; set; } = new();
    public object? BlockState { get; set; } = null; // Seems null
    public int AliveCount { get; set; }
    public object? BlockStringData { get; set; } = null; // Seems null
    public object? BlockStringDataIds { get; set; } = null; // Seems null
    public string GameVersion { get; set; } = "4.2.5.2"; // Or make configurable
    public int PersistentSubObjectIndex { get; set; } = -1;
    public int PersistentBlockIndex { get; set; } = -1;
    public AuthorDetails AuthorDetails { get; set; } = new(); // You might want to make this configurable
    public int BlockCount { get; set; }
}

internal class SerializedInfo
{
    public Dictionary<string, object> JsonDictionary { get; set; } = new(); // Assuming empty
    public bool IsEmpty { get; set; } = true;
}

internal class AuthorDetails // Placeholder - copy or make configurable
{
    public bool Valid { get; set; } = true;
    public int ForeignBlocks { get; set; } = 0;
    public string CreatorId { get; set; } = "c241a249-42d7-4bf7-85f1-4efe34ba5664";
    public string ObjectId { get; set; } = Guid.NewGuid().ToString(); // Generate new one?
    public string CreatorReadableName { get; set; } = "trk20";
    public string HashV1 { get; set; } = ""; // Needs calculation? Or leave empty?
}


public class ExportService
{
    private readonly ExportConfiguration _config;
    private readonly Dictionary<int, BasicBlockDefinition> _blockDefLookup = new(); // Pre-processed lookup
    private readonly Dictionary<string, string> _sharedBlockDataCache = new(); // Pre-processed shared data

    public ExportService(ExportConfiguration config)

    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        PreProcessConfig();
    }

    public (double totalCost, int blockCount) CalculateExportMetrics(
         ImmutableList<Placement> solutionPlacements,
         int targetHeight)
    {
        if (solutionPlacements == null || !solutionPlacements.Any())
        {
            return (0.0, 0);
        }

        // 1. Generate potential blocks (same logic as in GenerateBlueprintJson)
        var potentialBlocks = GeneratePotentialBlocks(solutionPlacements, targetHeight);

        // 2. Resolve overlaps and calculate cost (same logic as in GenerateBlueprintJson)
        var (resolvedBlocks, totalMaterialCost) = ResolveOverlaps(potentialBlocks);

        return (totalMaterialCost, resolvedBlocks.Count);
    }

    private void PreProcessConfig()
    {
        // --- Step 1: Cache shared data ---
        _sharedBlockDataCache.Clear();
        if (_config.SharedBlockData != null)
        {
            foreach (var kvp in _config.SharedBlockData)
            {
                _sharedBlockDataCache[kvp.Key] = kvp.Value;
            }
        }

        // --- Step 2: Populate lookup and resolve shared data references ---
        _blockDefLookup.Clear();
        if (_config.BasicBlockDefinitions == null) return; // Safety check

        foreach (var kvp in _config.BasicBlockDefinitions)
        {
            var blockDef = kvp.Value;
            if (blockDef == null) continue; // Safety check

            // --- Resolve shared block data reference FIRST ---
            // Start with the value directly in BlockData (if any)
            string? resolvedBlockData = blockDef.BlockData;

            // If UseSharedBlockData is specified, try to overwrite with shared data
            if (!string.IsNullOrEmpty(blockDef.UseSharedBlockData))
            {
                if (_sharedBlockDataCache.TryGetValue(blockDef.UseSharedBlockData, out var sharedData))
                {
                    resolvedBlockData = sharedData; // Use shared data
                }
                else
                {
                    Debug.WriteLine($"Warning: Shared BlockData key '{blockDef.UseSharedBlockData}' not found for BlockId {blockDef.BlockId}.");
                    // Keep existing BlockData (which might be null) or null if UseSharedBlockData was the only source
                }
            }
            // --- Store the potentially resolved data back into the object ---
            blockDef.BlockData = resolvedBlockData;

            // Add to lookup dictionary
            if (!_blockDefLookup.ContainsKey(blockDef.BlockId))
            {
                _blockDefLookup.Add(blockDef.BlockId, blockDef);
            }
            else
            {
                Debug.WriteLine($"Warning: Duplicate BlockId {blockDef.BlockId} found in configuration using key {kvp.Key}");
            }
        }
    }



    /// <summary>
    /// Generates the blueprint JSON string.
    /// </summary>
    /// <returns>A tuple containing the JSON string and the calculated total material cost.</returns>
    public (string json, double totalCost, int blockCount) GenerateBlueprintJson(
        ImmutableList<Placement> solutionPlacements,
        int targetHeight, // User input for applicable modes
        string blueprintName)
    {
        if (solutionPlacements == null || !solutionPlacements.Any())
        {
            throw new ArgumentException("Solution placements cannot be null or empty.", nameof(solutionPlacements));
        }

        // 1. Generate all potential blocks from placements
        var potentialBlocks = GeneratePotentialBlocks(solutionPlacements, targetHeight);

        // 2. Resolve overlaps and calculate cost
        var (resolvedBlocks, totalMaterialCost) = ResolveOverlaps(potentialBlocks);

        // 3. Assemble the final blueprint structure
        var blueprintRoot = AssembleFinalBlueprint(resolvedBlocks, totalMaterialCost, blueprintName);

        // 4. Serialize to JSON
        string json = JsonConvert.SerializeObject(blueprintRoot, Formatting.Indented, new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Include // Ensure nulls are written if needed by game
        });

        return (json, totalMaterialCost, blueprintRoot.SavedTotalBlockCount);
    }


    private List<PotentialBlock> GeneratePotentialBlocks(ImmutableList<Placement> solutionPlacements, int targetHeight)
    {
        var potentialBlocks = new List<PotentialBlock>();

        foreach (var placement in solutionPlacements)
        {
            if (!_config.ShapeExportMapping.TryGetValue(placement.ShapeName, out var mapping))
            {
                Debug.WriteLine($"Warning: No export mapping found for shape '{placement.ShapeName}'. Skipping placement.");
                continue;
            }

            // Determine base position using configured coordinate system
            int baseX = placement.Col; // Default if SolverCol -> GameX
            int baseZ = placement.Row; // Default if SolverRow -> GameZ
            int baseY = 0; // Usually start at Y=0

            // TODO: Implement proper coordinate system mapping based on _config.ExportDefaults.CoordinateSystem
            // Example: if (_config.ExportDefaults.CoordinateSystem.SolverCol == "GameZ") baseX = placement.Col; etc.

            switch (mapping.Mode)
            {
                case ExportMode.ScaleBasic:
                    GenerateScaleBasic(potentialBlocks, placement, mapping, targetHeight, baseX, baseY, baseZ);
                    break;
                case ExportMode.StackBasic:
                    GenerateStackBasic(potentialBlocks, placement, mapping, targetHeight, baseX, baseY, baseZ);
                    break;
                case ExportMode.StackPerCell:
                    GenerateStackPerCell(potentialBlocks, placement, mapping, baseX, baseY, baseZ);
                    break;
                default:
                    Debug.WriteLine($"Warning: Unknown export mode '{mapping.Mode}' for shape '{placement.ShapeName}'.");
                    break;
            }
        }
        return potentialBlocks;
    }

    private void GenerateScaleBasic(List<PotentialBlock> potentialBlocks, Placement placement, ShapeExportMapping mapping, int targetHeight, int baseX, int baseY, int baseZ)
    {
        if (mapping.HeightRange == null || mapping.HeightRange.Count != 2 || mapping.CellTypeMap == null) return; // Invalid config

        int height = Math.Clamp(targetHeight, mapping.HeightRange[0], mapping.HeightRange[1]);

        for (int pr = 0; pr < placement.Grid.GetLength(0); pr++)
        {
            for (int pc = 0; pc < placement.Grid.GetLength(1); pc++)
            {
                CellTypeInfo cellType = placement.Grid[pr, pc];
                if (cellType == null || cellType.IsEmpty) continue;

                if (!mapping.CellTypeMap.TryGetValue(cellType.Name, out var blockPrefix)) continue; // No mapping for this cell

                string basicDefKey = $"{blockPrefix}_{height}";
                if (!_config.BasicBlockDefinitions.TryGetValue(basicDefKey, out var basicDef))
                {
                    Debug.WriteLine($"Warning: BasicBlockDefinition not found for key '{basicDefKey}'");
                    continue;
                }

                LogicalOrientation orientation = ToLogicalOrientation(cellType.CurrentRotation); // North, East, South, West
                if (!TryGetRotationCodeAndData(basicDef, orientation, out int rotationCode, out string? blockDataSegment)) continue;

                int absX = baseX + pc;
                int absY = baseY; // ScaleBasic only places at Y=0
                int absZ = baseZ + pr;

                potentialBlocks.Add(new PotentialBlock(absX, absY, absZ, basicDef.BlockId, rotationCode, blockDataSegment, basicDef.MaterialCost));
            }
        }
    }

    private void GenerateStackBasic(List<PotentialBlock> potentialBlocks, Placement placement, ShapeExportMapping mapping, int targetHeight, int baseX, int baseY, int baseZ)
    {
        if (string.IsNullOrEmpty(mapping.BlockDefKey) || mapping.HeightRange == null || mapping.HeightRange.Count != 2) return; // Invalid config

        if (!_config.BasicBlockDefinitions.TryGetValue(mapping.BlockDefKey, out var basicDef))
        {
            Debug.WriteLine($"Warning: BasicBlockDefinition not found for key '{mapping.BlockDefKey}'");
            return;
        }

        int height = Math.Clamp(targetHeight, mapping.HeightRange[0], mapping.HeightRange[1]);

        // StackBasic assumes the placement covers only one cell, find it
        if (placement.CoveredCells.Count != 1)
        {
            Debug.WriteLine($"Warning: StackBasic mode expects exactly one covered cell for placement {placement.ShapeName}, found {placement.CoveredCells.Count}. Using first.");
            // Or potentially iterate CoveredCells if that's intended? Needs clarification.
            if (!placement.CoveredCells.Any()) return;
        }
        var (r, c) = placement.CoveredCells.FirstOrDefault(); // Use the actual cell position
        int pr = r - placement.Row;
        int pc = c - placement.Col;

        // Use default orientation/rotation unless specified differently
        LogicalOrientation orientation = LogicalOrientation.North; // Or derive from cell if needed? Assume fixed for now.
        if (!TryGetRotationCodeAndData(basicDef, orientation, out int rotationCode, out string? blockDataSegment)) return;

        int absX = baseX + pc;
        int absZ = baseZ + pr;

        for (int yOffset = 0; yOffset < height; yOffset++)
        {
            int absY = baseY + yOffset;
            potentialBlocks.Add(new PotentialBlock(absX, absY, absZ, basicDef.BlockId, rotationCode, blockDataSegment, basicDef.MaterialCost));
        }
    }

    private void GenerateStackPerCell(List<PotentialBlock> potentialBlocks, Placement placement, ShapeExportMapping mapping, int baseX, int baseY, int baseZ)
    {
        if (mapping.Height == null || mapping.CellStackDefinitions == null) return; // Invalid config
        int stackHeight = mapping.Height.Value;

        for (int pr = 0; pr < placement.Grid.GetLength(0); pr++)
        {
            for (int pc = 0; pc < placement.Grid.GetLength(1); pc++)
            {
                CellTypeInfo cellType = placement.Grid[pr, pc];
                if (cellType == null || cellType.IsEmpty) continue;

                if (!mapping.CellStackDefinitions.TryGetValue(cellType.Name, out var stackDef)) continue; // No stack def for this cell

                if (stackDef.Count != stackHeight)
                {
                    Debug.WriteLine($"Warning: Stack definition count mismatch for cell '{cellType.Name}' in shape '{placement.ShapeName}'. Expected {stackHeight}, got {stackDef.Count}.");
                    continue;
                }

                for (int yOffset = 0; yOffset < stackHeight; yOffset++)
                {
                    var stackEntry = stackDef[yOffset];
                    if (!_config.BasicBlockDefinitions.TryGetValue(stackEntry.BlockDefKey, out var basicDef))
                    {
                        Debug.WriteLine($"Warning: BasicBlockDefinition not found for key '{stackEntry.BlockDefKey}' in stack.");
                        continue;
                    }

                    LogicalOrientation orientation;
                    if (stackEntry.OrientationSource == RotationSource.FromCell)
                    {
                        orientation = ToLogicalOrientation(cellType.CurrentRotation);  // North, East, South, West
                    }
                    else if (stackEntry.FixedOrientation != null)
                    {
                        orientation = (LogicalOrientation)stackEntry.FixedOrientation; // Up, Down, South, West etc.
                    }
                    else
                    {
                        orientation = LogicalOrientation.North; // Fallback if neither is specified
                    }

                    if (!TryGetRotationCodeAndData(basicDef, orientation, out int rotationCode, out string? blockDataSegment)) continue;

                    int absX = baseX + pc;
                    int absY = baseY + yOffset;
                    int absZ = baseZ + pr;

                    potentialBlocks.Add(new PotentialBlock(absX, absY, absZ, basicDef.BlockId, rotationCode, blockDataSegment, basicDef.MaterialCost));
                }
            }
        }
    }

    private bool TryGetRotationCodeAndData(BasicBlockDefinition basicDef, LogicalOrientation logicalOrientation, out int rotationCode, out string? blockDataSegment)
    {
        rotationCode = 0;
        blockDataSegment = null;
        bool rotationFound = false;
        bool dataFound = false; // Track if we found *any* data definition (even null/empty)

        // --- Determine Rotation Code ---
        if (basicDef.RotationMap != null && basicDef.RotationMap.TryGetValue(logicalOrientation, out int mappedCode))
        {
            rotationCode = mappedCode;
            rotationFound = true;
        }
        else if (basicDef.DefaultRotationCode.HasValue)
        {
            rotationCode = basicDef.DefaultRotationCode.Value;
            rotationFound = true;
        }
        // Ensure rotation is found if strictly required, otherwise use default 0
        if (!rotationFound)
        {
            Debug.WriteLine($"Warning: Rotation code not found for BlockId {basicDef.BlockId}, Orientation '{logicalOrientation}'. Using default 0.");
            // Decide if proceeding with rotation 0 is acceptable, maybe return false?
            // For now, assume 0 is okay and continue.
            rotationFound = true; // Treat default 0 as 'found' for proceeding
        }


        // --- Determine Block Data Segment ---
        // Priority: Orientation-specific map FIRST, then the general BlockData field.
        if (basicDef.BlockDataMap != null && basicDef.BlockDataMap.TryGetValue(logicalOrientation, out var mappedData))
        {
            // Found specific data for this orientation
            blockDataSegment = mappedData;
            dataFound = true;
        }
        // --- CORRECTED CHECK: Check BlockData field *regardless* of IsNullOrEmpty ---
        // The PreProcessConfig step ensures BlockData holds the final value (null, "", or resolved shared data)
        else if (basicDef.BlockData != null) // Check if the property itself exists/was set
        {
            // Use the value stored in BlockData. This handles:
            // - Loaders (BlockData = "")
            // - Clips (BlockData = resolved shared data)
            // - Coolers (BlockData = "")
            // - Blocks with no data defined (BlockData = null, if PreProcess didn't assign "")
            blockDataSegment = basicDef.BlockData;
            dataFound = true;
        }
        // --- Removed the problematic else block ---

        // If data was expected based on block type but not found, could add specific warnings here.
        // Example:
        // if (!dataFound && basicDef.BlockId == 364) // Ammo Intake expects data
        // {
        //     Debug.WriteLine($"Warning: BlockDataMap entry missing for AmmoIntake (ID {basicDef.BlockId}) orientation '{logicalOrientation}'.");
        // }


        // Return true if we successfully determined a rotation code.
        // Data segment being null/empty is handled by the caller or AssembleFinalBlueprint.
        return rotationFound;
    }



    private (Dictionary<(int X, int Y, int Z), ResolvedBlockInfo> resolvedBlocks, double totalCost) ResolveOverlaps(List<PotentialBlock> potentialBlocks)
    {
        var resolvedBlocks = new Dictionary<(int X, int Y, int Z), ResolvedBlockInfo>();

        // Iterate and simply overwrite any existing entry for the coordinate.
        // The last block processed for a given coordinate will be the one stored.
        foreach (var potential in potentialBlocks)
        {
            var coord = (potential.X, potential.Y, potential.Z);
            // Unconditionally add or overwrite the entry.
            resolvedBlocks[coord] = new ResolvedBlockInfo(potential.BlockId, potential.RotationCode, potential.BlockDataSegment, potential.Cost);
        }

        // Calculate total cost AFTER resolution by summing costs of the final blocks.
        double totalMaterialCost = 0.0;
        foreach (var resolvedInfo in resolvedBlocks.Values)
        {
            totalMaterialCost += resolvedInfo.Cost;
        }

        return (resolvedBlocks, totalMaterialCost);
    }


    private BlueprintRoot AssembleFinalBlueprint(Dictionary<(int X, int Y, int Z), ResolvedBlockInfo> resolvedBlocks, double totalMaterialCost, string blueprintName)
    {
        // Handle empty case first
        if (!resolvedBlocks.Any())
        {
            Debug.WriteLine("AssembleFinalBlueprint: No resolved blocks found, returning empty blueprint.");
            // Return a minimal valid structure
            return new BlueprintRoot
            {
                Name = blueprintName,
                SavedTotalBlockCount = 0,
                SavedMaterialCost = 0,
                ItemDictionary = new Dictionary<string, string> { { "0", _config.KnownItemMappings["0"] } }, // Air block
                Blueprint = new Blueprint
                {
                    blueprintName = blueprintName,
                    TotalBlockCount = 0,
                    BlockCount = 0,
                    AliveCount = 0,
                    MinCords = "0,0,0",
                    MaxCords = "0,0,0", // Or maybe 1,1,1? Check game behavior for empty.
                    VehicleData = _config.ExportDefaults.VehicleData,
                    AuthorDetails = new AuthorDetails()
                }
            };
        }

        // --- Initialization ---
        var sortedCoords = resolvedBlocks.Keys.OrderBy(k => k.Z).ThenBy(k => k.Y).ThenBy(k => k.X).ToList();
        var finalBlockPositions = new List<string>(resolvedBlocks.Count);
        var finalBlockIds = new List<int>(resolvedBlocks.Count);
        var finalBlockRotations = new List<int>(resolvedBlocks.Count);
        var finalBlockColorIndices = new List<int>(resolvedBlocks.Count);
        var blockDataStream = new MemoryStream(); // Use MemoryStream to accumulate bytes
        var usedBlockIds = new HashSet<int>();
        int currentBlockIndex = 0; // Index for modifying BlockData

        // --- Calculate Bounds ---
        int minX = sortedCoords.Min(k => k.X);
        int minY = sortedCoords.Min(k => k.Y);
        int minZ = sortedCoords.Min(k => k.Z);
        int maxX = sortedCoords.Max(k => k.X);
        int maxY = sortedCoords.Max(k => k.Y);
        int maxZ = sortedCoords.Max(k => k.Z);

        // --- Process Blocks ---
        foreach (var coord in sortedCoords)
        {
            var blockInfo = resolvedBlocks[coord];

            // Calculate relative position
            int relX = coord.X - minX;
            int relY = coord.Y - minY;
            int relZ = coord.Z - minZ;

            // Add to final lists
            finalBlockPositions.Add($"{relZ - 0.5 * maxZ},{relY},{relX}");
            finalBlockIds.Add(blockInfo.BlockId);
            finalBlockRotations.Add(blockInfo.RotationCode);
            finalBlockColorIndices.Add(_config.ExportDefaults.DefaultBCIValue);
            usedBlockIds.Add(blockInfo.BlockId);

            // --- Process BlockData ---
            if (!string.IsNullOrEmpty(blockInfo.BlockDataSegment))
            {
                try
                {
                    // Decode the Base64 segment from the resolved info
                    byte[] exampleBytes = Convert.FromBase64String(blockInfo.BlockDataSegment);

                    // Verify length (at least 3 bytes for the index)
                    if (exampleBytes.Length >= 3)
                    {
                        // Modify the index (first 3 bytes) in a copy
                        byte[] modifiedBytes = (byte[])exampleBytes.Clone(); // Work on a copy
                        WriteUInt24LittleEndian(modifiedBytes, 0, (uint)currentBlockIndex);

                        // Append the modified bytes to the stream
                        blockDataStream.Write(modifiedBytes, 0, modifiedBytes.Length);
                    }
                    else
                    {
                        Debug.WriteLine($"Warning: BlockDataSegment for BlockId {blockInfo.BlockId} at index {currentBlockIndex} is too short ({exampleBytes.Length} bytes). Skipping data append.");
                    }
                }
                catch (FormatException ex)
                {
                    Debug.WriteLine($"Error: Invalid Base64 string in BlockDataSegment for BlockId {blockInfo.BlockId} at index {currentBlockIndex}. Data: '{blockInfo.BlockDataSegment}'. Error: {ex.Message}");
                    // Skip appending data for this block
                }
            }
            // --- End Process BlockData ---

            currentBlockIndex++; // Increment for the next block
        }

        // --- Finalize BlockData String ---
        string finalBlockDataString = Convert.ToBase64String(blockDataStream.ToArray());
        blockDataStream.Dispose(); // Dispose the stream

        // --- Build Item Dictionary ---
        var itemDictionary = new Dictionary<string, string>();
        if (_config.KnownItemMappings.TryGetValue("0", out var airGuid))
        {
            itemDictionary.Add("0", airGuid);
        }
        else { Debug.WriteLine("Error: '0' key missing in KnownItemMappings."); }

        foreach (int id in usedBlockIds)
        {
            string idStr = id.ToString(CultureInfo.InvariantCulture);
            if (_config.KnownItemMappings.TryGetValue(idStr, out var guid))
            {
                if (!itemDictionary.ContainsKey(idStr)) itemDictionary.Add(idStr, guid);
            }
            else Debug.WriteLine($"Warning: GUID mapping not found for BlockId {id}.");
        }


        // --- Assemble Root Object ---
        var root = new BlueprintRoot
        {
            Name = blueprintName,
            SavedTotalBlockCount = resolvedBlocks.Count,
            SavedMaterialCost = totalMaterialCost,
            ItemDictionary = itemDictionary,
            Blueprint = new Blueprint
            {
                BlockPositions = finalBlockPositions,
                BlockRotations = finalBlockRotations,
                BlockIds = finalBlockIds,
                BlockColorIndices = finalBlockColorIndices,
                BlockData = finalBlockDataString, // Use the final concatenated string
                TotalBlockCount = resolvedBlocks.Count,
                BlockCount = resolvedBlocks.Count,
                AliveCount = resolvedBlocks.Count,
                MinCords = $"{minX - minX},{minY - minY},{minZ - minZ}", // Always "0,0,0"
                MaxCords = $"{maxX - minX + 1},{maxY - minY + 1},{maxZ - minZ + 1}",
                blueprintName = blueprintName,
                VehicleData = _config.ExportDefaults.VehicleData,
                AuthorDetails = new AuthorDetails { ObjectId = Guid.NewGuid().ToString() }
            }
        };

        return root;
    }

    // Helper method to write a 24-bit unsigned integer in little-endian format
    private static void WriteUInt24LittleEndian(byte[] buffer, int offset, uint value)
    {
        if (offset + 3 > buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset is too large for the buffer size.");
        if (value > 0xFFFFFF) // Ensure value fits in 24 bits
            throw new ArgumentOutOfRangeException(nameof(value), "Value exceeds maximum for 24 bits.");

        buffer[offset] = (byte)(value & 0xFF);          // Least significant byte
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF); // Middle byte
        buffer[offset + 2] = (byte)((value >> 16) & 0xFF); // Most significant byte
    }


    private static LogicalOrientation ToLogicalOrientation(RotationDirection direction) => direction switch
    {
        RotationDirection.North => LogicalOrientation.North,
        RotationDirection.South => LogicalOrientation.South,
        RotationDirection.East => LogicalOrientation.East,
        RotationDirection.West => LogicalOrientation.West,
        _ => LogicalOrientation.North,
    };

}