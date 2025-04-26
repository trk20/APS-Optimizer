namespace APS_Optimizer_V3.Helpers;

public static class MathUtils
{
    // GCD of two ints
    public static int CalculateGcd(int a, int b)
    {
        while (b != 0)
        {
            int temp = b;
            b = a % b;
            a = temp;
        }
        return a;
    }

    // GCD for list of ints
    public static int CalculateListGcd(IEnumerable<int> numbers)
    {
        if (numbers == null || !numbers.Any())
        {
            return 1;
        }
        int result = numbers.First();

        foreach (int number in numbers.Skip(1))
        {
            result = CalculateGcd(result, number);
            if (result == 1)
            {
                break;
            }
        }
        return Math.Max(1, result);
    }
}