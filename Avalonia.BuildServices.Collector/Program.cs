using Avalonia.Telemetry;

public static class Program
{
    public static void Main(string[] args)
    {
        new Collector().Execute();
    }
}