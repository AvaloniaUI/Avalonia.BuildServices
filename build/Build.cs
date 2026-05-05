using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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

    AbsolutePath BuildServicesProject => RootDirectory / "Avalonia.BuildServices" / "Avalonia.BuildServices.csproj";
    AbsolutePath BuildServicesProtocolProject => RootDirectory / "Avalonia.BuildServices.Protocol" / "Avalonia.BuildServices.Protocol.csproj";

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
                .SetProjectFile(BuildServicesProtocolProject)
                .SetConfiguration(Configuration)
                .SetVersion(GetVersion()));

            DotNetTasks.DotNetBuild(settings => settings
                .SetProjectFile(BuildServicesProject)
                .SetConfiguration(Configuration)
                .SetVersion(GetVersion()));
        });

    Target Merge => _ => _
        .DependsOn(RunBuild)
        .Executes(() =>
        {
            var outputDir = BuildServicesProject.Parent / "bin" / Configuration / "netstandard2.0";
            var buildServicesDll = outputDir / "Avalonia.BuildServices.dll";
            var licensingDll = outputDir / "AvaloniaUI.Licensing.dll";
            var protocolDll = outputDir / "Avalonia.BuildServices.Protocol.dll";

            IlRepackTool.Invoke(
                $"""/internalize /parallel /ndebug /out:"{buildServicesDll}" "{buildServicesDll}" {licensingDll} {protocolDll} """,
                outputDir);

            // Protocol types are now baked into Avalonia.BuildServices.dll. Drop the loose copy
            // so it doesn't get picked up by the default pack output and shipped twice.
            if (File.Exists(protocolDll))
            {
                File.Delete(protocolDll);
            }
        });

    Target CreatePackage => _ => _
        .DependsOn(OutputParameters)
        .DependsOn(CleanArtifacts)
        .DependsOn(RunBuild)
        .DependsOn(Merge)
        .Executes(() =>
        {
            DotNetTasks.DotNetPack(settings => settings
                .SetNoBuild(true)
                .SetVersion(GetVersion())
                .SetProject(BuildServicesProtocolProject)
                .SetConfiguration(Configuration)
                .SetOutputDirectory(Output));

            DotNetTasks.DotNetPack(settings => settings
                .SetNoBuild(true)
                .SetVersion(GetVersion())
                .SetProject(BuildServicesProject)
                .SetConfiguration(Configuration)
                .SetOutputDirectory(Output));

            // Ref-asm generation is only relevant for the MSBuild task package.
            var pkg = Output.GlobFiles("Avalonia.BuildServices.*.nupkg")
                .Single(p => !p.Name.Contains("Protocol"));
            RefAssemblyGenerator.GenerateRefAsmsInPackage(pkg);
        });

    string GetVersion()
    {
        var xdoc = XDocument.Load(RootDirectory / "BuildTask/Avalonia.BuildServices.csproj");
        return xdoc.Descendants().First(x => x.Name.LocalName == "Version").Value;
    }
}