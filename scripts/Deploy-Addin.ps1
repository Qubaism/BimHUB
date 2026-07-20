[CmdletBinding()]
param(
    [ValidateSet("2023", "2024", "2025", "2026", "2027")]
    [string]$RevitVersion = "2025",

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot "src\QBTools.BimHUB\QBTools.BimHUB.csproj"
$revitDirectory = "C:\Program Files\Autodesk\Revit $RevitVersion"
$revitExecutablePath = "C:\Program Files\Autodesk\Revit $RevitVersion\Revit.exe"
$revitApiPath = Join-Path $revitDirectory "RevitAPI.dll"

if (-not (Test-Path $revitExecutablePath) -or -not (Test-Path $revitApiPath)) {
    throw "Revit $RevitVersion is not installed, so a verified BimHUB build cannot be deployed."
}

$revitIsRunning = Get-Process Revit -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -eq $revitExecutablePath }

if ($revitIsRunning) {
    throw "Close Revit $RevitVersion before deploying BimHUB."
}

dotnet build $projectPath -c $Configuration -p:RevitVersion=$RevitVersion -p:NoWarn=MSB3277 --nologo --verbosity quiet -consoleLoggerParameters:ErrorsOnly
if ($LASTEXITCODE -ne 0) {
    throw "The BimHUB build failed."
}

$outputDirectory = Join-Path $repositoryRoot "src\QBTools.BimHUB\bin\$Configuration\Revit$RevitVersion"
$builtAssemblyPath = Join-Path $outputDirectory "QBTools.BimHUB.dll"
$manifestTemplatePath = Join-Path $repositoryRoot "deploy\BimHUB.addin.template"
$addinsDirectory = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion"
$installDirectory = Join-Path $addinsDirectory "QBTools.BimHUB"
$installedAssemblyPath = Join-Path $installDirectory "QBTools.BimHUB.dll"
$installedManifestPath = Join-Path $addinsDirectory "BimHUB.addin"

New-Item -ItemType Directory -Force $installDirectory | Out-Null
$deploymentFiles = Get-ChildItem $outputDirectory -File |
    Where-Object {
        ($_.Extension -eq ".dll" -or $_.Name -eq "QBTools.BimHUB.dll.config") -and
        $_.Name -notin @("RevitAPI.dll", "RevitAPIUI.dll")
    }

foreach ($file in $deploymentFiles) {
    Copy-Item $file.FullName (Join-Path $installDirectory $file.Name) -Force
}

[xml]$manifest = Get-Content $manifestTemplatePath
$assemblyNode = $manifest.SelectSingleNode("/RevitAddIns/AddIn/Assembly")
$assemblyNode.InnerText = $installedAssemblyPath
$manifest.Save($installedManifestPath)

Write-Host "BimHUB was deployed for Revit $RevitVersion."
Write-Host "Manifest: $installedManifestPath"
Write-Host "Assembly: $installedAssemblyPath"
Write-Host "Files: $($deploymentFiles.Count)"
