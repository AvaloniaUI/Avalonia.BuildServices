using Avalonia.BuildServices.Protocol;

namespace Avalonia.BuildServices.Collector;

public static class Program
{
    public static void Main(string[] args)
    {
        Logger.Configure(System.IO.Path.Combine(BuildServicesPaths.AppDataFolder, "collector.log"));
        new Collector().Execute();
    }
}