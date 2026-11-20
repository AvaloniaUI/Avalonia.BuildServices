using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Nuke.Common.Utilities;

public class RefAssemblyGenerator
{
    class Resolver : DefaultAssemblyResolver, IAssemblyResolver
    {
        private readonly string _dir;
        Dictionary<string, AssemblyDefinition> _cache = new();

        public Resolver(string dir)
        {
            _dir = dir;
        }

        public override AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
        {
            if (_cache.TryGetValue(name.Name, out var asm))
                return asm;
            var path = Path.Combine(_dir, name.Name + ".dll");
            if (File.Exists(path))
                return _cache[name.Name] = AssemblyDefinition.ReadAssembly(path, parameters);
            return base.Resolve(name, parameters);
        }
    }

    public static void PatchRefAssembly(string file)
    {
        var def = AssemblyDefinition.ReadAssembly(file, new ReaderParameters
        {
            ReadWrite = true,
            InMemory = true,
            ReadSymbols = true,
            SymbolReaderProvider = new DefaultSymbolReaderProvider(false),
            AssemblyResolver = new Resolver(Path.GetDirectoryName(file))
        });

        def.Name = new AssemblyNameDefinition(
            "Avalonia.BuildServices."
            + Guid.NewGuid().ToString().Replace("-", ""),
            new Version(0, 0, 0));

        def.Write(file, new WriterParameters()
        {
            WriteSymbols = def.MainModule.HasSymbols,
            SymbolWriterProvider = new EmbeddedPortablePdbWriterProvider(),
            DeterministicMvid = def.MainModule.HasSymbols
        });
    }

    public static void GenerateRefAsmsInPackage(string packagePath)
    {
        using (var archive = new ZipArchive(File.Open(packagePath, FileMode.Open, FileAccess.ReadWrite),
                   ZipArchiveMode.Update))
        {
            var libs = archive.Entries.Where(e => e.FullName.StartsWith("tools/") && e.FullName.Contains("Avalonia") && e.FullName.EndsWith(".dll"))
                .Select((e => new { s = e.FullName.Split('/'), e = e }))
                .Select(e => new { Tfm = e.s[1], Name = e.s[2], Entry = e.e })
                .GroupBy(x => x.Tfm);

            foreach(var tfm in libs)
                using (Helpers.UseTempDir(out var temp))
                {
                    foreach (var l in tfm)
                    {
                        l.Entry.ExtractToFile(Path.Combine(temp, l.Name));
                    }

                    foreach (var l in tfm)
                    {
                        l.Entry.Delete();
                        PatchRefAssembly(Path.Combine(temp, l.Name));
                    }

                    foreach (var l in tfm)
                    {
                        archive.CreateEntryFromFile(Path.Combine(temp, l.Name), $"tools/{l.Tfm}/{l.Name}");
                    }
                }
        }
    }
}

class Helpers
{
    public static IDisposable UseTempDir(out string dir)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(path);
        dir = path;
        return DelegateDisposable.CreateBracket(null, () =>
        {
            try
            {
                Directory.Delete(path, true);
            }
            catch
            {
                // ignore
            }
        });
    }
}