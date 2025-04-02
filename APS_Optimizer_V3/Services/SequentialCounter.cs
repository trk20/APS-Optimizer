using System;
using System.Collections.Generic;
using System.Text; // Keep for potential debugging output if needed
using System.Linq;

namespace APS_Optimizer_V3.Services;

public static class SequentialCounter
{
    // Encodes the constraint that at most 'k' variables in the list can be true.
    // Returns the list of CNF clauses and the number of auxiliary variables used.
    public static (List<List<int>> clauses, int auxVarCount) EncodeAtMostK(
        List<int> variables, int k, VariableManager varManager)
    {
        var clauses = new List<List<int>>();
        int n = variables.Count;

        if (k < 0) throw new ArgumentOutOfRangeException(nameof(k), "k cannot be negative.");
        if (k == 0)
        {
            foreach (int x_i in variables)
            {
                clauses.Add(new List<int> { -x_i }); // Clause: -x_i 0
            }
            return (clauses, 0);
        }
        if (k >= n || n <= 1)
        {
            return (clauses, 0); // Empty list, constraint is trivial
        }

        int[,] s = new int[n, k];
        int auxVarsUsed = 0;
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < k; j++)
            {
                s[i, j] = varManager.GetNextVariable();
                auxVarsUsed++;
            }
        }

        // Base case: i = 0
        clauses.Add(new List<int> { -variables[0], s[0, 0] });       // -x_0 OR s_0,0
        for (int j = 1; j < k; j++)
        {
            clauses.Add(new List<int> { -s[0, j] });                 // -s_0,j
        }

        // Recursive step: 0 < i < n
        for (int i = 1; i < n; i++)
        {
            clauses.Add(new List<int> { -variables[i], s[i, 0] });   // -x_i OR s_i,0
            clauses.Add(new List<int> { -s[i - 1, 0], s[i, 0] });    // -s_{i-1},0 OR s_i,0

            for (int j = 1; j < k; j++)
            {
                // -x_i OR -s_{i-1},{j-1} OR s_i,j
                clauses.Add(new List<int> { -variables[i], -s[i - 1, j - 1], s[i, j] });
                // -s_{i-1},j OR s_i,j
                clauses.Add(new List<int> { -s[i - 1, j], s[i, j] });
            }
            // -x_i OR -s_{i-1},{k-1}
            clauses.Add(new List<int> { -variables[i], -s[i - 1, k - 1] });
        }

        return (clauses, auxVarsUsed);
    }

    // Encodes the constraint that at least 'k' variables in the list must be true.
    public static (List<List<int>> clauses, int auxVarCount) EncodeAtLeastK(
        List<int> variables, int k, VariableManager varManager)
    {
        var clauses = new List<List<int>>();
        int n = variables.Count;
        int totalAuxVars = 0;

        if (k < 0) throw new ArgumentOutOfRangeException(nameof(k), "k cannot be negative.");
        if (k == 0) return (clauses, 0); // Trivial
        if (k > n)
        {
            int dummyVar = varManager.GetNextVariable();
            clauses.Add(new List<int> { dummyVar });    // dummy 0
            clauses.Add(new List<int> { -dummyVar });   // -dummy 0
            return (clauses, 1); // Impossible, return unsatisfiable clauses
        }
        if (k == n)
        {
            foreach (int x_i in variables)
            {
                clauses.Add(new List<int> { x_i });     // x_i 0
            }
            return (clauses, 0); // All must be true
        }

        // --- Encode AtMost(n-k) for the negated variables using temporary variables ---
        List<int> tempNegatedVars = new List<int>();
        var negationClauses = new List<List<int>>();
        int tempVarCount = 0;

        foreach (int originalVar in variables)
        {
            int tempVar = varManager.GetNextVariable();
            tempNegatedVars.Add(tempVar);
            tempVarCount++;
            // Add clauses: (tempVar OR originalVar) AND (-tempVar OR -originalVar)
            negationClauses.Add(new List<int> { tempVar, originalVar });
            negationClauses.Add(new List<int> { -tempVar, -originalVar });
        }
        totalAuxVars += tempVarCount;

        var (atMostClauses, atMostAuxVars) = EncodeAtMostK(tempNegatedVars, n - k, varManager);
        totalAuxVars += atMostAuxVars;

        // Combine the clauses
        clauses.AddRange(negationClauses);
        clauses.AddRange(atMostClauses);

        return (clauses, totalAuxVars);
    }
}