using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.System.Threading;

namespace Avalonia.Telemetry;

internal static class WinProcUtil
{
    // Starts a process that outlives the build and shares nothing with it.
    //
    // No handle is inherited. A child holding the build's output pipe would
    // keep whoever reads that pipe (a CI step, an IDE) waiting until the child
    // exits. STARTF_USESTDHANDLES is not used for the same reason: it only
    // works with inherited handles. A detached process then has no standard
    // handles at all, which .NET treats as empty streams.
    //
    // The working directory must not be the project's: Windows will not delete
    // or rename a folder while it is some process's current directory.
    public static unsafe void StartBackground(string commandLine, string workingDirectory)
    {
        var startupInfo = new STARTUPINFOW { cb = (uint)Marshal.SizeOf<STARTUPINFOW>() };

        // CreateProcess may write to the command line, so it gets its own copy.
        Span<char> commandLineBuffer = (commandLine + '\0').ToCharArray();

        if (!PInvoke.CreateProcess(null, ref commandLineBuffer, null, null,
                false,
                PROCESS_CREATION_FLAGS.CREATE_NO_WINDOW | PROCESS_CREATION_FLAGS.CREATE_NEW_PROCESS_GROUP |
                PROCESS_CREATION_FLAGS.DETACHED_PROCESS,
                null, workingDirectory, startupInfo, out var info))
            throw new Win32Exception();

        PInvoke.CloseHandle(info.hProcess);
        PInvoke.CloseHandle(info.hThread);
    }
}
