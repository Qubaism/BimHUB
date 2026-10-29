[CmdletBinding()]
param(
    [ValidateSet('2023', '2024', '2025', '2026', '2027')]
    [string]$RevitVersion = '2025',

    [ValidateSet('auto', 'net48', 'net8', 'net10')]
    [string]$RevitRuntime = 'auto',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

function Get-InstalledRevitRuntime {
    param(
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$ExecutablePath
    )

    if ($Version -in @('2023', '2024')) { return 'net48' }
    if ($Version -eq '2027') { return 'net10' }

    $fileVersionText = (Get-Item -LiteralPath $ExecutablePath).VersionInfo.FileVersion
    $fileVersion = [System.Version]::Parse($fileVersionText)
    if ($fileVersion.Minor -ge 5) { return 'net10' }
    return 'net8'
}

function Find-RevitInstallDirectory {
    param([Parameter(Mandatory)][string]$Version)

    $standardDirectory = "C:\Program Files\Autodesk\Revit $Version"
    if (Test-Path -LiteralPath (Join-Path $standardDirectory 'Revit.exe')) {
        return $standardDirectory
    }

    $uninstallRoots = @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall'
    )
    foreach ($root in $uninstallRoots) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        foreach ($key in Get-ChildItem -LiteralPath $root -ErrorAction SilentlyContinue) {
            $entry = Get-ItemProperty -LiteralPath $key.PSPath -ErrorAction SilentlyContinue
            if ($entry.DisplayName -ne "Autodesk Revit $Version") { continue }

            foreach ($candidate in @(
                (Join-Path $entry.InstallLocation "Revit $Version"),
                $entry.InstallLocation
            )) {
                if ($candidate -and
                    (Test-Path -LiteralPath (Join-Path $candidate 'Revit.exe'))) {
                    return $candidate
                }
            }
        }
    }

    return $null
}

function Test-OwnedManifest {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$AddInId,
        [Parameter(Mandatory)][string]$ClassName
    )

    if (-not (Test-Path -LiteralPath $Path)) { return $false }
    try {
        [xml]$existing = Get-Content -LiteralPath $Path
        $addIn = $existing.SelectSingleNode('/RevitAddIns/AddIn')
        return $addIn -and
            [string]::Equals($addIn.AddInId, $AddInId, [StringComparison]::OrdinalIgnoreCase) -and
            [string]::Equals($addIn.FullClassName, $ClassName, [StringComparison]::Ordinal)
    }
    catch {
        return $false
    }
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'src\QBTools.BimHUB\QBTools.BimHUB.csproj'
$revitDirectory = Find-RevitInstallDirectory -Version $RevitVersion
if (-not $revitDirectory) {
    $revitDirectory = "C:\Program Files\Autodesk\Revit $RevitVersion"
}
$revitExecutablePath = Join-Path $revitDirectory 'Revit.exe'
$revitApiPath = Join-Path $revitDirectory 'RevitAPI.dll'

if (-not (Test-Path -LiteralPath $revitExecutablePath) -or
    -not (Test-Path -LiteralPath $revitApiPath)) {
    throw "Revit $RevitVersion is not installed, so a verified BimHUB build cannot be deployed."
}

$installedRuntime = Get-InstalledRevitRuntime `
    -Version $RevitVersion `
    -ExecutablePath $revitExecutablePath
if ($RevitRuntime -eq 'auto') {
    $RevitRuntime = $installedRuntime
}
elseif ($RevitRuntime -ne $installedRuntime) {
    throw "Installed Revit $RevitVersion requires $installedRuntime, not $RevitRuntime."
}

$revitIsRunning = Get-Process Revit -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -eq $revitExecutablePath }

if ($revitIsRunning) {
    throw "Close Revit $RevitVersion before deploying BimHUB."
}

dotnet build $projectPath `
    -c $Configuration `
    -p:RevitVersion=$RevitVersion `
    -p:RevitRuntime=$RevitRuntime `
    -p:RevitApiDirectory=$revitDirectory `
    -p:NoWarn=MSB3277 `
    --nologo `
    --verbosity quiet `
    -consoleLoggerParameters:ErrorsOnly
if ($LASTEXITCODE -ne 0) {
    throw 'The BimHUB build failed.'
}

$buildTarget = "Revit$RevitVersion-$RevitRuntime"
$outputDirectory = Join-Path $repositoryRoot `
    "src\QBTools.BimHUB\bin\$Configuration\$buildTarget"
$builtAssemblyPath = Join-Path $outputDirectory 'QBTools.BimHUB.dll'
$manifestTemplatePath = Join-Path $repositoryRoot 'deploy\BimHUB.addin.template'
$addinsDirectory = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion"
$installDirectory = Join-Path $addinsDirectory 'QBTools.BimHUB'
$installedAssemblyPath = Join-Path $installDirectory 'QBTools.BimHUB.dll'
$installedManifestPath = Join-Path $addinsDirectory 'QBTools.BimHUB.addin'
$legacyManifestPath = Join-Path $addinsDirectory 'BimHUB.addin'

[xml]$manifest = Get-Content -LiteralPath $manifestTemplatePath
$addInNode = $manifest.SelectSingleNode('/RevitAddIns/AddIn')
$addInId = [string]$addInNode.AddInId
$className = [string]$addInNode.FullClassName
if ((Test-Path -LiteralPath $installedManifestPath) -and
    -not (Test-OwnedManifest -Path $installedManifestPath -AddInId $addInId -ClassName $className)) {
    throw "Refusing to overwrite an unrelated manifest: $installedManifestPath"
}

New-Item -ItemType Directory -Force $installDirectory | Out-Null
$deploymentFiles = Get-ChildItem $outputDirectory -File |
    Where-Object {
        ($_.Extension -eq '.dll' -or
         $_.Name -eq 'QBTools.BimHUB.dll.config' -or
         $_.Name -eq 'QBTools.BimHUB.deps.json') -and
        $_.Name -notin @('RevitAPI.dll', 'RevitAPIUI.dll')
    }

foreach ($file in $deploymentFiles) {
    Copy-Item $file.FullName (Join-Path $installDirectory $file.Name) -Force
}

$assemblyNode = $manifest.SelectSingleNode('/RevitAddIns/AddIn/Assembly')
$assemblyNode.InnerText = $installedAssemblyPath
$manifest.Save($installedManifestPath)

if (Test-OwnedManifest -Path $legacyManifestPath -AddInId $addInId -ClassName $className) {
    Remove-Item -LiteralPath $legacyManifestPath -Force
}

$sourceHash = (Get-FileHash -LiteralPath $builtAssemblyPath -Algorithm SHA256).Hash
$installedHash = (Get-FileHash -LiteralPath $installedAssemblyPath -Algorithm SHA256).Hash
if ($sourceHash -ne $installedHash) {
    throw 'The deployed BimHUB assembly does not match the verified build output.'
}

Write-Host "BimHUB was deployed for Revit $RevitVersion ($RevitRuntime)."
Write-Host "Manifest: $installedManifestPath"
Write-Host "Assembly: $installedAssemblyPath"
Write-Host "Files: $($deploymentFiles.Count)"
Write-Host "SHA256: $($installedHash.ToLowerInvariant())"
