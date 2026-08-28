[CmdletBinding()]
param(
    [string]$RepoPath = '',
    [string]$RimTalkDir = $env:RIMTALK_DIR,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

# Windows PowerShell 5.1 can evaluate $PSScriptRoot as empty when it is used
# directly as a parameter default. Resolve the default after parameter binding.
if ([string]::IsNullOrWhiteSpace($RepoPath)) {
    $RepoPath = $PSScriptRoot
}

if ([string]::IsNullOrWhiteSpace($RepoPath)) {
    throw 'Repository path could not be resolved. Pass -RepoPath explicitly.'
}

function Resolve-RimTalkAssembly {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $null
    }

    $candidates = @(
        (Join-Path $Path '1.6\Assemblies\RimTalk.dll'),
        (Join-Path $Path 'Assemblies\RimTalk.dll'),
        (Join-Path $Path 'RimTalk.dll')
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    return $null
}

if (-not (Test-Path -LiteralPath $RepoPath -PathType Container)) {
    throw "Repository path does not exist: $RepoPath"
}

$resolvedRepo = (Resolve-Path -LiteralPath $RepoPath).Path
$project = Join-Path $resolvedRepo 'Source\RimTalk.TTS.csproj'

if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    throw "RimTalk.TTS.csproj was not found: $project"
}

$rimTalkAssembly = Resolve-RimTalkAssembly -Path $RimTalkDir
if (-not $rimTalkAssembly) {
    throw @"
RimTalk.dll could not be found.
Pass the RimTalk mod root with -RimTalkDir, for example:
  .\build.cmd -RimTalkDir "F:\SteamLibrary\steamapps\common\RimWorld\Mods\3551203752"
You can also set the RIMTALK_DIR environment variable.
"@
}

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    throw 'dotnet SDK was not found in PATH.'
}

Write-Host "Repository:       $resolvedRepo"
Write-Host "RimTalk assembly: $rimTalkAssembly"
Write-Host "Project:          $project"
Write-Host "Configuration:    $Configuration"

& $dotnet.Source build $project `
    --configuration $Configuration `
    "/p:RimTalkAssemblyPath=$rimTalkAssembly"

$buildExitCode = $LASTEXITCODE
if ($buildExitCode -ne 0) {
    throw "dotnet build failed with exit code $buildExitCode"
}

$outputDll = Join-Path $resolvedRepo '1.6\Assemblies\RimTalk.TTS.dll'
if (-not (Test-Path -LiteralPath $outputDll -PathType Leaf)) {
    throw "Build reported success but output DLL was not found: $outputDll"
}

Write-Host ""
Write-Host "Build succeeded: $outputDll"
