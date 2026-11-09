# CrossWpf development

Upstream WPF is pinned to v10.0.12, commit 3bbc551873ff9afc718eec20077a9118c15b0688, in upstream/wpf on branch crosswpf/prototype.

For a fresh checkout, restore the pinned WPF source and apply the portability changes:

```powershell
git clone --depth 1 --branch v10.0.12 https://github.com/dotnet/wpf.git upstream/wpf
git -C upstream/wpf apply --check ../../wpf-linux.patch
git -C upstream/wpf apply ../../wpf-linux.patch
```

## Current state

The Counter sample is a Windows WPF reference application. It is not yet a cross-platform implementation. A Windows-only rectangle rendering adapter is implemented in RenderingProbe. A Linux WPF runtime and SDK have not been implemented.

Build and run the baseline from this directory:

```powershell
dotnet build samples/Counter/Counter.csproj -c Release
dotnet samples/Counter/bin/Release/net10.0-windows/Counter.dll --smoke
dotnet samples/Counter/bin/Release/net10.0-windows/Counter.dll
```

The smoke test loads compiled XAML, shows the window, invokes the bound command, drains dispatcher work, verifies the displayed counter and closes the window. It does not simulate physical mouse or keyboard input.

## Porting boundaries confirmed in source

- WindowsBase/System/Windows/Threading/Dispatcher.cs registers DispatcherProcessQueue using RegisterWindowMessage in its static constructor and creates MessageOnlyHwndWrapper in its constructor. Both must be replaced for a Linux dispatcher, along with its message pumping, wakeup, timers and shutdown paths.
- PresentationCore/System/Windows/Media/MediaContext.cs constructs MediaContextNotificationWindow and creates DUCE channels. Replacing only window creation will still enter native composition.
- PresentationCore.csproj directly includes generated MIL commands and graphics exports. An adapter requires a coherent rendering/resource lifecycle, beyond replacing one DLL import.

Paths above are relative to upstream/wpf/src/Microsoft.DotNet.Wpf/src.

## Environment

Windows has .NET SDK 10.0.401 and WindowsDesktop runtime 10.0.12. Visual Studio 2022 has the C++ tools component (confirmed through vswhere). Debian WSL exists, but dotnet and python3 were not found on its PATH. WSL and network operations require execution outside the restricted sandbox.

The curated skill catalog was inspected through GitHub API because python resolves to a Microsoft Store alias. No WPF porting skill was found. No additional skills were installed. GitHub integration was offered and connection is not confirmed. Public source access works through Git.

## Verification rule

Report stock WPF tests separately from fork tests. A Linux milestone requires executing the actual fork and observing its output on Linux. Do not substitute Avalonia controls for the WPF visual tree and call that WPF compatibility.


## Rendering adapter probe

Avalonia 12.1.5 is pinned. Run `./build.ps1 -Target Rendering` to build and verify rendering. This performs real WPF layout, reads DrawingGroup/GeometryDrawing from the WPF visual tree, and reproduces supported operations in Avalonia/Skia. Generated PNGs are under artifacts/rendering. Pixel assertions verify a solid fill, property changes, opacity, translation and clipping after resizing. Unsupported drawing, brush, geometry and pen types fail explicitly.

The probe currently uses the installed Windows WPF runtime. The graphics path still uses WPF native calls. Repainting is explicitly driven by the probe; automatic invalidation and input are not implemented.

Use `./build.ps1 -Target Baseline` for the counter test and `./build.ps1 -Target Upstream` for the WPF source build. The script scopes NuGet cache and VS selection to its process and restores environment values afterwards.

## Upstream build status

The diagnostic Release x64 build completed with zero warnings and errors after installing C++/CLI support for v143. DirectWriteForwarder.dll linked successfully.

The diagnostic upstream build command uses LC_ALL=C and LANG=C for Perl, selects VS 2022 MSBuild explicitly, overrides System.Security.Cryptography.Xml to 10.0.14 to resolve the pinned dependency audit failures, and disables upstream build analyzers (RunAnalyzersDuringBuild=false). It does not represent a clean pass of the upstream analyzer suite. The dependency override was verified by the full diagnostic build.

Run `./build.ps1 -Target ForkRendering` after building upstream WPF to validate the adapter on the locally compiled runtime. The target publishes the Windows probe as self-contained, overlays the package's managed and win-x64 native DLLs into that private directory, and writes verified PNGs to artifacts/rendering-fork. This remains a Windows test and still uses WPF's native Windows components.

## Linux dispatcher probe

The fork uses a managed wakeup event for dispatcher frames on Linux and skips Windows ETW initialization. The headless probe verifies cross-thread queue processing, a DispatcherTimer tick, and shutdown. The Windows rendering probe also passes with the rebuilt fork.

After building WPF, run these commands from the CrossWpf directory in PowerShell:

```powershell
$env:NUGET_PACKAGES = Join-Path $PWD '.cache/packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $PWD '.cache/http'
dotnet publish samples/DispatcherProbe -c Release -r linux-x64 --self-contained -o artifacts/dispatcher-linux
wsl -d Debian -- timeout 15s env DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 ./artifacts/dispatcher-linux/DispatcherProbe
```

The test uses invariant globalization because this Debian installation lacks ICU. The dispatcher probe has no windows, input handling, or Avalonia event-loop integration.

Pass `--layout` after DispatcherProbe to run the experimental WPF layout check. PresentationCore now limits DirectWrite initialization and IMM detection to Windows. WindowsBase skips registry tracing settings on Linux and uses the managed debugger check there. The Linux probe uses DejaVu Sans at 12 DIP with normal style and weight for message font defaults. The probe creates Grid and Rectangle and sets the dimensions. InputManager checks STA on Windows. The Linux probe uses a double-click threshold of 4 units per axis and 500 ms, a black control foreground and disabled keyboard cues. Window registers taskbar messages only on Windows. MediaContext uses Stopwatch on Linux and initializes the native composition channels and notification window on Windows. The headless layout probe uses 96 DPI and skips HWND monitor checks. In Debian under WSL, Grid.Measure and Grid.Arrange produce the expected 40 by 20 DIP rectangle. Text rendering remains to be ported. The dispatcher check still passes independently.

## Linux rendering probe

After building the patched WPF assemblies, publish and run the same Avalonia/Skia probe in Debian. Skia needs the distro's fontconfig runtime libraries. The sample uses invariant globalization for environments without ICU.

```powershell
dotnet publish samples/RenderingProbe -c Release -p:ForkLinux=true -r linux-x64 --self-contained -o artifacts/rendering-linux
wsl -d Debian -- timeout 20s env DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 ./artifacts/rendering-linux/RenderingProbe artifacts/linux-png
```

The probe renders two PNGs from WPF drawings after explicit Measure and Arrange passes. Pixel checks cover fill, opacity, translation, clipping, and resize. The Linux path uses a managed composition lock and renders without a window.
