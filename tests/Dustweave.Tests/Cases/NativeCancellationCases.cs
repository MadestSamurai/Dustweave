using BD2Daily;
using System.Diagnostics;

static class NativeCancellationCases
{
    public static void Run(List<string> cases)
    {
        const int size = 300;
        var rng = new Random(441);
        var rows = new List<double[]>();
        for (int i = 0; i < size; i++)
            for (int j = i + 1; j < size; j++)
                if (rng.NextDouble() < .13)
                {
                    var row = new double[size];
                    row[i] = row[j] = 1;
                    rows.Add(row);
                }
        var cost = Enumerable.Range(0, size).Select(_ => -(double)rng.Next(1, 100)).ToArray();
        var timer = Stopwatch.StartNew();
        int checks = 0;
        Exception? error = null;
        try
        {
            DailyLinearOptimizer.Solve(cost, Enumerable.Repeat(1d, size).ToArray(), rows,
                Enumerable.Repeat(double.NegativeInfinity, rows.Count).ToArray(), Enumerable.Repeat(1d, rows.Count).ToArray(), true, 15,
                () => { if (++checks >= 3) throw new OperationCanceledException("operator_stop"); });
        }
        catch (OperationCanceledException e) { error = e; }
        if (error?.Data["daily_optimizer_native_callback"] is not true || timer.Elapsed > TimeSpan.FromSeconds(5))
            throw new Exception("Native MIP stop did not interrupt through its callback: " + checks + " / " + timer.Elapsed);
        cases.Add("actual native MIP is interrupted by operator stop before its 15 second search budget");
        var next = DailyLinearOptimizer.Solve([-1], [1], [new double[] { 1 }], [0], [1], true);
        if (next?.Values[0] != 1 || !next.Optimal)
            throw new Exception("Cancelled native solver leaked state into the next plan");
        cases.Add("native solver and callback resources are released after cancellation");
    }
}
