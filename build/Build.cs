using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Nuke.Common;
using Nuke.Common.CI;
using Nuke.Common.Execution;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.EnvironmentInfo;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;

class Build : NukeBuild
{
    /// Support plugins are available for:
    ///   - JetBrains ReSharper        https://nuke.build/resharper
    ///   - JetBrains Rider            https://nuke.build/rider
    ///   - Microsoft VisualStudio     https://nuke.build/visualstudio
    ///   - Microsoft VSCode           https://nuke.build/vscode


    [Parameter("configuration")]
    public string Configuration { get; set; }

    [Parameter("skip-tests")]
    public bool SkipTests { get; set; }

    [Parameter("force-nuget-version")]
    public string ForceNugetVersion { get; set; }

    [Parameter("skip-previewer")]
    public bool SkipPreviewer { get; set; }

    public class BuildParameters
    {
        public string Configuration { get; }

        public string MSBuildSolution { get; }

        public string Version { get; }


        public BuildParameters(Build b)
        {
            Configuration = b.Configuration ?? "Release";
            MSBuildSolution = RootDirectory / "Avalonia.BuildServices.sln";

            // VERSION
            Version = b.ForceNugetVersion ?? GetVersion();
        }

        string GetVersion()
        {
            var xdoc = XDocument.Load(RootDirectory / "BuildTask/Avalonia.BuildServices.csproj");
            return xdoc.Descendants().First(x => x.Name.LocalName == "Version").Value;
        }
    }

    BuildParameters Parameters { get; set; }

    protected override void OnBuildInitialized()
    {
        base.OnBuildInitialized();

        Parameters = new BuildParameters(this);
    }

    public static int Main () => Execute<Build>(x => x.CreatePackage);

    Target CreatePackage => _ => _
        .Executes(() =>
        {
            DotNetTasks.DotNetPack(settings => settings.SetProject(Parameters.MSBuildSolution).SetConfiguration(Parameters.Configuration));

            RefAssemblyGenerator.GenerateRefAsmsInPackage(Path.Combine(RootDirectory, "BuildTask/bin/Release/Avalonia.BuildServices." + Parameters.Version + ".nupkg"));
        });

}