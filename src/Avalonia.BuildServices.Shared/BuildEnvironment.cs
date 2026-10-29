using System;
using System.Collections;
using System.Collections.Generic;

namespace Avalonia.Telemetry;

// The environment variables of one build, read once. Every detector asks this
// rather than the process, so they all see the same variables and all match
// names the same way.
//
// Names match whatever their case. Windows treats them so, and a lookup that
// does not would miss "Path", or an opt-out spelt in lower case. On the other
// systems it only means a variable that differs from a rule by case counts too.
internal sealed class BuildEnvironment
{
    private readonly Dictionary<string, string> _variables = new(StringComparer.OrdinalIgnoreCase);

    public BuildEnvironment(IEnumerable<KeyValuePair<string, string>> variables)
    {
        foreach (var variable in variables)
        {
            // Not Add: on Unix two names that differ only by case are two
            // variables, and the second must not throw.
            _variables[variable.Key] = variable.Value;
        }
    }

    // The variables of this process. Never throws: an environment that cannot
    // be read is an environment with nothing in it.
    public static BuildEnvironment Capture()
    {
        var variables = new List<KeyValuePair<string, string>>();

        try
        {
            foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                if (entry.Key is string name && entry.Value is string value)
                {
                    variables.Add(new KeyValuePair<string, string>(name, value));
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "Failed to read the environment.");
        }

        return new BuildEnvironment(variables);
    }

    // Set at all, even to nothing: many systems mark themselves with a bare name.
    public bool Contains(string name) => _variables.ContainsKey(name);

    // The value, or null when the variable is not set.
    public string? Get(string name) => _variables.TryGetValue(name, out var value) ? value : null;

    // Set to something.
    public bool IsSet(string name) => !string.IsNullOrEmpty(Get(name));

    // Set to this value. A value matches whatever its case unless the rule
    // asks for an exact match.
    public bool HasValue(string name, string value, StringComparison comparison = StringComparison.OrdinalIgnoreCase) =>
        Get(name) is { } actual && string.Equals(actual, value, comparison);
}
