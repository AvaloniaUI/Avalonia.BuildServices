using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Avalonia.Telemetry.Tests;

public class AtomicFileTests
{
    private static string NewFolder(DataFolderScope folders)
    {
        Directory.CreateDirectory(folders.Data);
        return folders.Data;
    }

    [Fact]
    public void Creates_the_file_and_leaves_nothing_else()
    {
        using var folders = DataFolderScope.Create();
        var path = Path.Combine(NewFolder(folders), "file");

        Assert.True(AtomicFile.TryCreate(path, [1, 2, 3]));

        Assert.Equal([1, 2, 3], File.ReadAllBytes(path));
        Assert.Equal([path], folders.Files());
    }

    [Fact]
    public void Does_not_replace_a_file_that_is_there()
    {
        using var folders = DataFolderScope.Create();
        var path = Path.Combine(NewFolder(folders), "file");
        File.WriteAllBytes(path, [9]);

        Assert.False(AtomicFile.TryCreate(path, [1, 2, 3]));

        Assert.Equal([9], File.ReadAllBytes(path));
        Assert.Equal([path], folders.Files());
    }

    // The rule every state file leans on: parallel builds on a new machine
    // must end up with one machine identifier between them.
    [Fact]
    public void Lets_exactly_one_of_several_writers_win()
    {
        using var folders = DataFolderScope.Create();
        var path = Path.Combine(NewFolder(folders), "file");
        var created = new bool[32];

        Parallel.For(0, created.Length, i => created[i] = AtomicFile.TryCreate(path, [(byte)i]));

        var winner = Assert.Single(Enumerable.Range(0, created.Length), i => created[i]);
        Assert.Equal([(byte)winner], File.ReadAllBytes(path));
        Assert.Equal([path], folders.Files());
    }

    [Fact]
    public void Leaves_no_temporary_file_when_the_write_fails()
    {
        using var folders = DataFolderScope.Create();
        var path = Path.Combine(NewFolder(folders), "file");

        Assert.Throws<InvalidOperationException>(() =>
            AtomicFile.TryCreate(path, _ => throw new InvalidOperationException("The write failed.")));

        Assert.Empty(folders.Files());
    }

    [Fact]
    public void Deletes_a_file_and_does_not_mind_a_missing_one()
    {
        using var folders = DataFolderScope.Create();
        var path = Path.Combine(NewFolder(folders), "file");
        File.WriteAllBytes(path, [9]);

        Assert.True(AtomicFile.TryDelete(path));
        Assert.False(File.Exists(path));
        Assert.True(AtomicFile.TryDelete(path));
    }
}
