param([ValidateSet('Baseline', 'Rendering', 'Upstream', 'ForkRendering')][string] $Target = 'Rendering')
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$cacheRoot = Join-Path $projectRoot '.cache'
$names = @('NUGET_PACKAGES', 'NUGET_HTTP_CACHE_PATH', 'VSINSTALLDIR', 'PATH', 'LC_ALL', 'LANG')
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    $env:NUGET_PACKAGES = Join-Path $cacheRoot 'packages'
    $env:NUGET_HTTP_CACHE_PATH = Join-Path $cacheRoot 'http'
    if ($Target -eq 'Upstream') {
        $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
        $installation = & $vswhere -latest -version '[17.2,18.0)' -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
        if (-not $installation) { throw 'Visual Studio 2022 with C++ tools is required.' }
        $env:VSINSTALLDIR = $installation + '\'
        $env:PATH = (Join-Path $installation 'MSBuild/Current/Bin') + ';' + $env:PATH
        $env:LC_ALL = 'C'
        $env:LANG = 'C'
        Push-Location (Join-Path $projectRoot 'upstream/wpf')
        try {
            # Diagnostic baseline: preserve compiler checks, skip incompatible upstream analyzers.
            & ./build.cmd -configuration Release -platform x64 /p:RunAnalyzersDuringBuild=false /p:SystemSecurityCryptographyXmlPackageVersion=10.0.14
            if ($LASTEXITCODE -ne 0) { throw "WPF build failed with exit code $LASTEXITCODE" }
        } finally { Pop-Location }
    } elseif ($Target -eq 'ForkRendering') {
        $package = Join-Path $projectRoot 'upstream/wpf/artifacts/packaging/Release/Microsoft.DotNet.Wpf.GitHub'
        $managed = Join-Path $package 'lib/net10.0'
        $native = Join-Path $package 'runtimes/win-x64/native'
        if (!(Test-Path "$managed/PresentationCore.dll") -or !(Test-Path "$native/wpfgfx_cor3.dll")) {
            throw 'Build the upstream WPF package first with -Target Upstream.'
        }
        $publish = Join-Path $projectRoot 'artifacts/fork-probe'
        & dotnet publish (Join-Path $projectRoot 'samples/RenderingProbe/RenderingProbe.csproj') -c Release -r win-x64 --self-contained -o $publish
        if ($LASTEXITCODE -ne 0) { throw 'RenderingProbe publish failed' }
        Copy-Item "$managed/*.dll", "$native/*.dll" -Destination $publish -Force
        & (Join-Path $publish 'RenderingProbe.exe') (Join-Path $projectRoot 'artifacts/rendering-fork')
        if ($LASTEXITCODE -ne 0) { throw 'Fork rendering verification failed' }
    } else {
        $sample = if ($Target -eq 'Baseline') { 'Counter' } else { 'RenderingProbe' }
        & dotnet build (Join-Path $projectRoot "samples/$sample/$sample.csproj") -c Release
        if ($LASTEXITCODE -ne 0) { throw "$sample build failed" }
        $assembly = Join-Path $projectRoot "samples/$sample/bin/Release/net10.0-windows/$sample.dll"
        $argument = if ($Target -eq 'Baseline') { '--smoke' } else { Join-Path $projectRoot 'artifacts/rendering' }
        & dotnet $assembly $argument
        if ($LASTEXITCODE -ne 0) { throw "$sample verification failed" }
    }
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}


