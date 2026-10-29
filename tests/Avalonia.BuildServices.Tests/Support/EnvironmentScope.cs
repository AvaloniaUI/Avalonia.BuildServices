using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Avalonia.Telemetry.Tests;

// The task reads the opt-out from the real environment of this process. A
// developer who has opted out, or who runs the tests under NCrunch, would
// otherwise get different results from everyone else. This clears both
// variables, however they are spelt, sets what the test asks for, and puts
// everything back afterwards.
internal sealed class EnvironmentScope : IDisposable
{
    private static readonly string[] Cleared = ["AVALONIA_TELEMETRY_OPTOUT", "NCrunch"];

    private readonly Dictionary<string, string?> _before = new(StringComparer.Ordinal);

    public EnvironmentScope(params (string Name, string Value)[] variables)
    {
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string name && Cleared.Any(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase)))
            {
                _before[name] = entry.Value as string;
                Environment.SetEnvironmentVariable(name, null);
            }
        }

        foreach (var (name, value) in variables)
        {
            if (!_before.ContainsKey(name))
            {
                _before[name] = Environment.GetEnvironmentVariable(name);
            }

            Environment.SetEnvironmentVariable(name, value);
        }
    }

    public void Dispose()
    {
        // Remove first, restore second. Where names match whatever their case
        // (Windows), removing a spelling the test added would otherwise take
        // the restored original with it.
        foreach (var variable in _before.Where(v => v.Value is null))
        {
            Environment.SetEnvironmentVariable(variable.Key, null);
        }

        foreach (var variable in _before.Where(v => v.Value is not null))
        {
            Environment.SetEnvironmentVariable(variable.Key, variable.Value);
        }
    }
}
