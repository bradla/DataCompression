using System;
using System.Diagnostics;
using System.Threading.Tasks;

public class AdvancedPerformanceMonitor : IDisposable
{
    private readonly Stopwatch _stopwatch;
    private readonly long _startMemory;
    private readonly string _operationName;
    private readonly Process _currentProcess;

    public AdvancedPerformanceMonitor(string operationName = "Operation")
    {
        _operationName = operationName;
        _stopwatch = Stopwatch.StartNew();
        _startMemory = GC.GetTotalMemory(forceFullCollection: false);
        _currentProcess = Process.GetCurrentProcess();
        
        Console.WriteLine($"Starting: {_operationName}");
    }

    public void Dispose()
    {
        _stopwatch.Stop();
        var endMemory = GC.GetTotalMemory(forceFullCollection: false);
        
        // Get CPU usage (approximate)
        var cpuTime = _currentProcess.TotalProcessorTime;
        
        Console.WriteLine($"\n {_operationName} - Performance Report");
        Console.WriteLine("==========================================");
        Console.WriteLine($"Execution Time: {_stopwatch.ElapsedMilliseconds} ms");
        Console.WriteLine($"CPU Time: {cpuTime.TotalMilliseconds:F2} ms");
        Console.WriteLine($"Memory Usage: {(endMemory - _startMemory) / 1024.0:F2} KB");
        Console.WriteLine($"GC Collections - Gen 0: {GC.CollectionCount(0)}, Gen 1: {GC.CollectionCount(1)}, Gen 2: {GC.CollectionCount(2)}");
        Console.WriteLine($"Peak Working Set: {_currentProcess.PeakWorkingSet64 / 1024.0 / 1024.0:F2} MB");
        Console.WriteLine($"Thread Count: {_currentProcess.Threads.Count}");
        Console.WriteLine("==========================================\n");
    }
}

public class TimeMonitor : IDisposable
{
    private readonly Stopwatch _stopwatch;
    private readonly string _operationName;

    public TimeMonitor(string operationName = "Operation")
    {
        _operationName = operationName;
        _stopwatch = Stopwatch.StartNew();
        
        Console.WriteLine($"Starting: {_operationName}");
    }

    public void Dispose()
    {
        _stopwatch.Stop();
        
        
        Console.WriteLine($"\n {_operationName} - Performance Report");
        Console.WriteLine("==========================================");
        Console.WriteLine($"Execution Time: {_stopwatch.ElapsedMilliseconds} ms");
        Console.WriteLine("==========================================\n");
    }
}