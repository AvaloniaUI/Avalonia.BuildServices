using System;

namespace Avalonia.Telemetry;

// Whether this build, published as configured, would be trimmed and compiled
// ahead of time. Each platform has its own switches; they map onto the same
// two answers here. Properties are read as the build sees them when the task
// runs, which on iOS is after its SDK has set PublishTrimmed.
internal static class PublishHelper
{
    // Every platform trims with ILLink when PublishTrimmed is true: iOS and
    // browser-wasm turn it on themselves, Android does in Release unless
    // AndroidLinkMode=None. TrimMode=copy runs the trimmer without removing
    // anything, which is how iOS switches linking off (MtouchLink=None, and by
    // default in the x64 simulator).
    public static bool IsTrimmed(string? publishTrimmed, string? trimMode) =>
        IsTrue(publishTrimmed) && !string.Equals(trimMode, "copy", StringComparison.OrdinalIgnoreCase);

    // All of the app's code compiled ahead of time, so nothing relies on a JIT:
    // - Native AOT (PublishAot), on any platform;
    // - iOS and tvOS on a device, where Apple allows no JIT (simulators have one);
    // - Mono AOT (RunAOTCompilation) in the browser, and on Android unless it is
    //   profiled. Android's Release default compiles only startup code and
    //   leaves the rest to the JIT, so it does not count.
    public static bool IsAheadOfTime(string? publishAot, string? runAotCompilation, string? androidEnableProfiledAot,
        string? targetPlatformIdentifier, string? runtimeIdentifier)
    {
        if (IsTrue(publishAot))
            return true;

        var platform = targetPlatformIdentifier?.ToLowerInvariant();
        if (platform is "ios" or "tvos")
            return !string.IsNullOrEmpty(runtimeIdentifier) && runtimeIdentifier!.IndexOf("simulator", StringComparison.OrdinalIgnoreCase) < 0;

        if (!IsTrue(runAotCompilation))
            return false;

        return platform != "android" || !IsTrue(androidEnableProfiledAot);
    }

    private static bool IsTrue(string? value) => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
