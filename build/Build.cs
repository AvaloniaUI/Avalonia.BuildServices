using System.Linq;
using System.Xml.Linq;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Serilog;

class Build : NukeBuild
{
    [Parameter("configuration")]
    readonly string Configuration = "Release";

    [Parameter]
    readonly AbsolutePath Output = RootDirectory / "artifacts" / "packages";

    [NuGetPackage("dotnet-ilrepack", "ILRepackTool.dll", Framework = "net8.0")] readonly Tool IlRepackTool;

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

    string GetVersion()
    {
        var xdoc = XDocument.Load(RootDirectory / "BuildTask/Avalonia.BuildServices.csproj");
        return xdoc.Descendants().First(x => x.Name.LocalName == "Version").Value;
    }
}