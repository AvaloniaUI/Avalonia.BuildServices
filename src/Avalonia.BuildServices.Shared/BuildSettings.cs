namespace Avalonia.Telemetry;

// What this build is set to: inputs the task reads from MSBuild properties.
public readonly struct BuildSettings
{
    public BuildSettings(ProjectLanguage language, BuildConfiguration configuration, bool publishing,
        bool? compiledBindingsByDefault, bool? xamlCompilation, bool? nameGenerator)
    {
        Language = language;
        Configuration = configuration;
        Publishing = publishing;
        CompiledBindingsByDefault = compiledBindingsByDefault;
        XamlCompilation = xamlCompilation;
        NameGenerator = nameGenerator;
    }

    public ProjectLanguage Language { get; }

    public BuildConfiguration Configuration { get; }

    // A dotnet publish rather than a build.
    public bool Publishing { get; }

    // Avalonia's own switches, as its props and targets resolved them. Null
    // when the property was empty, which leaves an older Avalonia's default.
    public bool? CompiledBindingsByDefault { get; }

    public bool? XamlCompilation { get; }

    public bool? NameGenerator { get; }
}