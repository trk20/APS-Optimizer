using APS_Optimizer_V3.Helpers;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Globalization;

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
    double Cost
);

// Represents the root structure of exported JSON file
internal class BlueprintRoot
{
    public FileModelVersion FileModelVersion { get; set; } = new();
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; } = 0;
    public int SavedTotalBlockCount { get; set; }
    public double SavedMaterialCost { get; set; }
    public double ContainedMaterialCost { get; set; } = 0.0;
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
    public List<string>? COL { get; set; } = null;
    public List<object> SCs { get; set; } = new();
    [JsonProperty("BLP")] public List<string> BlockPositions { get; set; } = new();
    [JsonProperty("BLR")] public List<int> BlockRotations { get; set; } = new();
    public object? BP1 { get; set; } = null;
    public object? BP2 { get; set; } = null;
    [JsonProperty("BCI")] public List<int> BlockColorIndices { get; set; } = new(); // not sure
    public object? BEI { get; set; } = null;
    public string BlockData { get; set; } = string.Empty;
    public string VehicleData { get; set; } = string.Empty;
    public bool designChanged { get; set; } = false;
    public int blueprintVersion { get; set; } = 0;
    public string blueprintName { get; set; } = string.Empty;
    public SerializedInfo SerializedInfo { get; set; } = new();
    // Name, ItemNumber, LocalPosition, LocalRotation
    public object? Name { get; set; } = null;
    public int ItemNumber { get; set; } = 0;
    public string LocalPosition { get; set; } = "0,0,0";
    public string LocalRotation { get; set; } = "0,0,0,0";
    public int ForceId { get; set; } = 0;
    public int TotalBlockCount { get; set; }
    public string MaxCords { get; set; } = string.Empty;
    public string MinCords { get; set; } = string.Empty;
    [JsonProperty("BlockIds")] public List<int> BlockIds { get; set; } = new();
    public object? BlockState { get; set; } = null;
    public int AliveCount { get; set; }
    public object? BlockStringData { get; set; } = null;
    public object? BlockStringDataIds { get; set; } = null;
    public string GameVersion { get; set; } = "4.2.5.2"; // Possibly make configurable?
    public int PersistentSubObjectIndex { get; set; } = -1;
    public int PersistentBlockIndex { get; set; } = -1;
    public AuthorDetails AuthorDetails { get; set; } = new(); // Might make this configurable
    public int BlockCount { get; set; }
}

internal class SerializedInfo
{
    public Dictionary<string, object> JsonDictionary { get; set; } = new();
    public bool IsEmpty { get; set; } = true;
}

internal class AuthorDetails // might make configurable but works for now
{
    public bool Valid { get; set; } = true;
    public int ForeignBlocks { get; set; } = 0;
    public string CreatorId { get; set; } = "c241a249-42d7-4bf7-85f1-4efe34ba5664";
    public string ObjectId { get; set; } = Guid.NewGuid().ToString();
    public string CreatorReadableName { get; set; } = "trk20";
    public string HashV1 { get; set; } = ""; // looks to be unneeded but leaving in anyway
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

        // Generate potential blocks
        var potentialBlocks = GeneratePotentialBlocks(solutionPlacements, targetHeight);

        // Resolve overlaps and calculate cost
        var (resolvedBlocks, totalMaterialCost) = ResolveOverlaps(potentialBlocks);

        return (totalMaterialCost, resolvedBlocks.Count);
    }

    private void PreProcessConfig()
    {
        // --- Cache shared data ---
        _sharedBlockDataCache.Clear();
        if (_config.SharedBlockData != null)
        {
            foreach (var kvp in _config.SharedBlockData)
            {
                _sharedBlockDataCache[kvp.Key] = kvp.Value;
            }
        }

        // --- Populate lookup and resolve shared data references ---
        _blockDefLookup.Clear();
        if (_config.BasicBlockDefinitions == null) return;

        foreach (var kvp in _config.BasicBlockDefinitions)
        {
            var blockDef = kvp.Value;
            if (blockDef == null) continue;

            // --- Resolve shared block data reference ---
            // Start with value directly in BlockData (if any)
            string? resolvedBlockData = blockDef.BlockData;

            // If UseSharedBlockData specified, try to overwrite with shared data
            if (!string.IsNullOrEmpty(blockDef.UseSharedBlockData))
            {
                if (_sharedBlockDataCache.TryGetValue(blockDef.UseSharedBlockData, out var sharedData))
                {
                    resolvedBlockData = sharedData;
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

    public (int minHeight, int maxHeight, int step) CalculateHeightRules(ImmutableList<Placement>? placements)
    {
        int overallMin = 1;
        int overallMax = 8;
        int step = 1;
        List<int> stackHeights = new();
        bool hasScaling = false;
        bool hasStacking = false;

        if (placements == null || !placements.Any())
        {
            Debug.WriteLine("CalculateHeightRules: No placements provided, returning default (1, 8, 1).");
            return (1, 8, 1); // Return default range if no solution
        }

        var uniqueShapeNames = placements.Select(p => p.ShapeName).Distinct().ToList();

        // Get rules for each unique shape type in the solution
        foreach (var shapeName in uniqueShapeNames)
        {
            if (_config.ShapeExportMapping.TryGetValue(shapeName, out var mapping))
            {
                if (mapping.Mode == ExportMode.ScaleBasic || mapping.Mode == ExportMode.StackBasic)
                {
                    if (mapping.HeightRange != null && mapping.HeightRange.Count == 2)
                    {
                        hasScaling = true;
                        overallMin = Math.Max(overallMin, mapping.HeightRange[0]); // Highest minimum
                        overallMax = Math.Min(overallMax, mapping.HeightRange[1]); // Lowest maximum
                    }
                }
                else if (mapping.Mode == ExportMode.StackPerCell && mapping.Height.HasValue)
                {
                    hasStacking = true;
                    stackHeights.Add(mapping.Height.Value);
                }
            }
            else
            {
                Debug.WriteLine($"Warning: CalculateHeightRules - No export mapping found for shape '{shapeName}'.");
            }
        }

        // Determine rules based on shapes present
        if (hasStacking)
        {
            step = MathUtils.CalculateListGcd(stackHeights.Distinct());
            step = Math.Max(1, step); // Check step is at least 1

            if (hasScaling)
            {
                // Find intersection respecting step
                int firstValidMultiple = overallMin % step == 0
                   ? overallMin
                   : overallMin + (step - (overallMin % step));
                // Make sure start is at least one full step size if min was below it
                firstValidMultiple = Math.Max(step, firstValidMultiple);

                // Check start is within the original max bound
                if (firstValidMultiple > overallMax)
                {
                    // No valid range exists
                    Debug.WriteLine($"Height rule conflict (Mixed): First valid step {firstValidMultiple} exceeds max bound {overallMax}.");
                    return (1, 0, step); // Return impossible range (min > max)
                }

                int lastValidMultiple = overallMax / step * step; // Round down

                overallMin = firstValidMultiple;
                overallMax = lastValidMultiple;
            }
            else
            {
                // Only Stacking: Min and Step are GCD, Max is multiple of GCD
                overallMin = step;
                overallMax = step * 15;
            }
        }
        else if (hasScaling)
        {
            step = 1;
        }

        // Sanity check
        overallMin = Math.Max(1, overallMin);
        if (overallMin > overallMax)
        {
            Debug.WriteLine($"Height rule conflict (Final): Min ({overallMin}) > Max ({overallMax})");
            return (overallMin, overallMax, step);
        }

        Debug.WriteLine($"Calculated Height Rules: Min={overallMin}, Max={overallMax}, Step={step}");
        return (overallMin, overallMax, step);
    }



    // Generates blueprint JSON string.
    public (string json, double totalCost, int blockCount) GenerateBlueprintJson(
        ImmutableList<Placement> solutionPlacements,
        int targetHeight,
        string blueprintName)
    {
        if (solutionPlacements == null || !solutionPlacements.Any())
        {
            throw new ArgumentException("Solution placements cannot be null or empty.", nameof(solutionPlacements));
        }

        // Generate all potential blocks from placements
        var potentialBlocks = GeneratePotentialBlocks(solutionPlacements, targetHeight);

        // Resolve overlaps and calculate cost
        var (resolvedBlocks, totalMaterialCost) = ResolveOverlaps(potentialBlocks);

        // Assemble the final blueprint structure
        var blueprintRoot = AssembleFinalBlueprint(resolvedBlocks, totalMaterialCost, blueprintName, targetHeight);

        // Serialize to JSON
        string json = JsonConvert.SerializeObject(blueprintRoot, Formatting.Indented, new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Include // Just in case they're needed
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

            int baseX = placement.Col;
            int baseZ = placement.Row;
            int baseY = 0;

            switch (mapping.Mode)
            {
                case ExportMode.ScaleBasic:
                    GenerateScaleBasic(potentialBlocks, placement, mapping, targetHeight, baseX, baseY, baseZ);
                    break;
                case ExportMode.StackBasic:
                    GenerateStackBasic(potentialBlocks, placement, mapping, targetHeight, baseX, baseY, baseZ);
                    break;
                case ExportMode.StackPerCell:
                    GenerateStackPerCell(potentialBlocks, placement, mapping, targetHeight, baseX, baseY, baseZ);
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


    // Not currently needed but whatever
    private void GenerateStackBasic(List<PotentialBlock> potentialBlocks, Placement placement, ShapeExportMapping mapping, int targetHeight, int baseX, int baseY, int baseZ)
    {
        if (string.IsNullOrEmpty(mapping.BlockDefKey) || mapping.HeightRange == null || mapping.HeightRange.Count != 2) return; // Invalid config

        if (!_config.BasicBlockDefinitions.TryGetValue(mapping.BlockDefKey, out var basicDef))
        {
            Debug.WriteLine($"Warning: BasicBlockDefinition not found for key '{mapping.BlockDefKey}'");
            return;
        }

        int height = Math.Clamp(targetHeight, mapping.HeightRange[0], mapping.HeightRange[1]);

        // StackBasic assumes placement covers only one cell
        if (placement.CoveredCells.Count != 1)
        {
            Debug.WriteLine($"Warning: StackBasic mode expects exactly one covered cell for placement {placement.ShapeName}, found {placement.CoveredCells.Count}. Using first.");
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


    private void GenerateStackPerCell(List<PotentialBlock> potentialBlocks, Placement placement, ShapeExportMapping mapping, int targetHeight, int baseX, int baseY, int baseZ)
    {
        if (mapping.Height == null || mapping.Height <= 0 || mapping.CellStackDefinitions == null)
        {
            Debug.WriteLine($"Error: Invalid StackPerCell config for shape '{placement.ShapeName}'. Missing Height or CellStackDefinitions.");
            return;
        }
        int sectionHeight = mapping.Height.Value;
        int numberOfSections = targetHeight / sectionHeight;

        if (targetHeight % sectionHeight != 0)
        {
            Debug.WriteLine($"Warning: Target height {targetHeight} is not a multiple of section height {sectionHeight} for shape '{placement.ShapeName}'. Building {numberOfSections} full sections.");
        }

        if (numberOfSections <= 0)
        {
            Debug.WriteLine($"Warning: Target height {targetHeight} is less than section height {sectionHeight} for shape '{placement.ShapeName}'. No blocks generated.");
            return;
        }

        int gridRows = placement.Grid.GetLength(0);
        int gridCols = placement.Grid.GetLength(1);

        for (int pr = 0; pr < gridRows; pr++)
        {
            for (int pc = 0; pc < gridCols; pc++)
            {
                CellTypeInfo cellType = placement.Grid[pr, pc];
                if (cellType == null || cellType.IsEmpty) continue;

                if (!mapping.CellStackDefinitions.TryGetValue(cellType.Name, out var stackDef))
                {
                    Debug.WriteLineIf(!cellType.Name.Contains("Generic"), $"Warning: No CellStackDefinition for cell type '{cellType.Name}' in shape '{placement.ShapeName}'.");
                    continue;
                }


                if (stackDef.Count != sectionHeight)
                {
                    Debug.WriteLine($"Warning: Stack definition count mismatch for cell '{cellType.Name}' in shape '{placement.ShapeName}'. Expected {sectionHeight}, got {stackDef.Count}.");
                    continue;
                }

                // Iterate for the number of sections determined by targetHeight
                for (int sectionIndex = 0; sectionIndex < numberOfSections; sectionIndex++)
                {
                    // Iterate through the blocks defined for one section
                    for (int yInSection = 0; yInSection < sectionHeight; yInSection++)
                    {
                        var stackEntry = stackDef[yInSection];
                        if (!_config.BasicBlockDefinitions.TryGetValue(stackEntry.BlockDefKey, out var basicDef))
                        {
                            Debug.WriteLine($"Warning: BasicBlockDefinition not found for key '{stackEntry.BlockDefKey}' in stack (Cell: {cellType.Name}).");
                            continue;
                        }

                        LogicalOrientation orientation;
                        if (stackEntry.OrientationSource == RotationSource.FromCell)
                        {
                            orientation = ToLogicalOrientation(cellType.CurrentRotation);
                        }
                        else if (stackEntry.FixedOrientation.HasValue)
                        {
                            orientation = stackEntry.FixedOrientation.Value;
                        }
                        else
                        {
                            orientation = LogicalOrientation.North; // Fallback
                        }

                        if (!TryGetRotationCodeAndData(basicDef, orientation, out int rotationCode, out string? blockDataSegment))
                        {
                            Debug.WriteLine($"Warning: Failed to get rotation/data for stack entry '{stackEntry.BlockDefKey}' with orientation {orientation}.");
                            continue;
                        }

                        // Calculate absolute coordinates
                        int absX = baseX + pc;
                        // Base Y for this section + offset within the section
                        int absY = baseY + (sectionIndex * sectionHeight) + yInSection;
                        int absZ = baseZ + pr;

                        potentialBlocks.Add(new PotentialBlock(absX, absY, absZ, basicDef.BlockId, rotationCode, blockDataSegment, basicDef.MaterialCost));
                    }
                }
            }
        }
    }

    private bool TryGetRotationCodeAndData(BasicBlockDefinition basicDef, LogicalOrientation logicalOrientation, out int rotationCode, out string? blockDataSegment)
    {
        rotationCode = 0;
        blockDataSegment = null;
        bool rotationFound = false;

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
        // Make sure rotation is found if strictly required, otherwise use default
        if (!rotationFound)
        {
            Debug.WriteLine($"Warning: Rotation code not found for BlockId {basicDef.BlockId}, Orientation '{logicalOrientation}'. Using default.");
            rotationFound = true;
        }

        // --- Determine Block Data Segment ---
        // Priority: Orientation-specific map first, then general BlockData field
        if (basicDef.BlockDataMap != null && basicDef.BlockDataMap.TryGetValue(logicalOrientation, out var mappedData))
        {
            // Found specific data for this orientation
            blockDataSegment = mappedData;
        }
        // --- Check BlockData field ---
        else if (basicDef.BlockData != null)
        {
            blockDataSegment = basicDef.BlockData;
        }


        // Return true if rotation code successfully determined
        return rotationFound;
    }



    private (Dictionary<(int X, int Y, int Z), ResolvedBlockInfo> resolvedBlocks, double totalCost) ResolveOverlaps(List<PotentialBlock> potentialBlocks)
    {
        var resolvedBlocks = new Dictionary<(int X, int Y, int Z), ResolvedBlockInfo>();

        // Iterate - overwrite any existing entry for the coordinate.
        // Last block processed for a given coordinate will be the one stored
        foreach (var potential in potentialBlocks)
        {
            var coord = (potential.X, potential.Y, potential.Z);
            resolvedBlocks[coord] = new ResolvedBlockInfo(potential.BlockId, potential.RotationCode, potential.BlockDataSegment, potential.Cost);
        }

        // Calculate total cost after resolution 
        double totalMaterialCost = 0.0;
        foreach (var resolvedInfo in resolvedBlocks.Values)
        {
            totalMaterialCost += resolvedInfo.Cost;
        }

        return (resolvedBlocks, totalMaterialCost);
    }


    private BlueprintRoot AssembleFinalBlueprint(Dictionary<(int X, int Y, int Z), ResolvedBlockInfo> resolvedBlocks, double totalMaterialCost, string blueprintName, int blueprintHeight)
    {
        if (!resolvedBlocks.Any())
        {
            Debug.WriteLine("AssembleFinalBlueprint: No resolved blocks found, returning empty blueprint.");
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
                    MaxCords = "0,0,0",
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
        var blockDataStream = new MemoryStream(); // MemoryStream to accumulate bytes
        var usedBlockIds = new HashSet<int>();
        int currentBlockIndex = 0; // Index for modifying BlockData

        // --- Calculate Bounds ---
        int minX = sortedCoords.Min(k => k.X);
        int minY = sortedCoords.Min(k => k.Y);
        int minZ = sortedCoords.Min(k => k.Z);
        int maxX = sortedCoords.Max(k => k.X);
        int maxY = blueprintHeight;
        int maxZ = sortedCoords.Max(k => k.Z);

        // --- Process Blocks ---
        foreach (var coord in sortedCoords)
        {
            var blockInfo = resolvedBlocks[coord];

            // Calculate relative position
            int relX = coord.X - minX - ((maxX - minX) / 2);
            int relY = coord.Y - minY;
            int relZ = maxZ - coord.Z;

            // Add to final lists
            //                         yes this is weird
            finalBlockPositions.Add($"{relX},{relY},{relZ}");
            finalBlockIds.Add(blockInfo.BlockId);
            finalBlockRotations.Add(blockInfo.RotationCode);
            finalBlockColorIndices.Add(_config.ExportDefaults.DefaultBCIValue);
            usedBlockIds.Add(blockInfo.BlockId);


            // --- Process BlockData ---
            if (!string.IsNullOrEmpty(blockInfo.BlockDataSegment))
            {
                // BlockData accumulation - adapted method extracted from FTD using dnspy
                try
                {
                    // Decode B64 segment from resolved info
                    byte[] exampleBytes = Convert.FromBase64String(blockInfo.BlockDataSegment);

                    // Check length (at least 3 bytes for the index)
                    if (exampleBytes.Length >= 3)
                    {
                        // Modify the index (first 3 bytes) in a copy
                        byte[] modifiedBytes = (byte[])exampleBytes.Clone();
                        WriteUInt24LittleEndian(modifiedBytes, 0, (uint)currentBlockIndex);

                        // Append modified bytes to stream
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
                    // Skip block
                }
            }

            currentBlockIndex++; // Increment for next block
        }

        // --- Finalize BlockData String ---
        string finalBlockDataString = Convert.ToBase64String(blockDataStream.ToArray());
        blockDataStream.Dispose();

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
                BlockData = finalBlockDataString, // Use final concatenated string
                TotalBlockCount = resolvedBlocks.Count,
                BlockCount = resolvedBlocks.Count,
                AliveCount = resolvedBlocks.Count,
                MinCords = "0,0,0",
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
        if (value > 0xFFFFFF) // Make sure value fits in 24 bits
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