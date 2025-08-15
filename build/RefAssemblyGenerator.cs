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

    static bool HasPrivateApi(IEnumerable<CustomAttribute> attrs) => attrs.Any(a =>
        a.AttributeType.FullName == "Avalonia.Metadata.PrivateApiAttribute");

    static void ProcessType(TypeDefinition type, MethodReference obsoleteCtor)
    {
        foreach (var nested in type.NestedTypes)
            ProcessType(nested, obsoleteCtor);

        var hideMethods = (type.IsInterface && type.Name.EndsWith("Impl"))
                          || HasPrivateApi(type.CustomAttributes);

        var injectMethod = hideMethods
                           || type.CustomAttributes.Any(a =>
                               a.AttributeType.FullName == "Avalonia.Metadata.NotClientImplementableAttribute");



        if (injectMethod)
        {
            type.Methods.Add(new MethodDefinition(
                "(This interface or abstract class is -not- implementable by user code !)",
                MethodAttributes.Assembly
                | MethodAttributes.Abstract
                | MethodAttributes.NewSlot
                | MethodAttributes.HideBySig, type.Module.TypeSystem.Void));
        }

        var forceUnstable = type.CustomAttributes.FirstOrDefault(a =>
            a.AttributeType.FullName == "Avalonia.Metadata.UnstableAttribute");

        foreach (var m in type.Methods)
        {
            if (hideMethods || HasPrivateApi(m.CustomAttributes))
            {
                var dflags = MethodAttributes.Public | MethodAttributes.Family | MethodAttributes.FamORAssem |
                             MethodAttributes.FamANDAssem | MethodAttributes.Assembly;
                m.Attributes = ((m.Attributes | dflags) ^ dflags) | MethodAttributes.Assembly;
            }
            MarkAsUnstable(m, obsoleteCtor, forceUnstable);
        }

        foreach (var m in type.Properties)
            MarkAsUnstable(m, obsoleteCtor, forceUnstable);
        foreach (var m in type.Events)
            MarkAsUnstable(m, obsoleteCtor, forceUnstable);
    }

    static void MarkAsUnstable(IMemberDefinition def, MethodReference obsoleteCtor, ICustomAttribute unstableAttribute)
    {
        if (def.CustomAttributes.Any(a => a.AttributeType.FullName == "System.ObsoleteAttribute"))
            return;

        unstableAttribute = def.CustomAttributes.FirstOrDefault(a =>
            a.AttributeType.FullName == "Avalonia.Metadata.UnstableAttribute") ?? unstableAttribute;

        if (unstableAttribute is null)
            return;

        var message = unstableAttribute.ConstructorArguments.FirstOrDefault().Value?.ToString();
        if (string.IsNullOrEmpty(message))
        {
            message = "This is a part of unstable API and can be changed in minor releases. Consider replacing it with alternatives or reach out developers on GitHub.";
        }

        def.CustomAttributes.Add(new CustomAttribute(obsoleteCtor)
        {
            ConstructorArguments =
            {
                new CustomAttributeArgument(obsoleteCtor.Module.TypeSystem.String, message)
            }
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