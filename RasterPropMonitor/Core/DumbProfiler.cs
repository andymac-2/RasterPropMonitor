using System;
using System.Diagnostics;

class DumbProfiler
{
    private string name;
    private int count;
    private double sum;
    private double sumOfSquares;
    private Stopwatch stopwatch = new Stopwatch();

    public DumbProfiler(string name)
    {
        this.name = name;
    }

    public void Start()
    {
        if (stopwatch.IsRunning)
        {
            UnityEngine.Debug.Log("WARN: Stopwatch already started");
        }

        stopwatch.Reset();
        stopwatch.Start();
    }

    public void Stop()
    {
        stopwatch.Stop();
        double elapsed = stopwatch.ElapsedTicks;
        sum += elapsed;
        sumOfSquares += elapsed * elapsed;
        count++;

        if (count >= 1000)
        {
            double mean = sum / count;
            double stdDev = Math.Sqrt((sumOfSquares - 2 * mean * sum + count * mean * mean) / count);

            UnityEngine.Debug.Log($"Profiled {name}, {count} iterations, mean: {mean}, std deviation: {stdDev}");

            count = 0;
            sum = 0;
            sumOfSquares = 0;
        }
    }
}