using System;
using System.Collections;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Build.Framework;

namespace Avalonia.Telemetry
{
    public class Program
    {
        static int Main(string[] args)
        {
            new AvaloniaStatsTask().Execute();

            return 1;
        }
    }
}
