[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$root = [IO.Path]::GetFullPath($PSScriptRoot)
$project = Join-Path $root "src\taskTru.csproj"

function Invoke-DotNet([string[]]$Arguments) {
    Write-Host "dotnet $($Arguments -join ' ')" -ForegroundColor DarkGray
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path -LiteralPath $project)) {
    throw "Project not found: $project"
}

[xml]$projectXml = Get-Content -LiteralPath $project
# Keep the exe names tied to the csproj version so release builds cannot drift.
$parsedVersion = [Version]([string]$projectXml.Project.PropertyGroup.Version)
$version = if ($parsedVersion.Build -gt 0) {
    "$($parsedVersion.Major).$($parsedVersion.Minor).$($parsedVersion.Build)"
}
else {
    "$($parsedVersion.Major).$($parsedVersion.Minor)"
}

$releaseRoot = Join-Path $root "dist"
$stagingRoot = Join-Path $root "artifacts\releases\v$version"
if (Test-Path -LiteralPath $stagingRoot) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null

$assets = [System.Collections.Generic.List[object]]::new()
# GitHub release assets are standalone exe files: portable requires .NET, full includes it.
$deployments = @(
    [pscustomobject]@{ Name = "portable"; Suffix = ""; SelfContained = "false" },
    [pscustomobject]@{ Name = "full"; Suffix = "-full"; SelfContained = "true" }
)
foreach ($architecture in @("x64", "arm64")) {
    $runtime = "win-$architecture"
    foreach ($deployment in $deployments) {
        $assetName = if ($architecture -eq "x64") {
            "taskTru-$version$($deployment.Suffix).exe"
        }
        else {
            "taskTru-$version$($deployment.Suffix)`_$architecture.exe"
        }
        # Publish each runtime into staging first, then copy only the renamed exe files into dist.
        $publishDir = Join-Path $stagingRoot "$runtime-$($deployment.Name)"

        Write-Host ""
        Write-Host "Publishing $assetName" -ForegroundColor Cyan
        $publishArguments = @(
            "publish", $project,
            "--configuration", "Release",
            "--runtime", $runtime,
            "--self-contained", $deployment.SelfContained,
            "--nologo",
            "-p:PublishDir=$publishDir\",
            "-p:PublishSingleFile=true",
            "-p:UseAppHost=true",
            "-p:PublishReadyToRun=false",
            "-p:DebugType=none",
            "-p:DebugSymbols=false",
            "-p:ContinuousIntegrationBuild=true",
            "-p:Deterministic=true"
        )
        if ($deployment.SelfContained -eq "true") {
            # Full builds carry the runtime; compression keeps the single-file exe from becoming too chunky.
            $publishArguments += "-p:EnableCompressionInSingleFile=true"
            $publishArguments += "-p:IncludeNativeLibrariesForSelfExtract=true"
        }

        Invoke-DotNet $publishArguments

        $published = Join-Path $publishDir "taskTru.exe"
        if (-not (Test-Path -LiteralPath $published -PathType Leaf)) {
            throw "Expected publish output not found: $published"
        }

        $assets.Add([pscustomobject]@{
            File = $assetName
            StagedFile = $published
            Architecture = $architecture
            Runtime = $deployment.Name
            MiB = [Math]::Round((Get-Item -LiteralPath $published).Length / 1MB, 2)
        }) | Out-Null
    }
}

New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
# Only clear taskTru release executables; leave unrelated files in dist alone.
Get-ChildItem -LiteralPath $releaseRoot -Filter "taskTru*.exe" -File | Remove-Item -Force
foreach ($asset in $assets) {
    Copy-Item -LiteralPath $asset.StagedFile -Destination (Join-Path $releaseRoot $asset.File)
}

Remove-Item -LiteralPath $stagingRoot -Recurse -Force

Write-Host ""
Write-Host "Release executables created in:" -ForegroundColor Green
Write-Host $releaseRoot
$assets |
    Sort-Object Architecture, Runtime |
    Format-Table File, Architecture, Runtime, MiB -AutoSize
