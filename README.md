# Avalonia Telemetry

## Overview
Avalonia collects anonymous build-time usage data to understand how the framework is used and to focus development where it delivers the most value. This information guides our priorities, ensures we invest in the right features, and helps maintain a stable, performant ecosystem.

## Data Collection
When is data collected?
* Build time only – telemetry runs exclusively during compilation
* No runtime impact – deployed applications are unaffected

### What is collected?
Minimal, anonymised information (hashed with SHA256 where applicable):
* Build timestamp
* Project name (hashed)
* Output type, target framework, runtime identifier
* Project language (C#, F# or Visual Basic, from the project file's extension), build configuration (Debug, Release, or Other for a custom name), and whether the build was a `dotnet publish`
* Whether the app, published as the build is configured, would be trimmed, and whether all of its code would be compiled ahead of time (Native AOT, iOS device builds, or WebAssembly and Android AOT), read from each platform's trimming and AOT settings
* Which of Avalonia's own build switches are on: compiled bindings by default, XAML compilation, and the name generator
* Avalonia version in use, and the Avalonia XPF version for XPF projects
* Avalonia licence tier (Community, Plus, Pro, Enterprise, Trial): the highest found on the machine, and the one the project's own licence check used
* Which licensed Avalonia packages the project references (for example `Avalonia.Controls.Charts`)
* Anonymous machine identifier (a random GUID created on first use)
* Development environment (Visual Studio, Rider, VS Code, CLI), and for command-line builds whether they ran in Visual Studio's or Rider's terminal. A build in VS Code's terminal counts as VS Code, since VS Code runs its own builds there
* Operating system name and release (for example Windows 10.0.26200, macOS 26.5 or Ubuntu 24.04) and processor architecture
* On Linux, the desktop environment (GNOME, KDE and others from a fixed list) and display server (Wayland or X11) the build ran under, and whether that Linux runs in the Windows Subsystem for Linux
* CI environment detection (Azure DevOps, GitHub Actions, etc.), whether the build ran in a container, and whether it set `ContinuousIntegrationBuild`
* What kind of machine it is, as two numbers from 0 to 100 worked out once when the machine identifier is created: how likely it is to be a virtual machine, and how likely it is to be a server or cloud machine rather than someone's desktop or laptop
* The machine's hardware class: the number of logical processors, and the installed memory rounded to a standard size in gigabytes
* Whether an AI coding agent ran the build, and which one (for example Claude Code, Codex, OpenCode, Gemini CLI or Junie), recognised from the markers those tools set, using the rules of Vercel's open-source [detect-agent](https://github.com/vercel/detect-agent). Only a name from a fixed list is sent, or "Other" for an agent that names itself but is not on the list
* Whether the machine's first build was started from someone's logged-in desktop session, rather than by a background service, a remote shell or a container (true or false), worked out at the same time as the machine kind
* The version of this package

### What is not collected?
* Source code or project contents
* Personal information
* File paths or directory structures
* Runtime application data or network requests
* Email addresses, names, or account information
* Machine names or user names, in any form, hashed or not
* Environment variable values: a few are read to recognise IDEs, containers, CI systems, desktop sessions and AI coding agents, and only the result is sent
* Hardware details: the machine-kind check reads the firmware's maker, model, version and chassis type and its virtual-machine bit, the CPU's hypervisor flag, the Arm device tree's hypervisor entry, the list of virtual devices, whether there is a battery, and whether the operating system is a desktop or server edition. It sends only the two 0 to 100 results. No serial numbers, MAC addresses or hardware IDs are read
* Container detection reads whether a few well-known marker files exist and whether the process is in the root PID namespace. Only true or false is sent

## Privacy & Security
* **Anonymous**: No personally identifiable information is collected
* **Hashed**: Sensitive values are SHA256 hashed
* **EU Storage**: Data is stored in Western Europe
* **Build-time only**: No collection in deployed apps
* **Stays in the user profile**: The machine identifier, unsent records and log live under the user's local application data folder and nowhere else. A build under an account with no profile, such as a Windows service whose profile is not loaded or a container user without a home directory, sends no telemetry at all; the build task never writes into the project directory or a temporary folder
* **GDPR compliant**: Processed under legitimate interest with full compliance measures

### Opting Out
Set the environment variable:
`AVALONIA_TELEMETRY_OPTOUT=1`

A paid licence allows opting out; Community and trial licences do not. The build task reads the `feature:telemetry-optout` flag the Avalonia portal places in every paid licence, whether found in a signed-in tool's ticket, in the cache of an online key, or in the project's own key. Community and trial keys carry no flags at all, so a licence whose tier is Community or Trial counts as one without the flag. A paid-tier licence that carries no flags at all, such as an older key or a library key, is given the benefit of the doubt and may opt out. Tickets on the machine count for every project built on it. The variable is ignored only when every licence found requires telemetry; one licence that may opt out, the project's own or any ticket, is enough. A build with no Avalonia licence at all is always free to opt out.

#### Before opting out, please consider:
This data is our only reliable way to track active versions, platforms, and development environments. It lets us anticipate ecosystem changes and prioritise fixes and features that benefit everyone.

If you choose to disable telemetry, we welcome support through other means, for example, an [Avalonia Accelerate](https://avaloniaui.net/accelerate#pricing) licence, [donations](https://github.com/sponsors/AvaloniaUI), or contributions to code, documentation, or testing. Open source thrives on fairness, and we see anonymous telemetry as a reasonable exchange for a free, high-quality framework.

For full details, see our [Privacy Policy](https://avaloniaui.net/legal-center/privacy-policy).

## Why Open Source?
Transparency matters. By publishing the telemetry code, you can:
* Audit exactly what we collect
* Verify our privacy statements
* Understand the system’s implementation

## Industry Context
Anonymous build-time telemetry is standard across major .NET frameworks and tools, including the .NET SDK itself, Uno Platform, and WinUI. It is a proven, low-impact way to guide development effectively.

## Contributing
You are welcome to raise issues, suggestions, or security concerns. Pull requests will not be accepted. 

Licence
MIT Licence. 
