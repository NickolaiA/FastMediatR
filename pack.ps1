<#
.SYNOPSIS
    Builds the FastMediatR NuGet packages and prints what will be built before packing.

.DESCRIPTION
    Reads the single-source-of-truth version from Directory.Build.props, prints a summary of
    the packable projects and what each package bundles, then runs `dotnet pack` for the
    solution in Release. Finally lists the produced .nupkg / .snupkg files with sizes.

.PARAMETER Configuration
    Build configuration. Defaults to Release (packages should always be Release).

.PARAMETER Output
    Output directory for the packages. Defaults to ./artifacts.

.PARAMETER VersionSuffix
    Optional pre-release suffix (e.g. "preview.1" -> 0.1.0-preview.1). Omit for a stable build.

.PARAMETER NoBuild
    Skip the implicit build step (assumes the solution is already built).

.EXAMPLE
    ./pack.ps1
    Builds stable packages into ./artifacts.

.EXAMPLE
    ./pack.ps1 -VersionSuffix preview.1
    Builds 0.1.0-preview.1 packages.
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Output = 'artifacts',
    [string]$VersionSuffix,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$solution = Join-Path $repoRoot 'FastMediatR.slnx'
$propsFile = Join-Path $repoRoot 'Directory.Build.props'
$outputDir = if ([System.IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $repoRoot $Output }

function Write-Header($text) {
    Write-Host ''
    Write-Host ('=' * 72) -ForegroundColor DarkCyan
    Write-Host "  $text" -ForegroundColor Cyan
    Write-Host ('=' * 72) -ForegroundColor DarkCyan
}

# ── Resolve version from Directory.Build.props (single source of truth) ──────────
[xml]$props = Get-Content $propsFile
$baseVersion = ($props.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
if (-not $baseVersion) { throw "Could not read <Version> from $propsFile" }

$fullVersion = if ($VersionSuffix) { "$baseVersion-$VersionSuffix" } else { $baseVersion }

# ── Resolve git + SDK info (best-effort) ─────────────────────────────────────────
function Invoke-Safe([scriptblock]$command, $fallback) {
    try {
        $old = $ErrorActionPreference
        $ErrorActionPreference = 'SilentlyContinue'
        $result = & $command 2>$null
        $ErrorActionPreference = $old
        if ([string]::IsNullOrWhiteSpace($result)) { $fallback } else { ($result | Select-Object -First 1).ToString().Trim() }
    } catch {
        $fallback
    }
}

$gitSha = Invoke-Safe { git -C $repoRoot rev-parse --short HEAD } 'n/a'
$gitBranch = Invoke-Safe { git -C $repoRoot rev-parse --abbrev-ref HEAD } 'n/a'
$sdkVersion = Invoke-Safe { dotnet --version } 'unknown'

# ── Packable projects (what will be produced) ────────────────────────────────────
$packages = @(
    [pscustomobject]@{
        Package  = 'FastMediatR'
        Project  = 'src/FastMediatR/FastMediatR.csproj'
        Bundles  = 'Core + Extensions.DI (lib), Generator (analyzers/dotnet/cs)'
    }
    [pscustomobject]@{
        Package  = 'FastMediatR.Telemetry'
        Project  = 'src/FastMediatR.Telemetry/FastMediatR.Telemetry.csproj'
        Bundles  = 'OpenTelemetry TracingBehavior (depends on FastMediatR)'
    }
)

# ── Print pre-build summary ──────────────────────────────────────────────────────
Write-Header 'FastMediatR — NuGet pack'
Write-Host ("  Version        : {0}" -f $fullVersion) -ForegroundColor Green
Write-Host ("  Configuration  : {0}" -f $Configuration)
Write-Host ("  Output         : {0}" -f $outputDir)
Write-Host ("  Solution       : {0}" -f $solution)
Write-Host ("  .NET SDK       : {0}" -f $sdkVersion)
Write-Host ("  Git branch     : {0}" -f $gitBranch)
Write-Host ("  Git commit     : {0}" -f $gitSha)
Write-Host ("  Symbols        : snupkg (IncludeSymbols=true)")
Write-Host ("  Validation     : EnablePackageValidation=true")

Write-Header 'Packages to be produced'
$packages | Format-Table -AutoSize Package, Project, Bundles | Out-Host

# ── Clean output dir ─────────────────────────────────────────────────────────────
if (Test-Path $outputDir) {
    Get-ChildItem $outputDir -Filter '*.*nupkg' -ErrorAction SilentlyContinue | Remove-Item -Force
} else {
    New-Item -ItemType Directory -Path $outputDir | Out-Null
}

# ── Pack ─────────────────────────────────────────────────────────────────────────
Write-Header 'Running dotnet pack'
$packArgs = @(
    'pack', $solution,
    '-c', $Configuration,
    '-o', $outputDir,
    "-p:Version=$fullVersion"
)
if ($NoBuild) { $packArgs += '--no-build' }

Write-Host ("  dotnet {0}" -f ($packArgs -join ' ')) -ForegroundColor DarkGray
& dotnet @packArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed with exit code $LASTEXITCODE." }

# ── Print results ────────────────────────────────────────────────────────────────
Write-Header 'Build output'
$produced = Get-ChildItem $outputDir -Filter '*.*nupkg' | Sort-Object Name
if (-not $produced) { throw "No packages were produced in $outputDir." }

$produced |
    Select-Object Name, @{ N = 'Size'; E = { '{0:N1} KB' -f ($_.Length / 1KB) } }, @{ N = 'Modified'; E = { $_.LastWriteTime.ToString('s') } } |
    Format-Table -AutoSize | Out-Host

Write-Host ''
Write-Host ("  {0} file(s) written to {1}" -f $produced.Count, $outputDir) -ForegroundColor Green
Write-Host ''
Write-Host '  To publish to nuget.org:' -ForegroundColor Yellow
Write-Host ("    dotnet nuget push `"{0}/FastMediatR.{1}.nupkg`" --api-key <KEY> --source https://api.nuget.org/v3/index.json" -f $Output, $fullVersion)
Write-Host '  (.snupkg symbol packages are pushed automatically alongside the .nupkg.)'
Write-Host ''
