using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using APS_Optimizer_V3.Helpers;
using APS_Optimizer_V3.ViewModels;

namespace APS_Optimizer_V3.Services;

public class SolverService
{
    private const string CryptoMiniSatPath = "cryptominisat5.exe";

    public async Task<SolverResult> SolveAsync(SolveParameters parameters, IProgress<string>? progress = null)
    {
        var stopwatch = Stopwatch.StartNew();
        Debug.WriteLine($"Solver started. Grid: {parameters.GridWidth}x{parameters.GridHeight}, Shapes: {parameters.EnabledShapes.Count}, Symmetry: {parameters.SelectedSymmetry}");

        var iterationLogs = new List<SolverIterationLog>();

        // Generate all possible valid placements
        progress?.Report("Generating placements...");
        var (allPlacements, _) = GeneratePlacements(parameters);
        if (!allPlacements.Any())
        {
            return new SolverResult(false, "No valid placements possible for any shape.", 0, null, null);
        }
        Debug.WriteLine($"Generated {allPlacements.Count} raw placements.");

        // Apply Symmetry and Group Placements
        progress?.Report("Applying symmetry...");
        VariableManager varManager = new VariableManager();
        var solveElements = ApplySymmetryAndGroup(
            allPlacements,
            parameters,
            varManager,
            out var variableToObjectMap // Populated by ApplySymmetryAndGroup
        );

        if (!solveElements.Any()) // Should not happen if allPlacements was not empty, but check anyway
        {
            return new SolverResult(false, "Grouping resulted in zero elements.", 0, null, null);
        }


        // Detect Collisions
        progress?.Report("Analyzing conflicts...");
        var cellCollisions = DetectCollisions(solveElements, parameters.GridWidth);
        Debug.WriteLine($"Detected collisions across {cellCollisions.Count} cells.");

        var conflictClauses = new List<List<int>>();
        // Use the solveElements list which contains all ISolveElement (Placements and SymmetryGroups)
        for (int i = 0; i < solveElements.Count; i++)
        {
            for (int j = i + 1; j < solveElements.Count; j++)
            {
                var e1 = solveElements[i];
                var e2 = solveElements[j];
                int v1 = e1.VariableId;
                int v2 = e2.VariableId;

                // Find cells covered by both elements
                // Possibly switch to HashSet efficient intersection lookup if needed
                var e1Cells = e1.GetAllCoveredCells();
                var e2Cells = e2.GetAllCoveredCells();
                var overlappingCells = e1Cells.Intersect(e2Cells);

                bool hardConflictFound = false;
                foreach (var (r, c) in overlappingCells)
                {
                    // Determine the CellTypeInfo from each element at the overlapping cell
                    bool type1Found = TryGetCellTypeForElementAt(e1, r, c, out CellTypeInfo? type1);
                    bool type2Found = TryGetCellTypeForElementAt(e2, r, c, out CellTypeInfo? type2);

                    // This case indicates an issue, likely GetAllCoveredCells doesn't match TryGetCellTypeForElementAt logic
                    if (!type1Found || !type2Found || type1 == null || type2 == null)
                    {
                        Debug.WriteLine($"CRITICAL WARNING: Could not find cell type for overlapping cell ({r},{c}) between elements {v1} and {v2}. Assuming hard conflict.");
                        hardConflictFound = true;
                        break;
                    }

                    // Check for hard conflict based on CanSelfIntersect and Name
                    bool isConflictAtCell = !type1.CanSelfIntersect ||   // Type 1 doesn't allow self-intersection
                                            !type2.CanSelfIntersect ||   // Type 2 doesn't allow self-intersection
                                            type1.Name != type2.Name;    // Types are different (self-intersection only for same type)

                    if (isConflictAtCell)
                    {
                        hardConflictFound = true;
                        break; // Found one hard conflict cell, no need to check others for this pair
                    }
                }

                // If a hard conflict was found at any overlapping cell, add the exclusion clause
                if (hardConflictFound)
                {
                    conflictClauses.Add(new List<int> { -v1, -v2 });
                }
            }
        }
        Debug.WriteLine($"Generated {conflictClauses.Count} pairwise conflict clauses. Max Var ID so far: {varManager.GetMaxVariableId()}");



        progress?.Report("Starting optimization...");

        // Solving Loop
        var shapeAreas = parameters.EnabledShapes
        .Select(s => s.GetArea())
        .Where(area => area > 0)
        .Distinct()
        .ToList();
        if (!shapeAreas.Any()) { return new SolverResult(false, "...", 0, null, null); }
        int decrementStep = parameters.EnabledShapes.Any(s => s.CouldSelfIntersect()) ? 1 : MathUtils.CalculateListGcd(shapeAreas);
        int totalAvailableCells = parameters.GridWidth * parameters.GridHeight - parameters.BlockedCells.Count;
        int requiredCells = totalAvailableCells / decrementStep * decrementStep;
        int iterationCounter = 0;

        while (requiredCells >= 0)
        {
            iterationCounter++;
            progress?.Report($"Optimization attempt #{iterationCounter}...");
            //Debug.WriteLine($"Attempting to solve for at least {requiredCells} covered cells.");
            var currentClauses = new List<List<int>>(conflictClauses);
            var currentAuxVarManager = new VariableManager();
            while (currentAuxVarManager.GetMaxVariableId() < varManager.GetMaxVariableId()) { currentAuxVarManager.GetNextVariable(); }

            // Generate Coverage Constraint CNF (using Y variables)
            int[,] yVars = new int[parameters.GridHeight, parameters.GridWidth];
            var yVarLinkClauses = new List<List<int>>();
            var yVarList = new List<int>();

            for (int r = 0; r < parameters.GridHeight; r++)
            {
                for (int c = 0; c < parameters.GridWidth; c++)
                {
                    if (parameters.BlockedCells.Contains((r, c))) { yVars[r, c] = 0; continue; }

                    // Generate a new Y variable for this cell
                    int yVar = currentAuxVarManager.GetNextVariable();
                    yVars[r, c] = yVar;
                    yVarList.Add(yVar);
                    int cellIndex = r * parameters.GridWidth + c;

                    if (cellCollisions.TryGetValue(cellIndex, out var elementsCoveringCellVars))
                    {
                        // Y[r,c] => OR(Elements covering (r,c))
                        // CNF: -Y[r,c] OR E1 OR E2 ...
                        var yImpElementsClause = new List<int> { -yVar };
                        yImpElementsClause.AddRange(elementsCoveringCellVars);
                        yVarLinkClauses.Add(yImpElementsClause);

                        // Element_i => Y[r,c] for each E_i covering (r,c)
                        // CNF: -E_i OR Y[r,c]
                        foreach (int elementVar in elementsCoveringCellVars)
                        {
                            yVarLinkClauses.Add(new List<int> { -elementVar, yVar });
                        }
                    }
                    else
                    {
                        // If no element covers this cell, Y must be false
                        yVarLinkClauses.Add(new List<int> { -yVar }); // -Y 0
                    }
                }
            }
            currentClauses.AddRange(yVarLinkClauses);


            // Encode Sum(Y_vars) >= requiredCells
            var (coverageClausesList, _) = SequentialCounter.EncodeAtLeastK(yVarList, requiredCells, currentAuxVarManager);
            currentClauses.AddRange(coverageClausesList);


            // Finalize CNF and run solver
            int totalVars = currentAuxVarManager.GetMaxVariableId();
            string finalCnfString = FormatDimacs(currentClauses, totalVars);
            // File.WriteAllText($"debug_solver_input_{requiredCells}.cnf", finalCnfString);
            var iterationStopwatch = Stopwatch.StartNew();

            // Use max threads - 1 to leave one for UI
            var threads = Math.Max(Environment.ProcessorCount - 1, 1);
            var (sat, solutionVars, error) = await RunSatSolver(finalCnfString, $"--threads {threads}");
            iterationStopwatch.Stop();
            var logEntry = new SolverIterationLog(
                IterationNumber: iterationCounter,
                RequiredCells: requiredCells,
                Variables: totalVars,
                Clauses: currentClauses.Count,
                Duration: iterationStopwatch.Elapsed,
                IsSatisfiable: sat
            );
            iterationLogs.Add(logEntry);

            // Process Result
            if (!string.IsNullOrWhiteSpace(error))
            {
                stopwatch.Stop();
                Debug.WriteLine($"Error during SAT solver execution: {error}");
                progress?.Report("Error");
                return new SolverResult(false, "SAT solver error: " + error, 0, null, null);
            }
            else if (sat && solutionVars != null)
            {
                stopwatch.Stop();
                progress?.Report($"Solution found, displaying...");
                Debug.WriteLine($"SATISFIABLE found for >= {requiredCells} cells. Time: {stopwatch.ElapsedMilliseconds} ms");

                var solutionPlacements = MapResult(solutionVars, variableToObjectMap);
                return new SolverResult(true, $"Solution found covering at least {requiredCells} cells.", requiredCells, solutionPlacements, iterationLogs.ToImmutableList());
            }
            else
            {
                Debug.WriteLine($"UNSATISFIABLE for >= {requiredCells} cells.");
                if (requiredCells == 0) break;
                requiredCells -= decrementStep;
                if (requiredCells < 0) requiredCells = 0;
            }
        }
        stopwatch.Stop();
        progress?.Report("Failed: No solution found after all attempts.");
        return new SolverResult(false, "No solution found!", 0, null, null);
    }


    // --- Helper to Format DIMACS String ---
    private string FormatDimacs(List<List<int>> clauses, int variableCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"p cnf {variableCount} {clauses.Count}");
        foreach (var clause in clauses)
        {
            // Check if clause is empty which is invalid DIMACS, shouldn't happen with correct logic
            if (clause.Any())
            {
                sb.Append(string.Join(" ", clause));
                sb.AppendLine(" 0");
            }
            else
            {
                Debug.WriteLine("Warning: Encountered empty clause during DIMACS formatting.");
            }
        }
        return sb.ToString();
    }

    private (List<Placement>, Dictionary<int, int>) GeneratePlacements(SolveParameters parameters)
    {
        var placements = new List<Placement>();
        int placementIdCounter = 0;
        var blockedSet = parameters.BlockedCells.ToHashSet();

        for (int shapeIndex = 0; shapeIndex < parameters.EnabledShapes.Count; shapeIndex++)
        {
            var shapeInfo = parameters.EnabledShapes[shapeIndex];
            var rotations = shapeInfo.GetAllRotationGrids();

            for (int rotIndex = 0; rotIndex < rotations.Count; rotIndex++)
            {
                var grid = rotations[rotIndex];
                int pHeight = grid.GetLength(0);
                int pWidth = grid.GetLength(1);

                if (pHeight == 0 || pWidth == 0) continue;

                for (int r = 0; r <= parameters.GridHeight - pHeight; r++)
                {
                    for (int c = 0; c <= parameters.GridWidth - pWidth; c++)
                    {
                        bool isValid = true;
                        var covered = new List<(int r, int c)>();

                        for (int pr = 0; pr < pHeight; pr++)
                        {
                            for (int pc = 0; pc < pWidth; pc++)
                            {
                                if (!grid[pr, pc].IsEmpty)
                                {
                                    int gridR = r + pr;
                                    int gridC = c + pc;

                                    if (gridR < 0 || gridR >= parameters.GridHeight || gridC < 0 || gridC >= parameters.GridWidth)
                                    {
                                        Debug.WriteLine($"!!! Internal Error: Coords out of bounds. Shape: {shapeInfo.Name}, Pos ({r},{c}), Offset ({pr},{pc})");
                                        isValid = false; break;
                                    }
                                    if (blockedSet.Contains((gridR, gridC)))
                                    {
                                        isValid = false; break;
                                    }
                                    covered.Add((gridR, gridC));
                                }
                            }
                            if (!isValid) break;
                        }

                        if (isValid)
                        {
                            bool shapeHasAnyCells = false;
                            foreach (CellTypeInfo cellType in grid)
                            {
                                if (!cellType.IsEmpty)
                                {
                                    shapeHasAnyCells = true;
                                    break;
                                }
                            }
                            if (shapeHasAnyCells && !covered.Any())
                            {
                                Debug.WriteLine($"Warning: Placement deemed valid but covered list is empty. Shape: {shapeInfo.Name}, Pos ({r},{c})");
                                continue;
                            }
                            if (!shapeHasAnyCells)
                            {
                                continue;
                            }


                            var placement = new Placement(
                                placementIdCounter++,
                                shapeIndex,
                                shapeInfo.Name,
                                rotIndex,
                                r, c,
                                grid,
                                covered.ToImmutableList()
                            );
                            placements.Add(placement);
                        }
                    }
                }
            }
        }
        return (placements, new Dictionary<int, int>());
    }



    private Dictionary<int, List<int>> DetectCollisions(
    List<ISolveElement> solveElements,
    int gridWidth)
    {
        // Maps cell index (r * gridWidth + c) to list of CNF variable IDs covering it
        var cellCollisions = new Dictionary<int, List<int>>();

        foreach (var element in solveElements)
        {
            int elementVarId = element.VariableId; // Get the variable ID for this element
            if (elementVarId <= 0)
            {
                Debug.WriteLine($"Error: Invalid VariableId {elementVarId} encountered during collision detection.");
                continue; // Skip elements with invalid IDs
            }

            // Get all unique cells covered by this element (handles singletons and groups)
            foreach (var (r, c) in element.GetAllCoveredCells())
            {
                int cellIndex = r * gridWidth + c;
                if (!cellCollisions.TryGetValue(cellIndex, out var varList))
                {
                    varList = new List<int>();
                    cellCollisions[cellIndex] = varList;
                }
                // Add the element's variable ID (not the placement ID)
                if (!varList.Contains(elementVarId))
                {
                    varList.Add(elementVarId);
                }
            }
        }
        return cellCollisions;
    }


    private async Task<(bool IsSat, List<int>? SolutionVariables, string? error)> RunSatSolver(string cnfContent, string? solverArgs = null)
    {
        if (!File.Exists(CryptoMiniSatPath))
        {
            Debug.WriteLine($"Error: SAT Solver not found at '{CryptoMiniSatPath}'");
            return (false, null, $"SAT Solver not found at '{CryptoMiniSatPath}'");
        }

        var processInfo = new ProcessStartInfo
        {
            FileName = CryptoMiniSatPath,
            Arguments = string.IsNullOrWhiteSpace(solverArgs) ? string.Empty : solverArgs,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = Encoding.ASCII,
            StandardOutputEncoding = Encoding.ASCII,
            StandardErrorEncoding = Encoding.ASCII
        };

        using var process = new Process { StartInfo = processInfo };

        try
        {
            process.Start();
            await process.StandardInput.WriteAsync(cnfContent);
            process.StandardInput.Close();

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();
            string output = await outputTask;
            string errorOutput = await errorTask;

            bool logFullOutput = solverArgs?.Contains("-v") ?? false;
            if (logFullOutput)
            {
                Debug.WriteLine($"--- CryptoMiniSat Standard Output ---");
                Debug.WriteLine(output.Split("s SATISFIABLE")[0]);
                Debug.WriteLine($"-------------------------------------");
            }

            if (!string.IsNullOrWhiteSpace(errorOutput))
            {
                Debug.WriteLine($"SAT Solver Error Output:\n{errorOutput}");
            }

            if (output.Contains("s SATISFIABLE"))
            {
                var solutionVars = ParseSolverOutput(output);
                return (true, solutionVars, null);
            }
            else if (output.Contains("s UNSATISFIABLE"))
            {
                return (false, null, null);
            }
            else
            {
                Debug.WriteLine("Warning: Could not determine SAT/UNSAT from solver output.");
                return (false, null, null);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error running SAT solver: {ex.Message}");
            return (false, null, $"{ex.Message}");
        }
    }

    private List<int> ParseSolverOutput(string output)
    {
        var solution = new List<int>();
        using var reader = new StringReader(output);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.StartsWith("v "))
            {
                var parts = line.Split([' '], StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts.Skip(1))
                {
                    if (int.TryParse(part, out int varValue) && varValue != 0)
                    {
                        solution.Add(varValue);
                    }
                }
            }
        }
        return solution.Where(v => v > 0).ToList();
    }

    private ImmutableList<Placement> MapResult(
    List<int> trueVars, // List of TRUE VariableIDs from solver
    Dictionary<int, ISolveElement> variableToObjectMap) // Map VarID -> ISolveElement
    {
        var solutionPlacements = ImmutableList.CreateBuilder<Placement>();

        foreach (int trueVarId in trueVars)
        {
            // Look up the ISolveElement corresponding to the true variable
            if (variableToObjectMap.TryGetValue(trueVarId, out ISolveElement? element))
            {
                // Add ALL placements represented by this element to the final solution
                solutionPlacements.AddRange(element.GetPlacements());
            }
            // else: True variable might be an auxiliary variable (from SeqCounter or Y-linking), ignore it.
        }
        return solutionPlacements.ToImmutable();
    }


    /* 
    Tries to transform a single point according to the specified symmetry type
    relative to the grid center. Accounts for even/odd grid dimensions.
    */
    private static bool TryTransformPoint(
    int r, int c,
    SymmetryType type,
    int gridWidth, int gridHeight,
    out int newR, out int newC)
    {
        newR = r;
        newC = c;

        switch (type)
        {
            case SymmetryType.ReflectHorizontal:
                // Reflect across horizontal center line
                newR = gridHeight - 1 - r;
                break;

            case SymmetryType.ReflectVertical:
                // Reflect across vertical center line
                newC = gridWidth - 1 - c;
                // newR remains r
                break;

            case SymmetryType.Rotate180:
                // Equivalent to ReflectHorizontal then ReflectVertical
                newR = gridHeight - 1 - r;
                newC = gridWidth - 1 - c;
                break;

            case SymmetryType.Rotate90:
                double centerX = (gridWidth - 1.0) / 2.0;
                double centerY = (gridHeight - 1.0) / 2.0;
                double pointX = c + 0.5;
                double pointY = r + 0.5;
                double gridCenterX = centerX + 0.5;
                double gridCenterY = centerY + 0.5;

                // Vector from center to point
                double dx = pointX - gridCenterX;
                double dy = pointY - gridCenterY;

                // Rotated vector (dy, -dx) translated back from center
                double transformedX = gridCenterX + dy;
                double transformedY = gridCenterY - dx;

                // Convert back to integer cell indices (top-left corner) using Floor
                newC = (int)Math.Floor(transformedX);
                newR = (int)Math.Floor(transformedY);

                break;

            case SymmetryType.None:
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(type), "Unsupported symmetry type.");
        }

        // --- Final Bounds Check ---
        bool inBounds = newR >= 0 && newR < gridHeight && newC >= 0 && newC < gridWidth;

        if (!inBounds)
        {
            newR = -1;
            newC = -1;
        }

        return inBounds;
    }

    // Attempts to apply a geometric transformation to a set of covered cells.
    public static bool TryTransformCoveredCells(
        ImmutableList<(int r, int c)> originalCells,
        SymmetryType type,
        int gridWidth,
        int gridHeight,
        HashSet<(int r, int c)> blockedCells, out ImmutableList<(int r, int c)>? transformed)
    {
        transformed = null;
        if (type == SymmetryType.None)
        {
            transformed = originalCells; // No transformation needed
            return true; // Indicate success
        }

        var transformedCells = ImmutableList.CreateBuilder<(int r, int c)>();
        foreach (var (r, c) in originalCells)
        {
            if (!TryTransformPoint(r, c, type, gridWidth, gridHeight, out int newR, out int newC))
            {
                // Transformed point is out of bounds
                // Debug.WriteLine($"Transform failed: Point ({r},{c}) -> ({newR},{newC}) out of bounds.");
                return false;
            }

            var newCell = (newR, newC);
            if (blockedCells.Contains(newCell))
            {
                // Transformed point lands on a blocked cell
                // Debug.WriteLine($"Transform failed: Point ({r},{c}) -> ({newR},{newC}) is blocked.");
                return false;
            }
            transformedCells.Add(newCell);
        }

        if (transformedCells.Count != originalCells.Count)
        {
            Debug.WriteLine($"Transform failed: Cell count mismatch. Original: {originalCells.Count}, Transformed: {transformedCells.Count}");
            return false;
        }


        transformed = transformedCells.ToImmutable();
        return true;

    }

    private static string GenerateCoordinateKey(IEnumerable<(int r, int c)> cells)
    {
        var sortedCells = cells.OrderBy(cell => cell.r).ThenBy(cell => cell.c);
        return string.Join(";", sortedCells.Select(cell => $"{cell.r},{cell.c}"));
    }

    private static string GeneratePlacementSpecificKey(Placement placement)
    {
        if (placement == null || placement.CoveredCells == null || !placement.CoveredCells.Any())
        {
            return string.Empty;
        }

        var sortedCells = placement.CoveredCells.OrderBy(cell => cell.r).ThenBy(cell => cell.c);
        var sb = new StringBuilder();

        foreach (var (r, c) in sortedCells)
        {
            int pr = r - placement.Row;
            int pc = c - placement.Col;

            if (pr >= 0 && pr < placement.Grid.GetLength(0) && pc >= 0 && pc < placement.Grid.GetLength(1))
            {
                CellTypeInfo cellType = placement.Grid[pr, pc];
                sb.Append($"{r},{c}:{cellType.Name}/{(int)cellType.CurrentRotation};");
            }
            else
            {
                sb.Append($"{r},{c}:ERROR;");
            }
        }
        return sb.ToString();
    }


    private List<ISolveElement> ApplySymmetryAndGroup(
        List<Placement> allPlacements,
        SolveParameters parameters,
        VariableManager varManager,
        out Dictionary<int, ISolveElement> variableToObjectMap)
    {
        variableToObjectMap = new Dictionary<int, ISolveElement>();
        var solveElements = new List<ISolveElement>();
        var assignedPlacementIds = new HashSet<int>();
        var blockedCellsSet = parameters.BlockedCells.ToHashSet();
        var placementLookupByCoords = new Dictionary<string, List<Placement>>();

        int maxPlacementId = allPlacements.Max(p => p.PlacementId);

        // Precompute placement lookup
        foreach (var p in allPlacements)
        {
            // Use the coordinate-only key
            string coordKey = GenerateCoordinateKey(p.CoveredCells);
            if (!placementLookupByCoords.TryGetValue(coordKey, out var list))
            {
                list = new List<Placement>();
                placementLookupByCoords[coordKey] = list;
            }
            list.Add(p);
        }

        var transformsToApply = GetSymmetryTransforms(parameters.SelectedSymmetry);

        foreach (var seedPlacement in allPlacements)
        {
            //Debug.WriteLine($"Processing seed placement {seedPlacement.PlacementId} with {seedPlacement.CoveredCells.Count} covered cells.");

            var currentGroupPlacements = new List<Placement>();
            var visitedPlacementKeysInGroup = new HashSet<string>();
            var queue = new Queue<Placement>();

            string seedKey = GeneratePlacementSpecificKey(seedPlacement);
            if (visitedPlacementKeysInGroup.Add(seedKey)) // Check if visitable
            {
                queue.Enqueue(seedPlacement);
            }
            else
            {
                // should probably not happen
                Debug.WriteLine($"Warning: Seed placement {seedPlacement.PlacementId} specific key already visited?");
                continue;
            }

            while (queue.Count > 0)
            {
                var currentPlacement = queue.Dequeue();
                // Check if already assigned globally before adding to current group list
                // Prevents adding a placement that was already processed as part of a different group's split
                if (assignedPlacementIds.Contains(currentPlacement.PlacementId)) continue;

                currentGroupPlacements.Add(currentPlacement);

                foreach (var transformType in transformsToApply)
                {
                    if (TryTransformCoveredCells(currentPlacement.CoveredCells, transformType, parameters.GridWidth, parameters.GridHeight, blockedCellsSet, out var transformedCells))
                    {
                        string transformedCoordKey = GenerateCoordinateKey(transformedCells!);
                        if (placementLookupByCoords.TryGetValue(transformedCoordKey, out var potentialMatches))
                        {
                            // --- Determine rotation index of the partner ---
                            RotationDirection currentDirection = GetRotationDirectionFromIndex(currentPlacement.RotationIndex);
                            RotationDirection expectedPartnerDirection = GetExpectedPartnerRotation(currentDirection, transformType);
                            int expectedPartnerRotIdx = GetRotationIndexFromDirection(expectedPartnerDirection);

                            // --- Filter potential matches ---
                            var correctPartner = potentialMatches.FirstOrDefault(cand =>
                                GetRotationDirectionFromIndex(cand.RotationIndex) == expectedPartnerDirection);

                            if (correctPartner != null) // Found correct partner
                            {
                                if (!assignedPlacementIds.Contains(correctPartner.PlacementId))
                                {
                                    string partnerSpecificKey = GeneratePlacementSpecificKey(correctPartner);
                                    if (visitedPlacementKeysInGroup.Add(partnerSpecificKey))
                                    {
                                        queue.Enqueue(correctPartner);
                                    }
                                }
                            }
                        }
                    }

                }
            }

            // --- Group found, check consistency ---
            bool isInternallyConsistent = true;
            if (currentGroupPlacements.Count > 1)
            {
                if (currentGroupPlacements.Count > 2)
                {
                    Debug.WriteLine($"Found larger group");
                }
                var allCellsInGroup = currentGroupPlacements
                                        .SelectMany(p => p.CoveredCells)
                                        .ToImmutableHashSet();

                foreach (var (r, c) in allCellsInGroup)
                {
                    var nonEmptiesAtCell = new List<(Placement p, CellTypeInfo type)>();
                    foreach (var p in currentGroupPlacements)
                    {
                        int pr = r - p.Row;
                        int pc = c - p.Col;
                        if (pr >= 0 && pr < p.Grid.GetLength(0) && pc >= 0 && pc < p.Grid.GetLength(1))
                        {
                            CellTypeInfo type = p.Grid[pr, pc];
                            if (!type.IsEmpty)
                            {
                                nonEmptiesAtCell.Add((p, type));
                            }
                        }
                    }

                    if (nonEmptiesAtCell.Count > 1)
                    {
                        var typeCounts = new Dictionary<CellTypeInfo, int>(new CellTypeInfoComparer());
                        foreach (var (_, type) in nonEmptiesAtCell)
                        {
                            typeCounts.TryGetValue(type, out int currentCount);
                            typeCounts[type] = currentCount + 1;
                        }

                        // --- Multiple contributions of same non-self-intersecting type ---
                        foreach (var kvp in typeCounts)
                        {
                            if (kvp.Value > 1 && !kvp.Key.CanSelfIntersect)
                            {
                                isInternallyConsistent = false;
                                Debug.WriteLine($"Symmetry Group Inconsistency at ({r},{c}): {kvp.Value} placements contribute the same non-self-intersecting type '{kvp.Key.Name}/{kvp.Key.CurrentRotation}'. Seed: {seedPlacement.PlacementId}");
                                break; // Conflict found for this cell (same type)
                            }
                        }
                        if (!isInternallyConsistent) break; // Conflict found, stop checking cells for this group

                        if (typeCounts.Count > 1) // More than one unique CellTypeInfo present
                        {
                            // Conflict if any involved type cannot self-intersect or if multiple names are present.
                            bool differentTypeConflictFound = false;
                            var uniqueNames = new HashSet<string>();
                            foreach (var uniqueType in typeCounts.Keys)
                            {
                                uniqueNames.Add(uniqueType.Name);
                                if (!uniqueType.CanSelfIntersect)
                                {
                                    differentTypeConflictFound = true;
                                    break; // Found a non-intersecting type in a multi-type overlap
                                }
                            }


                            if (!differentTypeConflictFound && uniqueNames.Count > 1)
                            {
                                differentTypeConflictFound = true;
                            }

                            if (differentTypeConflictFound)
                            {
                                isInternallyConsistent = false;
                                // Debug.WriteLine($"Symmetry Group Inconsistency at ({r},{c}): Multiple different types overlap. Types: {string.Join(", ", typeCounts.Keys.Select(t => $"{t.Name}/{t.CurrentRotation}"))}. Seed: {seedPlacement.PlacementId}");
                                break;
                            }
                        }
                    }
                    if (!isInternallyConsistent) break;
                }
            }

            // --- Add elements based on consistency ---
            if (isInternallyConsistent)
            {
                // Consistent group: Assign one VariableId for the element (group or single)
                int variableId = varManager.GetNextVariable();
                ISolveElement elementToAdd;

                if (currentGroupPlacements.Count == 1)
                {
                    var element = currentGroupPlacements[0];
                    element.VariableId = variableId;
                    elementToAdd = element;
                }
                else
                {
                    elementToAdd = new SymmetryGroup(variableId, currentGroupPlacements.ToImmutableList());
                }

                // Add the consistent element and mark its placements as globally assigned
                solveElements.Add(elementToAdd);
                variableToObjectMap.Add(variableId, elementToAdd);
                foreach (var p in currentGroupPlacements) { assignedPlacementIds.Add(p.PlacementId); }
            }
            else
            {
                if (parameters.UseSoftSymmetry) // Check symmetry mode
                {
                    // Soft Mode: Split into individual placements on inconsistent groups
                    Debug.WriteLine($"Splitting inconsistent group (Soft Symmetry Enabled) starting with seed {seedPlacement.PlacementId}...");
                    foreach (var placement in currentGroupPlacements)
                    {
                        if (assignedPlacementIds.Contains(placement.PlacementId)) continue;
                        int individualVariableId = varManager.GetNextVariable();
                        placement.VariableId = individualVariableId;
                        solveElements.Add(placement);
                        variableToObjectMap.Add(individualVariableId, placement);
                        assignedPlacementIds.Add(placement.PlacementId);
                    }
                }
                else
                {
                    // Hard Mode: Discard the entire inconsistent group
                    foreach (var p in currentGroupPlacements)
                    {
                        assignedPlacementIds.Add(p.PlacementId);
                    }
                }
            }

        }

        //Debug.WriteLine($"Grouping resulted in {solveElements.Count} elements (valid groups/singletons/split individuals).");

        foreach (var element in solveElements)
        {
            if (element.VariableId <= 0)
            {
                Debug.WriteLine($"CRITICAL ERROR: Element found in solveElements without a valid VariableId! Type: {element.GetType().Name}");
                // Placement added directly without getting an ID assigned or SymmetryGroup constructor issue
            }
            // Check mapping consistency
            if (!variableToObjectMap.ContainsKey(element.VariableId))
            {
                Debug.WriteLine($"CRITICAL ERROR: Element {element.VariableId} in solveElements but not in variableToObjectMap!");
            }
        }
        if (solveElements.Count != variableToObjectMap.Count)
        {
            Debug.WriteLine($"CRITICAL ERROR: Mismatch between solveElements count ({solveElements.Count}) and variableToObjectMap count ({variableToObjectMap.Count})");
        }

        return solveElements;
    }

    // Helper to get the basic symmetry operations required based on the user selection string.
    private List<SymmetryType> GetSymmetryTransforms(SelectedSymmetryType selectedSymmetry)
    {
        switch (selectedSymmetry)
        {
            case SelectedSymmetryType.Rotational180:
                return new List<SymmetryType> { SymmetryType.Rotate180 };
            case SelectedSymmetryType.Rotational90:
                // Rotating by 90 degrees generates 180 and 270 as well.
                return new List<SymmetryType> { SymmetryType.Rotate90 };
            case SelectedSymmetryType.Horizontal:
                return new List<SymmetryType> { SymmetryType.ReflectHorizontal };
            case SelectedSymmetryType.Vertical:
                return new List<SymmetryType> { SymmetryType.ReflectVertical };
            case SelectedSymmetryType.Quadrants:
                return new List<SymmetryType> { SymmetryType.ReflectHorizontal, SymmetryType.ReflectVertical };
            case SelectedSymmetryType.None:
            default:
                return new List<SymmetryType> { SymmetryType.None };
        }

    }

    private bool TryGetCellTypeForElementAt(ISolveElement element, int r, int c, out CellTypeInfo? cellType)
    {
        cellType = null;
        // An element might cover (r,c) w any of its placements
        foreach (var placement in element.GetPlacements())
        {
            // Calculate relative coordinates within this placement's grid
            int pr = r - placement.Row;
            int pc = c - placement.Col;
            int pHeight = placement.Grid.GetLength(0);
            int pWidth = placement.Grid.GetLength(1);

            // Check if (r,c) falls within the bounds of this placement's grid
            if (pr >= 0 && pr < pHeight && pc >= 0 && pc < pWidth)
            {
                var currentCellType = placement.Grid[pr, pc];
                // Check if the cell type at this relative position is non-empty
                if (!currentCellType.IsEmpty)
                {
                    // Placement contributes the non-empty cell type at (r,c)
                    cellType = currentCellType;
                    return true;
                }
                // If empty here, placement doesn't define the type at (r,c) - continue
            }
        }
        // If no placement within the element has non-empty cell at (r,c), element doesn't actually define the type there
        return false;
    }


    private RotationDirection GetExpectedPartnerRotation(RotationDirection currentRotation, SymmetryType transform)
    {
        switch (transform)
        {
            case SymmetryType.ReflectVertical:
                return currentRotation switch
                {
                    RotationDirection.North => RotationDirection.North, // Stays North
                    RotationDirection.East => RotationDirection.West,   // East -> West
                    RotationDirection.South => RotationDirection.South, // Stays South
                    RotationDirection.West => RotationDirection.East,   // West -> East
                    _ => currentRotation
                };
            case SymmetryType.ReflectHorizontal:
                return currentRotation switch
                {
                    RotationDirection.North => RotationDirection.South, // North -> South
                    RotationDirection.East => RotationDirection.East,   // Stays East
                    RotationDirection.South => RotationDirection.North, // South -> North
                    RotationDirection.West => RotationDirection.West,   // Stays West
                    _ => currentRotation
                };
            case SymmetryType.Rotate180:
                return (RotationDirection)(((int)currentRotation + 2) % 4);
            case SymmetryType.Rotate90:
                return (RotationDirection)(((int)currentRotation + 1) % 4);
            case SymmetryType.None:
            default:
                return currentRotation;
        }
    }

    private RotationDirection GetRotationDirectionFromIndex(int rotationIndex)
    {
        if (rotationIndex >= 0 && rotationIndex < 4)
        {
            return (RotationDirection)rotationIndex;
        }
        return RotationDirection.North;
    }

    // Helper to get RotationIndex from RotationDirection
    private int GetRotationIndexFromDirection(RotationDirection direction)
    {
        return (int)direction;
    }




}