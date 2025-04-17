using System.Diagnostics;
using System.Text;
using APS_Optimizer_V3.Helpers;
using APS_Optimizer_V3.ViewModels;

namespace APS_Optimizer_V3.Services;

public class SolverService
{
    private const string CryptoMiniSatPath = "cryptominisat5.exe";

    public async Task<SolverResult> SolveAsync(SolveParameters parameters)
    {
        var stopwatch = Stopwatch.StartNew();
        Debug.WriteLine($"Solver started. Grid: {parameters.GridWidth}x{parameters.GridHeight}, Shapes: {parameters.EnabledShapes.Count}, Symmetry: {parameters.SelectedSymmetry}");

        var iterationLogs = new List<SolverIterationLog>();

        // 1. Generate all possible valid placements (raw)
        var (allPlacements, _) = GeneratePlacements(parameters); // Don't need placementVarMap from here anymore
        if (!allPlacements.Any())
        {
            return new SolverResult(false, "No valid placements possible for any shape.", 0, null, null);
        }
        Debug.WriteLine($"Generated {allPlacements.Count} raw placements.");

        // 2. Apply Symmetry and Group Placements -> Get ISolveElements
        VariableManager varManager = new VariableManager();
        var solveElements = ApplySymmetryAndGroup(
            allPlacements,
            parameters,
            varManager,
            out var variableToObjectMap // Gets populated by ApplySymmetryAndGroup
        );

        if (!solveElements.Any()) // Should not happen if allPlacements was not empty, but check anyway
        {
            return new SolverResult(false, "Grouping resulted in zero elements.", 0, null, null);
        }


        // 3. Detect Collisions (using ISolveElements)
        var cellCollisions = DetectCollisions(solveElements, parameters.GridWidth, parameters.GridHeight);
        Debug.WriteLine($"Detected collisions across {cellCollisions.Count} cells.");

        /*
        // 4. Generate Base CNF (Collision Constraints)
        var baseClauses = new List<List<int>>();
        foreach (var kvp in cellCollisions)
        {
            var collidingVars = kvp.Value; // These are VariableIDs of ISolveElements
            if (collidingVars.Count > 1)
            {
                var (atMostClauses, _) = SequentialCounter.EncodeAtMostK(collidingVars, 1, varManager);
                baseClauses.AddRange(atMostClauses);
            }
        }

        Debug.WriteLine($"Generated {baseClauses.Count} base clauses for collisions. Max Var ID so far: {varManager.GetMaxVariableId()}");*/

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
                // Use HashSet for efficient intersection lookup if performance is critical
                var e1Cells = e1.GetAllCoveredCells(); // Consider caching this if called often
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

                    // Check for a "hard" conflict based on CanSelfIntersect and Name
                    bool isConflictAtCell = !type1.CanSelfIntersect ||   // Type 1 doesn't allow self-intersection
                                            !type2.CanSelfIntersect ||   // Type 2 doesn't allow self-intersection
                                            type1.Name != type2.Name;    // Types are different (self-intersection only for same type)

                    if (isConflictAtCell)
                    {
                        hardConflictFound = true;
                        break; // Found one hard conflict cell, no need to check others for this pair
                    }
                    // else: This specific cell (r,c) is not a hard conflict (e.g., both are the same Cooler type)
                }

                // If a hard conflict was found at *any* overlapping cell, add the exclusion clause
                if (hardConflictFound)
                {
                    conflictClauses.Add(new List<int> { -v1, -v2 });
                }
            }
        }
        Debug.WriteLine($"Generated {conflictClauses.Count} pairwise conflict clauses. Max Var ID so far: {varManager.GetMaxVariableId()}");


        // 5. Iterative Solving Loop (using GCD)
        // ... (Calculate GCD, set initial requiredCells - NO CHANGES here) ...
        var shapeAreas = parameters.EnabledShapes
        .Select(s => s.GetArea()) // Use the GetArea method from ShapeViewModel
        .Where(area => area > 0)
        .Distinct()
        .ToList();
        if (!shapeAreas.Any()) { return new SolverResult(false, "...", 0, null, null); }
        int decrementStep = parameters.EnabledShapes.Any(s => s.CouldSelfIntersect()) ? 1 : CalculateListGcd(shapeAreas);
        int totalAvailableCells = parameters.GridWidth * parameters.GridHeight - parameters.BlockedCells.Count;
        int requiredCells = totalAvailableCells / decrementStep * decrementStep;
        int iterationCounter = 0;

        while (requiredCells >= 0)
        {
            iterationCounter++;
            Debug.WriteLine($"Attempting to solve for at least {requiredCells} covered cells.");
            var currentClauses = new List<List<int>>(conflictClauses);
            var currentAuxVarManager = new VariableManager();
            while (currentAuxVarManager.GetMaxVariableId() < varManager.GetMaxVariableId()) { currentAuxVarManager.GetNextVariable(); }

            // 6. Generate Coverage Constraint CNF (using Y variables)
            // --- This part needs updating to link Y vars to ISolveElement vars ---
            int[,] yVars = new int[parameters.GridHeight, parameters.GridWidth];
            var yVarLinkClauses = new List<List<int>>();
            var yVarList = new List<int>();

            for (int r = 0; r < parameters.GridHeight; r++)
            {
                for (int c = 0; c < parameters.GridWidth; c++)
                {
                    if (parameters.BlockedCells.Contains((r, c))) { yVars[r, c] = 0; continue; }

                    // Use the aux manager for Y vars
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
            // --- End Y Variable Update ---


            // Encode Sum(Y_vars) >= requiredCells
            // ... (AtLeastK/AtMostK encoding remains the same, using yVarList) ...
            var (coverageClausesList, _) = SequentialCounter.EncodeAtLeastK(yVarList, requiredCells, currentAuxVarManager);
            currentClauses.AddRange(coverageClausesList);


            // 7. Finalize CNF and Run Solver
            int totalVars = currentAuxVarManager.GetMaxVariableId();
            string finalCnfString = FormatDimacs(currentClauses, totalVars);
            // File.WriteAllText($"debug_solver_input_{requiredCells}.cnf", finalCnfString);
            var iterationStopwatch = Stopwatch.StartNew();
            var threads = Math.Max(Environment.ProcessorCount - 1, 1);
            var (sat, solutionVars) = await RunSatSolver(finalCnfString, $"--threads {threads}");
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

            // 8. Process Result
            if (sat && solutionVars != null)
            {
                stopwatch.Stop();
                Debug.WriteLine($"SATISFIABLE found for >= {requiredCells} cells. Time: {stopwatch.ElapsedMilliseconds} ms");

                // *** UPDATE MAPPING TO USE ISolveElement ***
                var solutionPlacements = MapResult(solutionVars, variableToObjectMap);
                return new SolverResult(true, $"Solution found covering at least {requiredCells} cells.", requiredCells, solutionPlacements, iterationLogs.ToImmutableList());
            }
            else
            {
                // ... (UNSAT handling remains the same, decrement by GCD) ...
                Debug.WriteLine($"UNSATISFIABLE for >= {requiredCells} cells.");
                if (requiredCells == 0) break;
                requiredCells -= decrementStep;
                if (requiredCells < 0) requiredCells = 0;
            }
        } // End while loop

        // ... (No solution found handling) ...
        stopwatch.Stop();
        return new SolverResult(false, "No solution found for any possible coverage.", 0, null, null);
    }


    // --- Helper to Format DIMACS String ---
    private string FormatDimacs(List<List<int>> clauses, int variableCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"p cnf {variableCount} {clauses.Count}");
        foreach (var clause in clauses)
        {
            // Check if clause is empty which is invalid DIMACS, though shouldn't happen with correct logic
            if (clause.Any())
            {
                sb.Append(string.Join(" ", clause));
                sb.AppendLine(" 0");
            }
            else
            {
                Debug.WriteLine("Warning: Encountered empty clause during DIMACS formatting.");
                // Decide how to handle: skip, add dummy (like '1 -1 0'), or throw error.
                // Skipping for now.
            }
        }
        return sb.ToString();
    }


    // --- Other Helper Methods (GeneratePlacements, DetectCollisions, RunSatSolver, ParseSolverOutput, MapResult) ---
    // These methods remain largely the same as before, only the inputs/outputs
    // related to CNF generation within SolveAsync have changed.
    // Make sure DetectCollisions uses the correct placementVarMap keys (PlacementId).

    private (List<Placement>, Dictionary<int, int>) GeneratePlacements(SolveParameters parameters)
    {
        var placements = new List<Placement>();
        // var placementVarMap = new Dictionary<int, int>(); // Not needed here anymore
        int placementIdCounter = 0;
        var blockedSet = parameters.BlockedCells.ToHashSet();

        for (int shapeIndex = 0; shapeIndex < parameters.EnabledShapes.Count; shapeIndex++)
        {
            var shapeInfo = parameters.EnabledShapes[shapeIndex];
            var rotations = shapeInfo.GetAllRotationGrids(); // Gets List<CellType[,]>

            for (int rotIndex = 0; rotIndex < rotations.Count; rotIndex++)
            {
                var grid = rotations[rotIndex]; // grid is CellType[,]
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
                            // *** CORRECTED CHECK for empty shapes ***
                            bool shapeHasAnyCells = false;
                            foreach (CellTypeInfo cellType in grid) // Iterate grid directly
                            {
                                if (!cellType.IsEmpty)
                                {
                                    shapeHasAnyCells = true;
                                    break;
                                }
                            }

                            // Check if the shape definition had cells but none were placed (shouldn't happen if isValid)
                            if (shapeHasAnyCells && !covered.Any())
                            {
                                Debug.WriteLine($"Warning: Placement deemed valid but covered list is empty. Shape: {shapeInfo.Name}, Pos ({r},{c})");
                                continue; // Skip this potentially problematic placement
                            }
                            // Also skip if the shape definition itself was empty
                            if (!shapeHasAnyCells)
                            {
                                continue;
                            }
                            // *** END CORRECTION ***


                            var placement = new Placement(
                                placementIdCounter++,
                                shapeIndex,
                                shapeInfo.Name,
                                rotIndex,
                                r, c,
                                grid, // Pass the CellType[,] grid
                                covered.ToImmutableList()
                            );
                            placements.Add(placement);
                        }
                    }
                }
            }
        }
        // Return empty dictionary as var map is assigned later
        return (placements, new Dictionary<int, int>());
    }



    private Dictionary<int, List<int>> DetectCollisions(
    List<ISolveElement> solveElements, // Accept list of elements
    int gridWidth, int gridHeight)
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


    private async Task<(bool IsSat, List<int>? SolutionVariables)> RunSatSolver(string cnfContent, string? solverArgs = null)
    {
        if (!File.Exists(CryptoMiniSatPath))
        {
            Debug.WriteLine($"Error: SAT Solver not found at '{CryptoMiniSatPath}'");
            return (false, null);
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
            StandardInputEncoding = Encoding.ASCII, // Explicitly set encoding
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
                Debug.WriteLine(output.Split("s SATISFIABLE")[0]); // Log the full output
                Debug.WriteLine($"-------------------------------------");
            }

            if (!string.IsNullOrWhiteSpace(errorOutput))
            {
                Debug.WriteLine($"SAT Solver Error Output:\n{errorOutput}");
            }

            if (output.Contains("s SATISFIABLE"))
            {
                var solutionVars = ParseSolverOutput(output);
                return (true, solutionVars);
            }
            else if (output.Contains("s UNSATISFIABLE"))
            {
                return (false, null);
            }
            else
            {
                Debug.WriteLine("Warning: Could not determine SAT/UNSAT from solver output.");
                return (false, null);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error running SAT solver: {ex.Message}");
            return (false, null);
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
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
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

        // Remove duplicates if multiple groups somehow contained the same placement instance (shouldn't happen with correct grouping)
        // return solutionPlacements.ToImmutableHashSet(new PlacementComparer()).ToImmutableList(); // Requires a custom comparer
        // Or simply return as is, assuming grouping is correct:
        return solutionPlacements.ToImmutable();
    }


    /// <summary>
    /// Tries to transform a single point according to the specified symmetry type
    /// relative to the grid center. Accounts for even/odd grid dimensions.
    /// </summary>
    /// <returns>True if the transformed point is within grid bounds, false otherwise.</returns>
    private static bool TryTransformPoint(
    int r, int c,           // Original cell coordinates
    SymmetryType type,
    int gridWidth, int gridHeight, // Use integer dimensions directly
    out int newR, out int newC)
    {
        // Initialize to original coordinates
        newR = r;
        newC = c;

        switch (type)
        {
            case SymmetryType.ReflectHorizontal:
                // Reflect across horizontal center line
                // Formula: new = (Dimension - 1) - old
                newR = (gridHeight - 1) - r;
                // newC remains c
                break;

            case SymmetryType.ReflectVertical:
                // Reflect across vertical center line
                // Formula: new = (Dimension - 1) - old
                newC = (gridWidth - 1) - c;
                // newR remains r
                break;

            case SymmetryType.Rotate180:
                // Equivalent to ReflectHorizontal then ReflectVertical
                newR = (gridHeight - 1) - r;
                newC = (gridWidth - 1) - c;
                break;

            case SymmetryType.Rotate90:
                // Keep using floating-point for 90-degree rotation for now,
                // as pure integer grid-based rotation around a center is complex.
                // Be mindful of potential precision issues near edges/center.
                double centerX = (gridWidth - 1.0) / 2.0;   // Geometric center X index
                double centerY = (gridHeight - 1.0) / 2.0;  // Geometric center Y index
                double pointX = c + 0.5;                    // Center of original cell X
                double pointY = r + 0.5;                    // Center of original cell Y
                double gridCenterX = centerX + 0.5;         // Geometric center X coordinate
                double gridCenterY = centerY + 0.5;         // Geometric center Y coordinate

                // Vector from center to point
                double dx = pointX - gridCenterX;
                double dy = pointY - gridCenterY;

                // Rotated vector (dy, -dx) translated back from center
                double transformedX = gridCenterX + dy;
                double transformedY = gridCenterY - dx;

                // Convert back to integer cell indices (top-left corner) using Floor
                // Floor is generally correct for mapping a coordinate to the grid cell index it falls within.
                newC = (int)Math.Floor(transformedX);
                newR = (int)Math.Floor(transformedY);

                // --- Debug Logging Placeholder ---
                // if (/* condition for suspect points, e.g., near edge */) {
                //     Debug.WriteLine($"Rotate90: ({r},{c}) -> pt({pointX:F2},{pointY:F2}) | center({gridCenterX:F2},{gridCenterY:F2}) | d({dx:F2},{dy:F2}) -> tf({transformedX:F4},{transformedY:F4}) -> floor({newR},{newC})");
                // }
                // --- End Debug ---
                break;

            case SymmetryType.None:
                // No change needed, newR/newC already initialized to r/c
                break;

            default:
                // Should not happen if enum is exhaustive
                throw new ArgumentOutOfRangeException(nameof(type), "Unsupported symmetry type.");
        }

        // --- Final Bounds Check ---
        // This check is crucial regardless of integer or float calculation.
        bool inBounds = newR >= 0 && newR < gridHeight && newC >= 0 && newC < gridWidth;

        if (!inBounds)
        {
            // If out of bounds, reset to invalid coordinates to signal failure clearly
            newR = -1;
            newC = -1;
            // Debug.WriteLine($"Transform failed bounds check: Type={type}, Original=({r},{c}), Attempted=({newR_before_reset},{newC_before_reset}), Grid=({gridWidth}x{gridHeight})");
        }

        return inBounds;
    }
    /// <summary>
    /// Attempts to apply a geometric transformation to a set of covered cells.
    /// Checks if all transformed cells are within bounds and not blocked.
    /// </summary>
    /// <param name="originalCells">The original set of absolute cell coordinates.</param>
    /// <param name="type">The transformation to apply.</param>
    /// <param name="gridWidth">Width of the main grid.</param>
    /// <param name="gridHeight">Height of the main grid.</param>
    /// <param name="blockedCells">A set of blocked cell coordinates for quick lookup.</param>
    /// <returns>A new list of transformed cell coordinates if successful and valid, otherwise null.</returns>
    public static ImmutableList<(int r, int c)>? TryTransformCoveredCells(
        ImmutableList<(int r, int c)> originalCells,
        SymmetryType type,
        int gridWidth,
        int gridHeight,
        HashSet<(int r, int c)> blockedCells) // Pass HashSet for efficiency
    {
        if (type == SymmetryType.None) return originalCells; // No transformation needed

        var transformedCells = ImmutableList.CreateBuilder<(int r, int c)>();
        foreach (var (r, c) in originalCells)
        {
            if (!TryTransformPoint(r, c, type, gridWidth, gridHeight, out int newR, out int newC))
            {
                // Transformed point is out of bounds
                // Debug.WriteLine($"Transform failed: Point ({r},{c}) -> ({newR},{newC}) out of bounds.");
                return null;
            }

            var newCell = (newR, newC);
            if (blockedCells.Contains(newCell))
            {
                // Transformed point lands on a blocked cell
                Debug.WriteLine($"Transform failed: Point ({r},{c}) -> ({newR},{newC}) is blocked.");
                return null;
            }
            transformedCells.Add(newCell);
        }

        // Important: Check if the number of cells is the same. Transformations should be bijective.
        if (transformedCells.Count != originalCells.Count)
        {
            Debug.WriteLine($"Transform failed: Cell count mismatch. Original: {originalCells.Count}, Transformed: {transformedCells.Count}");
            // This might happen if multiple original cells map to the same transformed cell,
            // although unlikely with standard reflections/rotations.
            return null;
        }


        // Return the immutable list of transformed cells
        return transformedCells.ToImmutable();
    }

    private static string GenerateCoordinateKey(IEnumerable<(int r, int c)> cells)
    {
        // Sort by row, then column to ensure consistent ordering
        var sortedCells = cells.OrderBy(cell => cell.r).ThenBy(cell => cell.c);
        // Create a unique string representation
        return string.Join(";", sortedCells.Select(cell => $"{cell.r},{cell.c}"));
    }

    private static string GeneratePlacementSpecificKey(Placement placement)
    {
        if (placement == null || placement.CoveredCells == null || !placement.CoveredCells.Any())
        {
            return string.Empty; // Or handle as an error
        }

        var sortedCells = placement.CoveredCells.OrderBy(cell => cell.r).ThenBy(cell => cell.c);
        var sb = new StringBuilder();
        //sb.Append($"P({placement.PlacementId})_"); // Optional: Include PlacementID for debugging keys

        foreach (var (r, c) in sortedCells)
        {
            // Calculate relative coordinates to look up in the placement's grid
            int pr = r - placement.Row;
            int pc = c - placement.Col;

            // Basic bounds check (should be valid if CoveredCells is correct)
            if (pr >= 0 && pr < placement.Grid.GetLength(0) && pc >= 0 && pc < placement.Grid.GetLength(1))
            {
                CellTypeInfo cellType = placement.Grid[pr, pc];
                // Include coordinates, type name, and rotation in the key part for this cell
                sb.Append($"{r},{c}:{cellType.Name}/{(int)cellType.CurrentRotation};"); // Using int value of enum
            }
            else
            {
                // This indicates an inconsistency between CoveredCells and the Grid/Row/Col
                sb.Append($"{r},{c}:ERROR;");
                Debug.WriteLine($"Warning: Inconsistency generating specific key for Placement {placement.PlacementId}. Cell ({r},{c}) not found in grid relative coords ({pr},{pc}).");
            }
        }
        return sb.ToString();
    }


    private List<ISolveElement> ApplySymmetryAndGroup(
    List<Placement> allPlacements,
    SolveParameters parameters,
    VariableManager varManager,
    out Dictionary<int, ISolveElement> variableToObjectMap) // No inconsistentGroupVars output
    {
        variableToObjectMap = new Dictionary<int, ISolveElement>();
        var solveElements = new List<ISolveElement>();
        var assignedPlacementIds = new HashSet<int>();
        var blockedCellsSet = parameters.BlockedCells.ToHashSet();
        var placementLookupByCoords = new Dictionary<string, List<Placement>>();


        // Precompute placement lookup
        foreach (var p in allPlacements)
        {
            // Use the coordinate-only key here
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
            if (assignedPlacementIds.Contains(seedPlacement.PlacementId)) continue; // Already processed

            var currentGroupPlacements = new List<Placement>();
            // --- Use Placement-Specific Key for Visited Tracking within a group search ---
            var visitedPlacementKeysInGroup = new HashSet<string>();
            var queue = new Queue<Placement>();

            string seedKey = GeneratePlacementSpecificKey(seedPlacement);
            if (visitedPlacementKeysInGroup.Add(seedKey)) // Check if visitable
            {
                queue.Enqueue(seedPlacement);
            }
            else
            {
                // This should technically not happen if seed hasn't been assigned
                Debug.WriteLine($"Warning: Seed placement {seedPlacement.PlacementId} specific key already visited?");
                continue;
            }

            while (queue.Count > 0)
            {
                var currentPlacement = queue.Dequeue();
                // Check if already assigned *globally* before adding to current group list
                // This prevents adding a placement that was already processed as part of a *different* group's split
                if (assignedPlacementIds.Contains(currentPlacement.PlacementId)) continue;

                currentGroupPlacements.Add(currentPlacement);
                // Mark as assigned globally *only if it will be added to the results* (either group or split)
                // We defer the global assignment until after the consistency check.

                foreach (var transformType in transformsToApply)
                {
                    var transformedCells = TryTransformCoveredCells(currentPlacement.CoveredCells, transformType, parameters.GridWidth, parameters.GridHeight, blockedCellsSet);
                    if (transformedCells != null)
                    {
                        // 2. Find potential matches by COORDINATE footprint
                        string transformedCoordKey = GenerateCoordinateKey(transformedCells);
                        if (placementLookupByCoords.TryGetValue(transformedCoordKey, out var potentialMatches))
                        {
                            // 3. Check each potential match
                            foreach (var candidatePlacement in potentialMatches)
                            {
                                // Check if globally assigned
                                if (assignedPlacementIds.Contains(candidatePlacement.PlacementId)) continue;

                                // *** Crucial Check: Use Placement-Specific Key for Visited ***
                                string candidateSpecificKey = GeneratePlacementSpecificKey(candidatePlacement);
                                if (visitedPlacementKeysInGroup.Add(candidateSpecificKey))
                                {
                                    // This specific placement (coords + types/rotations)
                                    // hasn't been visited *in this group search* yet.
                                    // Assume it's the correct symmetric partner if found via coords.
                                    // (Relies on GeneratePlacements creating all valid possibilities)
                                    queue.Enqueue(candidatePlacement);
                                }
                            }
                        }
                    }

                }
            } // End while queue

            // --- Group found (currentGroupPlacements), now check consistency ---
            bool isInternallyConsistent = true;
            if (currentGroupPlacements.Count > 1)
            {
                // Find all unique cells covered by *any* placement in this potential group
                var allCellsInGroup = currentGroupPlacements
                                        .SelectMany(p => p.CoveredCells)
                                        .ToImmutableHashSet();

                foreach (var (r, c) in allCellsInGroup)
                {
                    // Find all non-empty contributions to this cell from placements in the group
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

                    if (nonEmptiesAtCell.Count <= 1) continue; // No conflict possible with <= 1 non-empty contributor

                    // --- Identify UNIQUE CellTypeInfos at this cell ---
                    // We only care about the distinct types present, ignoring duplicate identical contributions.
                    // Use a HashSet based on Name and CurrentRotation for uniqueness.
                    var uniqueTypesAtCell = new HashSet<CellTypeInfo>(new CellTypeInfoComparer()); // Need a comparer
                    foreach (var (_, type) in nonEmptiesAtCell)
                    {
                        uniqueTypesAtCell.Add(type);
                    }

                    // If only one UNIQUE type is present (even if multiple placements contribute it), no conflict AT THIS CELL.
                    if (uniqueTypesAtCell.Count <= 1) continue;

                    // --- Now check for conflicts among the UNIQUE types ---
                    bool hardConflictAmongUnique = false;
                    var uniqueTypeNames = new HashSet<string>();

                    foreach (var uniqueType in uniqueTypesAtCell)
                    {
                        uniqueTypeNames.Add(uniqueType.Name);
                        if (!uniqueType.CanSelfIntersect)
                        {
                            // If ANY unique type here is non-intersecting, it's a conflict
                            // because we already know there's more than one unique type present.
                            hardConflictAmongUnique = true;
                            break; // Found a non-intersecting type among multiple unique types
                        }
                    }

                    // Check conflict conditions for this cell based on UNIQUE types
                    // Conflict exists if a non-intersecting type was found OR if multiple different self-intersecting type names exist.
                    if (hardConflictAmongUnique || uniqueTypeNames.Count > 1)
                    {
                        isInternallyConsistent = false;
                        var involvedPlacementIds = nonEmptiesAtCell.Select(pair => pair.p.PlacementId).Distinct();
                        Debug.WriteLine($"Symmetry Group Inconsistency Detected at cell ({r},{c}): Conflict among unique non-empty types. Group originating from {seedPlacement.PlacementId}. Involved Placements: {string.Join(", ", involvedPlacementIds)} Unique Types: {string.Join(", ", uniqueTypesAtCell.Select(t => $"{t.Name}/{t.CurrentRotation}"))}");
                        break; // Found inconsistency, no need to check other cells
                    }
                    // else: All unique non-empty types covering this cell are the *same* self-intersecting type - ok for this cell.

                } // End foreach cell in group
            } // End if currentGroupPlacements.Count > 1


            // --- Add elements based on consistency ---
            if (isInternallyConsistent)
            {
                // Consistent group: Assign ONE VariableId for the element (group or singleton)
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
                if (parameters.UseSoftSymmetry) // Check the boolean parameter
                {
                    // Soft Mode: Split into individual placements
                    Debug.WriteLine($"Splitting inconsistent group (Soft Symmetry Enabled) starting with seed {seedPlacement.PlacementId}...");
                    foreach (var placement in currentGroupPlacements)
                    {
                        // ... (logic remains the same: assign individual ID, add placement, mark assigned) ...
                        if (assignedPlacementIds.Contains(placement.PlacementId)) continue;
                        int individualVariableId = varManager.GetNextVariable();
                        placement.VariableId = individualVariableId;
                        solveElements.Add(placement);
                        variableToObjectMap.Add(individualVariableId, placement);
                        assignedPlacementIds.Add(placement.PlacementId);
                    }
                }
                else // Hard Mode (UseSoftSymmetry is false)
                {
                    // Hard Mode: Discard the entire inconsistent group
                    Debug.WriteLine($"Discarding inconsistent group (Soft Symmetry Disabled) starting with seed {seedPlacement.PlacementId}.");
                    // Mark placements as assigned so they aren't picked up again
                    foreach (var p in currentGroupPlacements) { assignedPlacementIds.Add(p.PlacementId); }
                    // Do NOT add anything to solveElements or variableToObjectMap
                }
            }

        } // End foreach seedPlacement

        Debug.WriteLine($"Grouping resulted in {solveElements.Count} elements (valid groups/singletons/split individuals).");

        foreach (var element in solveElements)
        {
            if (element.VariableId <= 0)
            {
                Debug.WriteLine($"CRITICAL ERROR: Element found in solveElements without a valid VariableId! Type: {element.GetType().Name}");
                // This might indicate a Placement added directly without getting an ID assigned,
                // or a SymmetryGroup constructor issue.
            }
            // Also check mapping consistency
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


    /// <summary>
    /// Helper to get the basic symmetry operations required based on the user selection string.
    /// </summary>
    private List<SymmetryType> GetSymmetryTransforms(SelectedSymmetryType selectedSymmetry)
    {
        // Returns the set of *generators* for the symmetry group.
        // Applying these generators repeatedly explores the whole group.
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

    /// <summary>
    /// Calculates the Greatest Common Divisor (GCD) of two integers using the Euclidean algorithm.
    /// </summary>
    private static int CalculateGcd(int a, int b)
    {
        while (b != 0)
        {
            int temp = b;
            b = a % b; // Remainder
            a = temp;
        }
        // When b becomes 0, a holds the GCD
        return a;
    }

    /// <summary>
    /// Calculates the Greatest Common Divisor (GCD) for a list of integers.
    /// </summary>
    /// <param name="numbers">An enumerable collection of integers.</param>
    /// <returns>The GCD of the list. Returns 1 if the list is empty or null, or if the calculated GCD is less than 1.</returns>
    private static int CalculateListGcd(IEnumerable<int> numbers)
    {
        // Handle empty or null input gracefully
        if (numbers == null || !numbers.Any())
        {
            // Returning 1 is safe for the decrement step logic.
            // Mathematically, GCD of an empty set is sometimes considered 0,
            // but that would break the loop.
            return 1;
        }

        // Start with the first number
        int result = numbers.First();

        // Iterate through the rest of the numbers, calculating GCD pairwise
        foreach (int number in numbers.Skip(1))
        {
            result = CalculateGcd(result, number);

            // Optimization: If the GCD ever reaches 1, it cannot get smaller.
            // All subsequent GCD calculations with 1 will result in 1.
            if (result == 1)
            {
                break;
            }
        }

        // Ensure the result used for decrementing is at least 1
        // (Handles cases where input might contain only 0s, though shape areas should be > 0)
        return Math.Max(1, result);
    }

    private bool TryGetCellTypeForElementAt(ISolveElement element, int r, int c, out CellTypeInfo? cellType)
    {
        cellType = null;
        // An element might cover (r,c) via any of its constituent placements
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
                // Crucially, check if the cell type at this relative position is non-empty
                if (!currentCellType.IsEmpty)
                {
                    // This placement contributes the non-empty cell type at (r,c)
                    cellType = currentCellType;
                    return true; // Found the relevant type
                }
                // If it's empty here, this placement doesn't define the type at (r,c),
                // continue checking other placements within the element (if it's a group)
            }
        }
        // If no placement within the element provided a non-empty cell at (r,c),
        // then this element doesn't actually define the type there, even if (r,c)
        // might be listed in GetAllCoveredCells (e.g., if GetAllCoveredCells had a bug)
        // Or, more likely, (r,c) wasn't actually covered by this element.
        return false;
    }

}