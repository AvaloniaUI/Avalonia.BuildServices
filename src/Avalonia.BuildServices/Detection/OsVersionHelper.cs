using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Avalonia.Telemetry;

// The operating system's name and version, precise enough to tell releases
// apart and no more: "Windows 10.0.26200", "macOS 27.0", "Ubuntu 24.04",
// "Fedora Linux 44", "Arch Linux". RuntimeInformation.OSDescription varies with
// the .NET runtime MSBuild happens to run on: on macOS older runtimes give the
// whole kernel build string, on Linux it is the distribution's full name with
// edition, code name and point release, and on Windows it can end in a space.
// That made hundreds of values for a few dozen releases.
internal static class OsVersionHelper
{
    private static readonly Regex WindowsVersion = new(@"\d+\.\d+\.\d+");

    public static string Describe()
    {
        try
        {
            var description =
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? DescribeWindows()
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? DescribeMac()
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? DescribeLinux()
                : null;
            if (description != null)
                return description;
        }
        catch
        {
            // Fall back to what the runtime says.
        }

        return RuntimeInformation.OSDescription.Trim();
    }

    // .NET Framework and .NET both say "Microsoft Windows 10.0.26200". The build
    // number is what tells releases apart: 19045 is Windows 10 22H2, 26100
    // Windows 11 24H2 and Windows Server 2025.
    private static string? DescribeWindows()
    {
        var version = WindowsVersion.Match(RuntimeInformation.OSDescription);
        return version.Success ? "Windows " + version.Value : null;
    }

    // "27.0.1" becomes "macOS 27.0".
    private static string? DescribeMac()
    {
        var version = MacUtil.ReadString("kern.osproductversion");
        return string.IsNullOrEmpty(version) ? null : "macOS " + MajorMinor(version!);
    }

    private static readonly char[] KeyValueSeparator = ['='];

    // os-release(5): NAME and VERSION_ID, from /etc or, failing that, /usr/lib.
    // Rolling distributions have no VERSION_ID, except openSUSE Tumbleweed,
    // whose VERSION_ID is the snapshot date; it is left out like theirs.
    private static string DescribeLinux()
    {
        var path = File.Exists("/etc/os-release") ? "/etc/os-release" : "/usr/lib/os-release";
        if (!File.Exists(path))
            return "Linux";

        var fields = File.ReadAllLines(path)
            .Select(l => l.Split(KeyValueSeparator, 2))
            .Where(kv => kv.Length == 2)
            .GroupBy(kv => kv[0].Trim())
            .ToDictionary(g => g.Key, g => g.Last()[1].Trim().Trim('"', '\''));

        var name = fields.TryGetValue("NAME", out var n) && n.Length > 0 ? n : "Linux";
        if (!fields.TryGetValue("VERSION_ID", out var version) || version.Length == 0
            || (version.Length == 8 && version.All(char.IsDigit)))
            return name;

        return name + " " + MajorMinor(version);
    }

    private static string MajorMinor(string version) => string.Join(".", version.Split('.').Take(2));
}
