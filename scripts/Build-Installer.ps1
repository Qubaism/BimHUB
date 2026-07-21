[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version = '0.1.0-alpha.2',

    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string]$NumericVersion = '0.1.0.2',

    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'src\QBTools.BimHUB\QBTools.BimHUB.csproj'
$testProjectPath = Join-Path $repositoryRoot 'tests\QBTools.BimHUB.Tests\QBTools.BimHUB.Tests.csproj'
$installerScriptPath = Join-Path $repositoryRoot 'installer\BimHUB.iss'
$artifactsRoot = Join-Path $repositoryRoot 'artifacts'
$stagingRoot = Join-Path $artifactsRoot 'installer-staging'
$outputRoot = Join-Path $artifactsRoot 'installer'
$supportedVersions = @('2023', '2024', '2025', '2026')

function Assert-RepositoryChildPath {
    param([Parameter(Mandatory)][string]$Path)

    $root = [System.IO.Path]::GetFullPath($repositoryRoot).TrimEnd('\') + '\'
    $candidate = [System.IO.Path]::GetFullPath($Path)
    if (-not $candidate.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside the repository: $candidate"
    }
}

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

$numericParts = $NumericVersion.Split('.')
if ($numericParts.Count -ne 4 -or
    ($numericParts | Where-Object { [int]$_ -gt 65535 }).Count -gt 0) {
    throw 'NumericVersion must contain four values between 0 and 65535.'
}

foreach ($path in @($stagingRoot, $outputRoot)) {
    Assert-RepositoryChildPath $path
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
    New-Item -ItemType Directory -Path $path -Force | Out-Null
}

foreach ($revitVersion in $supportedVersions) {
    $revitDirectory = "C:\Program Files\Autodesk\Revit $revitVersion"
    if (-not (Test-Path -LiteralPath (Join-Path $revitDirectory 'Revit.exe')) -or
        -not (Test-Path -LiteralPath (Join-Path $revitDirectory 'RevitAPI.dll')) -or
        -not (Test-Path -LiteralPath (Join-Path $revitDirectory 'RevitAPIUI.dll'))) {
        throw "Revit $revitVersion is required to produce its verified installer payload."
    }

    if (-not $SkipTests) {
        Write-Host "Testing BimHUB for Revit $revitVersion..."
        Invoke-DotNet @(
            'test', $testProjectPath,
            '-c', 'Release',
            "-p:RevitVersion=$revitVersion",
            '-p:NoWarn=MSB3277',
            '--nologo',
            '--verbosity', 'minimal'
        )
    }

    Write-Host "Building BimHUB for Revit $revitVersion..."
    Invoke-DotNet @(
        'build', $projectPath,
        '-c', 'Release',
        "-p:RevitVersion=$revitVersion",
        "-p:Version=$Version",
        "-p:FileVersion=$NumericVersion",
        "-p:InformationalVersion=$Version",
        '-p:NoWarn=MSB3277',
        '--nologo',
        '--verbosity', 'minimal'
    )

    $buildOutput = Join-Path $repositoryRoot "src\QBTools.BimHUB\bin\Release\Revit$revitVersion"
    $payloadOutput = Join-Path $stagingRoot "Revit$revitVersion"
    New-Item -ItemType Directory -Path $payloadOutput -Force | Out-Null

    $payloadFiles = Get-ChildItem -LiteralPath $buildOutput -File |
        Where-Object {
            ($_.Extension -eq '.dll' -or
             $_.Name -eq 'QBTools.BimHUB.dll.config' -or
             $_.Name -eq 'QBTools.BimHUB.deps.json') -and
            $_.Name -notin @('RevitAPI.dll', 'RevitAPIUI.dll')
        }

    if (-not ($payloadFiles.Name -contains 'QBTools.BimHUB.dll')) {
        throw "The Revit $revitVersion payload does not contain QBTools.BimHUB.dll."
    }

    $payloadFiles | Copy-Item -Destination $payloadOutput -Force
    Write-Host "Staged $($payloadFiles.Count) files for Revit $revitVersion."
}

$compilerCandidates = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
)
$compilerPath = $compilerCandidates |
    Where-Object { $_ -and (Test-Path -LiteralPath $_) } |
    Select-Object -First 1

if (-not $compilerPath) {
    throw 'Inno Setup 6 was not found. Install JRSoftware.InnoSetup with winget.'
}

Write-Host "Compiling BimHUB installer $Version..."
& $compilerPath @(
    "/DAppVersion=$Version",
    "/DNumericVersion=$NumericVersion",
    "/DPayloadRoot=$stagingRoot",
    "/DOutputRoot=$outputRoot",
    $installerScriptPath
)
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

$installerPath = Join-Path $outputRoot "BimHUB-$Version-Setup.exe"
if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "The expected installer was not created: $installerPath"
}

$hash = Get-FileHash -LiteralPath $installerPath -Algorithm SHA256
$hashPath = "$installerPath.sha256"
Set-Content -LiteralPath $hashPath -Value "$($hash.Hash.ToLowerInvariant())  $([System.IO.Path]::GetFileName($installerPath))" -Encoding ascii

Write-Host ''
Write-Host 'Installer created successfully.'
Write-Host "Installer: $installerPath"
Write-Host "SHA256:   $($hash.Hash.ToLowerInvariant())"
