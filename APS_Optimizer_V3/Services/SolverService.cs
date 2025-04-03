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

        // 1. Generate Placements
        var (allPlacements, placementVarMap) = GeneratePlacements(parameters);
        if (!allPlacements.Any())
        {
            return new SolverResult(false, "No valid placements possible for any shape.", 0, null);
        }
        Debug.WriteLine($"Generated {allPlacements.Count} raw placements.");

        // 2. Apply Symmetry & Assign Variables
        object problemRepresentation;
        Dictionary<int, object> variableToObjectMap;
        VariableManager varManager = new VariableManager();

        if (parameters.SelectedSymmetry == "None")
        {
            foreach (var p in allPlacements)
            {
                placementVarMap[p.PlacementId] = varManager.GetNextVariable();
            }
            variableToObjectMap = allPlacements.ToDictionary(p => placementVarMap[p.PlacementId], p => (object)p);
            problemRepresentation = allPlacements;
            Debug.WriteLine($"Using {variableToObjectMap.Count} variables for placements (no symmetry).");
        }
        else
        {
            // --- Fallback to no symmetry (as symmetry is not implemented) ---
            Debug.WriteLine($"Warning: Symmetry '{parameters.SelectedSymmetry}' not fully implemented. Proceeding without symmetry grouping.");
            foreach (var p in allPlacements)
            {
                placementVarMap[p.PlacementId] = varManager.GetNextVariable();
            }
            variableToObjectMap = allPlacements.ToDictionary(p => placementVarMap[p.PlacementId], p => (object)p);
            problemRepresentation = allPlacements;
            // --- End Fallback ---
            // TODO: Implement actual symmetry grouping here when ready.
        }

        // 3. Detect Collisions
        var cellCollisions = DetectCollisions(problemRepresentation, placementVarMap, parameters.GridWidth, parameters.GridHeight);
        Debug.WriteLine($"Detected collisions across {cellCollisions.Count} cells.");

        // 4. Generate Base CNF (Collision Constraints) as List<List<int>>
        var baseClauses = new List<List<int>>();
        foreach (var kvp in cellCollisions)
        {
            var collidingVars = kvp.Value;
            if (collidingVars.Count > 1)
            {
                var (atMostClauses, _) = SequentialCounter.EncodeAtMostK(collidingVars, 1, varManager);
                baseClauses.AddRange(atMostClauses);
            }
        }
        Debug.WriteLine($"Generated {baseClauses.Count} base clauses for collisions. Max Var ID so far: {varManager.GetMaxVariableId()}");

        // 5. Iterative Solving Loop
        int totalAvailableCells = parameters.GridWidth * parameters.GridHeight - parameters.BlockedCells.Count;
        int minShapeArea = parameters.EnabledShapes.Any() ? parameters.EnabledShapes.Min(s => s.GetBaseRotationGrid().Cast<bool>().Count(c => c)) : 1;
        if (minShapeArea <= 0) minShapeArea = 1;
        int requiredCells = (totalAvailableCells / minShapeArea) * minShapeArea;

        while (requiredCells >= 0) // Loop until 0 or solution found
        {
            Debug.WriteLine($"Attempting to solve for at least {requiredCells} covered cells.");
            var currentClauses = new List<List<int>>(baseClauses); // Copy base clauses
            var currentVarManager = new VariableManager();
            // Ensure currentVarManager starts after base variables
            while (currentVarManager.GetMaxVariableId() < varManager.GetMaxVariableId()) { currentVarManager.GetNextVariable(); }

            // 6. Generate Coverage Constraint CNF (using Y variables)
            int[,] yVars = new int[parameters.GridHeight, parameters.GridWidth];
            var yVarLinkClauses = new List<List<int>>();
            var yVarList = new List<int>(); // Active Y variables for available cells

            for (int r = 0; r < parameters.GridHeight; r++)
            {
                for (int c = 0; c < parameters.GridWidth; c++)
                {
                    if (parameters.BlockedCells.Contains((r, c))) { yVars[r, c] = 0; continue; }

                    int yVar = currentVarManager.GetNextVariable();
                    yVars[r, c] = yVar;
                    yVarList.Add(yVar);
                    int cellIndex = r * parameters.GridWidth + c;

                    if (cellCollisions.TryGetValue(cellIndex, out var placementsCoveringCell))
                    {
                        // Y[r,c] => OR(Placements)  <=> -Y OR P1 OR P2 ...
                        var yImpPlacementsClause = new List<int> { -yVar };
                        yImpPlacementsClause.AddRange(placementsCoveringCell);
                        yVarLinkClauses.Add(yImpPlacementsClause);

                        // Placement_i => Y[r,c] <=> -Pi OR Y
                        foreach (int pVar in placementsCoveringCell)
                        {
                            yVarLinkClauses.Add(new List<int> { -pVar, yVar });
                        }
                    }
                    else
                    {
                        yVarLinkClauses.Add(new List<int> { -yVar }); // -Y (must be false)
                    }
                }
            }
            currentClauses.AddRange(yVarLinkClauses);

            // Encode Sum(Y_vars) >= requiredCells <=> AtMost(n-k) on negated Y vars
            int k_for_atmost = totalAvailableCells - requiredCells;
            if (k_for_atmost < 0) k_for_atmost = 0;

            Debug.WriteLine($"Encoding AtMostK for {yVarList.Count} negated Y variables with k={k_for_atmost}");
            var (coverageClausesList, _) = SequentialCounter.EncodeAtMostK(
                yVarList.Select(y => -y).ToList(),
                k_for_atmost,
                currentVarManager);
            currentClauses.AddRange(coverageClausesList);

            // 7. Finalize CNF String and Run Solver
            int totalVars = currentVarManager.GetMaxVariableId();
            string finalCnfString = FormatDimacs(currentClauses, totalVars);
            // File.WriteAllText($"debug_solver_input_{requiredCells}.cnf", finalCnfString); // Optional debug

            var (sat, solutionVars) = await RunSatSolver(finalCnfString);

            // 8. Process Result
            if (sat && solutionVars != null)
            {
                stopwatch.Stop();
                Debug.WriteLine($"SATISFIABLE found for >= {requiredCells} cells. Time: {stopwatch.ElapsedMilliseconds} ms");
                var solutionPlacements = MapResult(solutionVars, variableToObjectMap, placementVarMap);
                return new SolverResult(true, $"Solution found covering at least {requiredCells} cells.", requiredCells, solutionPlacements);
            }
            else
            {
                Debug.WriteLine($"UNSATISFIABLE for >= {requiredCells} cells.");
                if (requiredCells == 0) break; // Stop if even 0 is unsatisfiable (shouldn't happen)
                requiredCells -= minShapeArea;
                if (requiredCells < 0) requiredCells = 0; // Ensure it checks 0 if needed
            }
        }

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
        var placementVarMap = new Dictionary<int, int>(); // Maps PlacementID -> CNF Var ID (assigned later)
        int placementIdCounter = 0;
        var blockedSet = parameters.BlockedCells.ToHashSet(); // Faster lookups

        for (int shapeIndex = 0; shapeIndex < parameters.EnabledShapes.Count; shapeIndex++)
        {
            var shapeVM = parameters.EnabledShapes[shapeIndex];
            var rotations = shapeVM.GetAllRotationGrids(); // Get all unique rotations

            for (int rotIndex = 0; rotIndex < rotations.Count; rotIndex++)
            {
                var grid = rotations[rotIndex];
                int pHeight = grid.GetLength(0);
                int pWidth = grid.GetLength(1);

                if (pHeight == 0 || pWidth == 0) continue; // Skip empty shapes

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

                                    if (blockedSet.Contains((gridR, gridC)))
                                    {
                                        isValid = false;
                                        break;
                                    }
                                    covered.Add((gridR, gridC));
                                }
                            }
                            if (!isValid) break;
                        }

                        if (isValid)
                        {
                            var placement = new Placement(
                                placementIdCounter++,
                                shapeIndex,
                                shapeVM.Name,
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
        return (placements, placementVarMap);
    }

    private Dictionary<int, List<int>> DetectCollisions(
       object problemRepresentation,
       Dictionary<int, int> placementVarMap, // Maps PlacementID -> CNF Var ID
       int gridWidth, int gridHeight)
    {
        var cellCollisions = new Dictionary<int, List<int>>();

        Action<Placement> processPlacement = (placement) =>
        {
            if (!placementVarMap.TryGetValue(placement.PlacementId, out int cnfVarId))
            {
                Debug.WriteLine($"Error: CNF Variable ID not found for Placement ID {placement.PlacementId}");
                return;
            }

            foreach (var (r, c) in placement.CoveredCells)
            {
                int cellIndex = r * gridWidth + c;
                if (!cellCollisions.TryGetValue(cellIndex, out var varList))
                {
                    varList = new List<int>();
                    cellCollisions[cellIndex] = varList;
                }
                if (!varList.Contains(cnfVarId))
                {
                    varList.Add(cnfVarId);
                }
            }
        };

        if (problemRepresentation is List<Placement> placements)
        {
            foreach (var placement in placements) { processPlacement(placement); }
        }
        else if (problemRepresentation is List<SymmetryGroup> groups)
        {
            throw new NotImplementedException("Collision detection for symmetry groups not implemented.");
            // TODO: Adapt for symmetry groups when implemented
        }
        else { throw new ArgumentException("Invalid problem representation type."); }

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
       List<int> trueVars,
       Dictionary<int, object> variableToObjectMap,
       Dictionary<int, int> placementVarMap)
    {
        var solutionPlacements = new List<Placement>();

        foreach (int trueVar in trueVars)
        {
            if (variableToObjectMap.TryGetValue(trueVar, out object? obj))
            {
                if (obj is Placement placement)
                {
                    solutionPlacements.Add(placement);
                }
                else if (obj is SymmetryGroup group)
                {
                    throw new NotImplementedException("Result mapping for symmetry groups not implemented.");
                    // TODO: Add placements from the selected group when symmetry is implemented
                }
            }
        }
        return solutionPlacements.ToImmutableList();
    }

}