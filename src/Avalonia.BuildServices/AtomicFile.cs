using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Avalonia.Telemetry;

// Writes a file whole or not at all. The content goes to a temporary file in
// the same folder, which then takes its real name in one step to the file
// system. A build that is killed part-way leaves no half-written file behind,
// a parallel build or the collector never reads one, and of several builds
// that write the same file at once exactly one succeeds.
internal static class AtomicFile
{
    // link(2): gives a file a second name, and fails when that name is taken.
    // The paths go as UTF-8 bytes with a closing zero, which is what the file
    // systems this runs on expect whatever the marshaller's default.
    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int Link(byte[] existing, byte[] created);

    // EEXIST, the same number on Linux, macOS and the BSDs.
    private const int NameTaken = 17;

    // Writes the file unless it is already there. False when it is: a parallel
    // build got there first, and its file stands.
    public static bool TryCreate(string path, Action<Stream> write)
    {
        var folder = Path.GetDirectoryName(path)
                     ?? throw new ArgumentException("The path names no folder.", nameof(path));

        // Not a spool file's name (Common.IsSpoolFile), so no collector sends
        // it. The collector clears away one that a killed build left behind.
        var temporary = Path.Combine(folder, "write_" + Guid.NewGuid().ToString("N") + Common.TemporaryFileExtension);

        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                write(stream);
            }

            return Publish(temporary, path);
        }
        catch (IOException) when (File.Exists(path))
        {
            return false;
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    // Gives the finished file its real name unless that name is taken. False,
    // or an IOException, when it is.
    private static bool Publish(string temporary, string path)
    {
        // On Windows File.Move does not replace a file, and the check and the
        // rename are one step. On the other systems .NET looks first and then
        // calls rename(2), which does replace: two builds that both looked
        // before either renamed would each believe its own file stood. A hard
        // link cannot do that, because it fails when the name is taken. The
        // caller then deletes the temporary name, which leaves the one file.
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            switch (TryLink(temporary, path))
            {
                case true:
                    return true;
                case false:
                    return false;
            }
        }

        File.Move(temporary, path);
        return true;
    }

    // Null when a hard link cannot be made here at all: a file system without
    // them (FAT, some network shares), or no libc to call.
    private static bool? TryLink(string existing, string created)
    {
        try
        {
            if (Link(NativePath(existing), NativePath(created)) == 0)
            {
                return true;
            }

            return Marshal.GetLastWin32Error() == NameTaken ? false : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static byte[] NativePath(string path) => Encoding.UTF8.GetBytes(path + "\0");

    public static bool TryCreate(string path, byte[] content) =>
        TryCreate(path, stream => stream.Write(content, 0, content.Length));

    // False when the file is still there afterwards.
    public static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
