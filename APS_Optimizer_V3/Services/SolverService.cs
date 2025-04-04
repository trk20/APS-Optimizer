using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using APS_Optimizer_V3.ViewModels;

namespace APS_Optimizer_V3.Services;

public class SolverService
{
    private const string CryptoMiniSatPath = "cryptominisat5.exe";

    public async Task<SolverResult> SolveAsync(SolveParameters parameters)
    {
        var stopwatch = Stopwatch.StartNew();
        Debug.WriteLine($"Solver started. Grid: {parameters.GridWidth}x{parameters.GridHeight}, Shapes: {parameters.EnabledShapes.Count}, Symmetry: {parameters.SelectedSymmetry}");

        // 1. Generate all possible valid placements (raw)
        var (allPlacements, _) = GeneratePlacements(parameters); // Don't need placementVarMap from here anymore
        if (!allPlacements.Any())
        {
            return new SolverResult(false, "No valid placements possible for any shape.", 0, null);
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
        // 'solveElements' now contains ISolveElement objects (Placement or SymmetryGroup)
        // 'variableToObjectMap' maps the assigned VariableId to the corresponding ISolveElement

        if (!solveElements.Any()) // Should not happen if allPlacements was not empty, but check anyway
        {
            return new SolverResult(false, "Grouping resulted in zero elements.", 0, null);
        }


        // 3. Detect Collisions (using ISolveElements)
        var cellCollisions = DetectCollisions(solveElements, parameters.GridWidth, parameters.GridHeight);
        Debug.WriteLine($"Detected collisions across {cellCollisions.Count} cells.");


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

        Debug.WriteLine($"Generated {baseClauses.Count} base clauses for collisions. Max Var ID so far: {varManager.GetMaxVariableId()}");

        // 5. Iterative Solving Loop (using GCD)
        // ... (Calculate GCD, set initial requiredCells - NO CHANGES here) ...
        var shapeAreas = parameters.EnabledShapes.Select(s => s.GetBaseRotationGrid().Cast<bool>().Count(c => c)).Where(area => area > 0).ToList();
        if (!shapeAreas.Any()) { /* handle error */ return new SolverResult(false, "...", 0, null); }
        int decrementStep = CalculateListGcd(shapeAreas);
        int totalAvailableCells = parameters.GridWidth * parameters.GridHeight - parameters.BlockedCells.Count;
        int requiredCells = (totalAvailableCells / decrementStep) * decrementStep;


        while (requiredCells >= 0)
        {
            Debug.WriteLine($"Attempting to solve for at least {requiredCells} covered cells.");
            var currentClauses = new List<List<int>>(baseClauses);
            var currentVarManager = new VariableManager();
            while (currentVarManager.GetMaxVariableId() < varManager.GetMaxVariableId()) { currentVarManager.GetNextVariable(); }

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

                    int yVar = currentVarManager.GetNextVariable();
                    yVars[r, c] = yVar;
                    yVarList.Add(yVar);
                    int cellIndex = r * parameters.GridWidth + c;

                    // Find which ISolveElements cover this cell
                    if (cellCollisions.TryGetValue(cellIndex, out var elementsCoveringCellVars))
                    {
                        // Y[r,c] => OR(Elements covering (r,c))
                        // CNF: -Y[r,c] OR E1 OR E2 ... (where E_i is the VariableId of the ISolveElement)
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
                        yVarLinkClauses.Add(new List<int> { -yVar }); // -Y (must be false)
                    }
                }
            }
            currentClauses.AddRange(yVarLinkClauses);
            // --- End Y Variable Update ---


            // Encode Sum(Y_vars) >= requiredCells
            // ... (AtLeastK/AtMostK encoding remains the same, using yVarList) ...
            var (coverageClausesList, _) = SequentialCounter.EncodeAtLeastK(yVarList, requiredCells, currentVarManager);
            currentClauses.AddRange(coverageClausesList);


            // 7. Finalize CNF and Run Solver
            int totalVars = currentVarManager.GetMaxVariableId();
            string finalCnfString = FormatDimacs(currentClauses, totalVars);
            // File.WriteAllText($"debug_solver_input_{requiredCells}.cnf", finalCnfString);

            var (sat, solutionVars) = await RunSatSolver(finalCnfString);

            // 8. Process Result
            if (sat && solutionVars != null)
            {
                stopwatch.Stop();
                Debug.WriteLine($"SATISFIABLE found for >= {requiredCells} cells. Time: {stopwatch.ElapsedMilliseconds} ms");

                // *** UPDATE MAPPING TO USE ISolveElement ***
                var solutionPlacements = MapResult(solutionVars, variableToObjectMap);
                return new SolverResult(true, $"Solution found covering at least {requiredCells} cells.", requiredCells, solutionPlacements);
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
        return new SolverResult(false, "No solution found for any possible coverage.", 0, null);
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
        var placementVarMap = new Dictionary<int, int>();
        int placementIdCounter = 0;
        var blockedSet = parameters.BlockedCells.ToHashSet();

        for (int shapeIndex = 0; shapeIndex < parameters.EnabledShapes.Count; shapeIndex++)
        {
            var shapeVM = parameters.EnabledShapes[shapeIndex];
            var rotations = shapeVM.GetAllRotationGrids();

            for (int rotIndex = 0; rotIndex < rotations.Count; rotIndex++)
            {
                var grid = rotations[rotIndex];
                int pHeight = grid.GetLength(0);
                int pWidth = grid.GetLength(1);

                if (pHeight == 0 || pWidth == 0) continue;

                // Ensure loops correctly prevent shape *bounding box* from going off edge
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
                                if (grid[pr, pc]) // If this part of the shape exists
                                {
                                    int gridR = r + pr;
                                    int gridC = c + pc;

                                    // *** ADD EXPLICIT BOUNDS CHECK HERE ***
                                    // This check should technically be redundant if outer loops are correct,
                                    // but acts as a safeguard against unexpected issues.
                                    if (gridR < 0 || gridR >= parameters.GridHeight || gridC < 0 || gridC >= parameters.GridWidth)
                                    {
                                        Debug.WriteLine($"!!! Internal Error: Calculated coordinate ({gridR},{gridC}) out of bounds in GeneratePlacements. Shape: {shapeVM.Name}, Pos ({r},{c}), Offset ({pr},{pc})");
                                        isValid = false;
                                        break; // Exit inner loop (pc)
                                    }
                                    // *** END ADDED CHECK ***

                                    if (blockedSet.Contains((gridR, gridC)))
                                    {
                                        isValid = false;
                                        break; // Exit inner loop (pc)
                                    }
                                    covered.Add((gridR, gridC));
                                }
                            }
                            if (!isValid) break; // Exit outer loop (pr)
                        }

                        if (isValid)
                        {
                            // Ensure covered list is not empty if shape had cells
                            if (grid.Cast<bool>().Any(cell => cell) && !covered.Any())
                            {
                                Debug.WriteLine($"Warning: Placement deemed valid but covered list is empty. Shape: {shapeVM.Name}, Pos ({r},{c})");
                                // Decide how to handle - skip placement?
                                continue;
                            }

                            var placement = new Placement(
                                placementIdCounter++,
                                shapeIndex,
                                shapeVM.Name,
                                rotIndex,
                                r, c,
                                grid,
                                covered.ToImmutableList() // Use the validated list
                            );
                            placements.Add(placement);
                        }
                    }
                }
            }
        }
        // placementVarMap is assigned later during grouping
        return (placements, placementVarMap);
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


    private async Task<(bool IsSat, List<int>? SolutionVariables)> RunSatSolver(string cnfContent)
    {
        if (!File.Exists(CryptoMiniSatPath))
        {
            Debug.WriteLine($"Error: SAT Solver not found at '{CryptoMiniSatPath}'");
            return (false, null);
        }

        var processInfo = new ProcessStartInfo
        {
            FileName = CryptoMiniSatPath,
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
        double centerX = (gridWidth - 1.0) / 2.0;
        double centerY = (gridHeight - 1.0) / 2.0;

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
        var placementLookup = new Dictionary<string, Placement>();

        // Precompute placement lookup
        foreach (var p in allPlacements)
        {
            string key = GenerateCoordinateKey(p.CoveredCells);
            if (!placementLookup.ContainsKey(key)) placementLookup.Add(key, p);
            // else: Handle duplicate key warning if necessary
        }

        var transformsToApply = GetSymmetryTransforms(parameters.SelectedSymmetry);

        foreach (var seedPlacement in allPlacements)
        {
            if (assignedPlacementIds.Contains(seedPlacement.PlacementId)) continue; // Already processed

            // --- Find all placements in this potential group ---
            var currentGroupPlacements = new List<Placement>(); // Placements found via symmetry from seed
            var visitedCoordinateKeys = new HashSet<string>();
            var queue = new Queue<Placement>();
            queue.Enqueue(seedPlacement);
            visitedCoordinateKeys.Add(GenerateCoordinateKey(seedPlacement.CoveredCells));

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
                    if (transformType == SymmetryType.None) continue;
                    var transformedCells = TryTransformCoveredCells(currentPlacement.CoveredCells, transformType, parameters.GridWidth, parameters.GridHeight, blockedCellsSet);
                    if (transformedCells != null)
                    {
                        string transformedKey = GenerateCoordinateKey(transformedCells);
                        if (visitedCoordinateKeys.Add(transformedKey)) // Check if visited *within this group search*
                        {
                            if (placementLookup.TryGetValue(transformedKey, out var matchingPlacement) &&
                                !assignedPlacementIds.Contains(matchingPlacement.PlacementId)) // Check global assignment
                            {
                                // Found a potential symmetric partner not yet assigned globally
                                queue.Enqueue(matchingPlacement);
                            }
                        }
                    }
                }
            } // End while queue

            // --- Group found (currentGroupPlacements), now check consistency ---
            bool isInternallyConsistent = true;
            if (currentGroupPlacements.Count > 1)
            {
                for (int i = 0; i < currentGroupPlacements.Count; i++)
                {
                    var p1Cells = currentGroupPlacements[i].CoveredCells;
                    for (int j = i + 1; j < currentGroupPlacements.Count; j++)
                    {
                        var p2Cells = currentGroupPlacements[j].CoveredCells;
                        if (p1Cells.Intersect(p2Cells).Any())
                        {
                            isInternallyConsistent = false;
                            Debug.WriteLine($"Symmetry Group Inconsistency Detected: Will split group originating from Placement {seedPlacement.PlacementId}. Placements {currentGroupPlacements[i].PlacementId} and {currentGroupPlacements[j].PlacementId} overlap.");
                            break;
                        }
                    }
                    if (!isInternallyConsistent) break;
                }
            }

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

        var elementCellsToId = new Dictionary<string, (ISolveElement Element, int Id)>();
        foreach (var element in solveElements.ToList()) // Copy list as we might modify it
        {
            string cellsKey = GenerateCoordinateKey(element.GetAllCoveredCells());
            if (elementCellsToId.TryGetValue(cellsKey, out var existing))
            {
                Debug.WriteLine($"Warning: Found duplicate solve elements covering same cells. VarIDs: {element.VariableId} and {existing.Id}");
                solveElements.Remove(element); // Remove duplicate element
                if (variableToObjectMap.ContainsKey(element.VariableId))
                {
                    variableToObjectMap.Remove(element.VariableId);
                }
            }
            else
            {
                elementCellsToId[cellsKey] = (element, element.VariableId);
            }
        }

        Debug.WriteLine($"Grouping resulted in {solveElements.Count} elements (valid groups/singletons/split individuals).");
        return solveElements;
    }


    /// <summary>
    /// Helper to get the basic symmetry operations required based on the user selection string.
    /// </summary>
    private List<SymmetryType> GetSymmetryTransforms(string selectedSymmetry)
    {
        // Returns the set of *generators* for the symmetry group.
        // Applying these generators repeatedly explores the whole group.
        switch (selectedSymmetry)
        {
            case "Rotational (180°)":
                return new List<SymmetryType> { SymmetryType.Rotate180 };
            case "Rotational (90°)":
                // Rotating by 90 degrees generates 180 and 270 as well.
                return new List<SymmetryType> { SymmetryType.Rotate90 };
            case "Horizontal":
                return new List<SymmetryType> { SymmetryType.ReflectHorizontal };
            case "Vertical":
                return new List<SymmetryType> { SymmetryType.ReflectVertical };
            case "Quadrants":
                return new List<SymmetryType> { SymmetryType.ReflectHorizontal, SymmetryType.ReflectVertical };
            case "None":
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
}