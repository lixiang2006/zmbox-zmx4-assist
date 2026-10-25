#Requires -Version 7.0
<#
.SYNOPSIS
    Build a self-contained win-x64 release of 造梦盒子·造梦西游四辅助工具.

.DESCRIPTION
    Resolves the release version from Directory.Build.props (the single version source) unless
    -Version is given, publishes the app self-contained, and writes the zip next to the other
    local release packages. The version is embedded in the assembly and in the archive name.

.PARAMETER Version
    Overrides the version from Directory.Build.props. A leading "v" is accepted, so a git tag
    can be passed through unchanged.

.PARAMETER OutputDirectory
    Where the zip is written. Defaults to <repo>/releases.

.PARAMETER DryRun
    Resolves and prints the version and output paths without publishing anything.

.PARAMETER SkipArchive
    Publishes the app but does not create the zip.
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$OutputDirectory,
    [switch]$DryRun,
    [switch]$SkipArchive
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$propsPath = Join-Path $repoRoot "Directory.Build.props"
$projectPath = Join-Path $repoRoot "src\ZmboxZmx4Assist\ZmboxZmx4Assist.csproj"
$publishDirectory = Join-Path $repoRoot "publish\win-x64"
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot "releases" }

if (-not $Version) {
    if (-not (Test-Path -LiteralPath $propsPath)) { throw "找不到版本来源：$propsPath" }
    [xml]$props = Get-Content -LiteralPath $propsPath -Raw
    $Version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
    if (-not $Version) { throw "Directory.Build.props 中没有 <Version>。" }
}

$releaseVersion = $Version.Trim().TrimStart("v", "V")
if ($releaseVersion -notmatch '^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$') {
    throw "版本号 '$Version' 不是合法的语义化版本（例如 1.1.0 或 1.2.0-rc.1）。"
}

$archivePath = Join-Path $OutputDirectory "ZmboxZmx4Assist-$releaseVersion-win-x64.zip"
Write-Host "版本：$releaseVersion"
Write-Host "发布目录：$publishDirectory"
Write-Host "压缩包：$archivePath"

if ($DryRun) {
    Write-Host "DryRun：未调用 dotnet publish。"
    return
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "找不到 dotnet CLI；请先安装 .NET 8 SDK。"
}

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}

& dotnet publish $projectPath -c Release -r win-x64 --self-contained true -p:Version=$releaseVersion -o $publishDirectory
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败（退出码 $LASTEXITCODE）。" }

if ($SkipArchive) {
    Write-Host "已跳过压缩（-SkipArchive）。"
    return
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
if (Test-Path -LiteralPath $archivePath) { Remove-Item -LiteralPath $archivePath -Force }
Compress-Archive -Path (Join-Path $publishDirectory "*") -DestinationPath $archivePath -CompressionLevel Optimal
Write-Host "已生成：$archivePath"
