using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.System.Threading;

namespace Avalonia.Telemetry;

public class WinProcUtil
{
    public static unsafe void StartBackground(string exe, string cmdline)
    {
        var secattr = new SECURITY_ATTRIBUTES
        {
            nLength = (uint)Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
            lpSecurityDescriptor = null,
            bInheritHandle = true
        };

        using var nullFile = PInvoke.CreateFile("NUL",
            0x10000000,
            FILE_SHARE_MODE.FILE_SHARE_NONE,
            secattr,
            FILE_CREATION_DISPOSITION.OPEN_EXISTING,
            FILE_FLAGS_AND_ATTRIBUTES.SECURITY_ANONYMOUS,
            null);
        var hNul = nullFile.DangerousGetHandle();
        
        var startupInfo = new STARTUPINFOW
        {
            cb = (uint)Marshal.SizeOf<STARTUPINFOW>(),
            dwFlags = STARTUPINFOW_FLAGS.STARTF_USESTDHANDLES,
            hStdInput = new HANDLE(hNul),
            hStdOutput = new HANDLE(hNul),
            hStdError = new HANDLE(hNul),
        };

        Span<char> cmdLineSpan = (cmdline + '\0').ToCharArray();
        if (!PInvoke.CreateProcess(exe, ref cmdLineSpan, null, null,
                false,
                PROCESS_CREATION_FLAGS.CREATE_NO_WINDOW
                | PROCESS_CREATION_FLAGS.CREATE_NEW_PROCESS_GROUP
                | PROCESS_CREATION_FLAGS.DETACHED_PROCESS,
                null, Directory.GetCurrentDirectory(), startupInfo, out var info))
            throw new Win32Exception();

        PInvoke.CloseHandle(info.hProcess);
        PInvoke.CloseHandle(info.hThread);
    }
}