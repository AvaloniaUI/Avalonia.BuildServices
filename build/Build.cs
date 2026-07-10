using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using NukeExtensions;
using Serilog;

class Build : NukeBuild
{
    [Parameter("configuration")]
    readonly string Configuration = "Release";

    [Parameter]
    readonly AbsolutePath Output = RootDirectory / "artifacts" / "packages";

    [NuGetPackage("dotnet-ilrepack", "ILRepackTool.dll", Framework = "net8.0")] readonly Tool IlRepackTool;
    [NuGetPackage("CycloneDX", "CycloneDX.dll", Framework = "net9.0")] readonly Tool CycloneDx;

    AbsolutePath BuildServicesProject => RootDirectory / "BuildTask" / "Avalonia.BuildServices.csproj";

    public static int Main () => Execute<Build>(x => x.CreatePackage);

    Target OutputParameters => _ => _
        .Executes(() =>
        {
            Log.Information("Configuration: {Configuration}", Configuration);
            Log.Information("Output: {AbsolutePath}", Output);
            Log.Information("Version: {GetVersion}", GetVersion());
        });

    Target CleanArtifacts => _ => _
        .Executes(() =>
        {
            Output.CreateOrCleanDirectory();
        });

    Target RunBuild => _ => _
        .DependsOn(OutputParameters)
        .Executes(() =>
        {
            DotNetTasks.DotNetBuild(settings => settings
                .SetProjectFile(BuildServicesProject)
                .SetConfiguration(Configuration)
                .SetVersion(GetVersion()));
        });

    Target MergeLicensing => _ => _
        .DependsOn(RunBuild)
        .Executes(() =>
        {
            var outputDir = BuildServicesProject.Parent / "bin" / Configuration / "netstandard2.0";
            var buildServicesDll = outputDir / "Avalonia.BuildServices.dll";
            var licensingDll = outputDir / "AvaloniaUI.Licensing.dll";

            IlRepackTool.Invoke(
                $"""/internalize /parallel /ndebug /out:"{buildServicesDll}" "{buildServicesDll}" {licensingDll} """,
                outputDir);
        });

    Target CreatePackage => _ => _
        .DependsOn(OutputParameters)
        .DependsOn(CleanArtifacts)
        .DependsOn(RunBuild)
        .DependsOn(MergeLicensing)
        .Executes(() =>
        {
            DotNetTasks.DotNetPack(settings => settings
                .SetNoBuild(true)
                .SetVersion(GetVersion())
                .SetProject(BuildServicesProject)
                .SetConfiguration(Configuration)
                .SetOutputDirectory(Output));

            var pkg= Output.GlobFiles("*.nupkg").Single();
            RefAssemblyGenerator.GenerateRefAsmsInPackage(pkg);
        });

    // Generates a CycloneDX SBOM (EU Cyber Resilience Act evidence) for the shipped package and
    // embeds it into the .nupkg at _manifest/cyclonedx/bom.cdx.json. TriggeredBy makes the default
    // CreatePackage target produce it with no build-command changes; it must run after
    // RefAssemblyGenerator so the recorded hashes are those of the binaries actually shipped.
    Target CreateSbom => _ => _
        .DependsOn(CreatePackage)
        .TriggeredBy(CreatePackage)
        .Executes(() =>
        {
            var pkg = Output.GlobFiles("*.nupkg").Single();
            var sbomOutput = RootDirectory / "artifacts" / "sbom";
            sbomOutput.CreateOrCleanDirectory();

            // The Collector's output is bundled into the package's tools/ folder, so it's a
            // constituent of the shipped package and its dependencies belong in the SBOM.
            SbomGenerator.GenerateForPackage(
                CycloneDx,
                RootDirectory,
                pkg,
                sbomOutput,
                GetVersion(),
                "Avalonia.BuildServices",
                new[] { "Avalonia.BuildServices", "Avalonia.BuildServices.Collector" },
                projectSearchDirs: new[] { "BuildTask", "Avalonia.BuildServices.Collector" });

            AddMergedLicensingComponent(sbomOutput / $"Avalonia.BuildServices.{GetVersion()}.cdx.json", pkg);
        });

    // AvaloniaUI.Licensing is IL-merged into Avalonia.BuildServices.dll (MergeLicensing) and
    // referenced with PrivateAssets="All", so the shared generator can't see it: cyclonedx-dotnet
    // excludes it as a dev dependency, it's absent from the shipped nuspec (which would otherwise
    // re-add it), and the merged bytes live inside the package's primary assembly, which the
    // package-content scan deliberately skips. Its code ships regardless, so record it explicitly,
    // in both the standalone SBOM and the copy embedded in the package.
    void AddMergedLicensingComponent(AbsolutePath sbomPath, AbsolutePath nupkgPath)
    {
        var licensingVersion = XDocument.Load(BuildServicesProject).Descendants()
            .First(x => x.Name.LocalName == "PackageReference" &&
                        x.Attribute("Include")?.Value == "AvaloniaUI.Licensing")
            .Attribute("Version")!.Value;
        var purl = $"pkg:nuget/AvaloniaUI.Licensing@{licensingVersion}";

        var doc = JsonNode.Parse(File.ReadAllText(sbomPath))!.AsObject();
        var components = doc["components"]!.AsArray();

        // If the shared generator ever starts emitting the component itself (e.g. the reference
        // moves off PrivateAssets="All" and licensing lands back in the shipped nuspec, where the
        // generator's cross-check re-adds it), this manual step is obsolete - skip it rather than
        // emit a duplicate bom-ref.
        if (components.Any(c => c?["name"]?.GetValue<string>() == "AvaloniaUI.Licensing"))
        {
            Log.Warning("SBOM: AvaloniaUI.Licensing is already present in the generated SBOM - " +
                        "the manual AddMergedLicensingComponent step is obsolete and can be removed.");
            return;
        }

        components.Add(new JsonObject
        {
            ["type"] = "library",
            ["bom-ref"] = purl,
            ["name"] = "AvaloniaUI.Licensing",
            ["version"] = licensingVersion,
            ["purl"] = purl,
            ["scope"] = "required"
        });

        var rootRef = doc["metadata"]!["component"]!["bom-ref"]!.GetValue<string>();
        var rootNode = doc["dependencies"]!.AsArray().OfType<JsonObject>()
            .First(d => d["ref"]!.GetValue<string>() == rootRef);
        rootNode["dependsOn"]!.AsArray().Add(purl);

        var sbomJson = doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(sbomPath, sbomJson);

        // Replace the copy GenerateForPackage embedded so the two stay identical. The package is
        // not signed at this point (nuget.org signs server-side on push), and "json" is already
        // registered in [Content_Types].xml by the original embed.
        using var zip = ZipFile.Open(nupkgPath, ZipArchiveMode.Update);
        const string entryPath = "_manifest/cyclonedx/bom.cdx.json";
        zip.GetEntry(entryPath)!.Delete();
        using var entryStream = zip.CreateEntry(entryPath).Open();
        using var writer = new StreamWriter(entryStream);
        writer.Write(sbomJson);
    }

    string GetVersion()
    {
        var xdoc = XDocument.Load(RootDirectory / "BuildTask/Avalonia.BuildServices.csproj");
        return xdoc.Descendants().First(x => x.Name.LocalName == "Version").Value;
    }
}