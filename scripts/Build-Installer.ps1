[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version = '0.2.0-alpha.1',

    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string]$NumericVersion = '0.2.0.1',

    [string]$ApiReferenceRoot,

    [switch]$SkipTests,

    [switch]$AllowPartialMatrix
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'src\QBTools.BimHUB\QBTools.BimHUB.csproj'
$testProjectPath = Join-Path $repositoryRoot 'tests\QBTools.BimHUB.Tests\QBTools.BimHUB.Tests.csproj'
$installerScriptPath = Join-Path $repositoryRoot 'installer\BimHUB.iss'
$packageReadmePath = Join-Path $repositoryRoot 'installer\Package-README.txt'
$artifactsRoot = Join-Path $repositoryRoot 'artifacts'
$stagingRoot = Join-Path $artifactsRoot 'installer-staging'
$packageStagingRoot = Join-Path $artifactsRoot 'installer-package'
$outputRoot = Join-Path $artifactsRoot 'installer'
if (-not $ApiReferenceRoot) {
    $ApiReferenceRoot = Join-Path $artifactsRoot 'revit-api'
}

$buildTargets = @(
    [pscustomobject]@{ Version = '2023'; Runtime = 'net48'; Key = 'Revit2023-net48'; Define = 'HasRevit2023Net48' },
    [pscustomobject]@{ Version = '2024'; Runtime = 'net48'; Key = 'Revit2024-net48'; Define = 'HasRevit2024Net48' },
    [pscustomobject]@{ Version = '2025'; Runtime = 'net8'; Key = 'Revit2025-net8'; Define = 'HasRevit2025Net8' },
    [pscustomobject]@{ Version = '2025'; Runtime = 'net10'; Key = 'Revit2025-net10'; Define = 'HasRevit2025Net10' },
    [pscustomobject]@{ Version = '2026'; Runtime = 'net8'; Key = 'Revit2026-net8'; Define = 'HasRevit2026Net8' },
    [pscustomobject]@{ Version = '2026'; Runtime = 'net10'; Key = 'Revit2026-net10'; Define = 'HasRevit2026Net10' },
    [pscustomobject]@{ Version = '2027'; Runtime = 'net10'; Key = 'Revit2027-net10'; Define = 'HasRevit2027Net10' }
)

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

function Test-RevitApiDirectory {
    param(
        [Parameter(Mandatory)]$Target,
        [Parameter(Mandatory)][string]$Path
    )

    $apiPath = Join-Path $Path 'RevitAPI.dll'
    $apiUiPath = Join-Path $Path 'RevitAPIUI.dll'
    if (-not (Test-Path -LiteralPath $apiPath) -or
        -not (Test-Path -LiteralPath $apiUiPath)) {
        return $false
    }

    try {
        $apiVersion = [System.Version]::Parse(
            (Get-Item -LiteralPath $apiPath).VersionInfo.FileVersion)
        $apiUiVersion = [System.Version]::Parse(
            (Get-Item -LiteralPath $apiUiPath).VersionInfo.FileVersion)
    }
    catch {
        Write-Warning "Could not read Revit API versions in '$Path': $($_.Exception.Message)"
        return $false
    }

    $expectedMajor = [int]$Target.Version.Substring(2)
    if ($apiVersion.Major -ne $expectedMajor -or
        $apiUiVersion.Major -ne $expectedMajor) {
        Write-Warning (
            "Rejected '$Path': expected Revit $($Target.Version) API files, " +
            "found RevitAPI $apiVersion and RevitAPIUI $apiUiVersion.")
        return $false
    }

    $apiRuntime = Get-RevitRuntimeFromFileVersion `
        -Version $Target.Version `
        -FileVersion $apiVersion
    if ($apiRuntime -ne $Target.Runtime) {
        Write-Warning (
            "Rejected '$Path': $($Target.Key) requires $($Target.Runtime) API " +
            "references, but RevitAPI $apiVersion maps to $apiRuntime.")
        return $false
    }

    return $true
}

function Get-RevitRuntimeFromFileVersion {
    param(
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][System.Version]$FileVersion
    )

    if ($Version -in @('2023', '2024')) { return 'net48' }
    if ($Version -eq '2027') { return 'net10' }

    if ($FileVersion.Minor -ge 5) { return 'net10' }
    return 'net8'
}

function Get-InstalledRevitRuntime {
    param(
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$ExecutablePath
    )

    $fileVersion = [System.Version]::Parse(
        (Get-Item -LiteralPath $ExecutablePath).VersionInfo.FileVersion)
    return Get-RevitRuntimeFromFileVersion -Version $Version -FileVersion $fileVersion
}

function Resolve-RevitApiDirectory {
    param([Parameter(Mandatory)]$Target)

    $installedDirectory = "C:\Program Files\Autodesk\Revit $($Target.Version)"
    $executablePath = Join-Path $installedDirectory 'Revit.exe'
    if ((Test-Path -LiteralPath $executablePath) -and
        (Test-RevitApiDirectory -Target $Target -Path $installedDirectory)) {
        $installedRuntime = Get-InstalledRevitRuntime `
            -Version $Target.Version `
            -ExecutablePath $executablePath
        if ($installedRuntime -eq $Target.Runtime) {
            return $installedDirectory
        }
    }

    $cachedDirectory = Join-Path $ApiReferenceRoot $Target.Key
    if (Test-RevitApiDirectory -Target $Target -Path $cachedDirectory) {
        return $cachedDirectory
    }

    return $null
}

$numericParts = $NumericVersion.Split('.')
if ($numericParts.Count -ne 4 -or
    ($numericParts | Where-Object { [int]$_ -gt 65535 }).Count -gt 0) {
    throw 'NumericVersion must contain four values between 0 and 65535.'
}

foreach ($path in @($stagingRoot, $packageStagingRoot, $outputRoot)) {
    Assert-RepositoryChildPath $path
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
    New-Item -ItemType Directory -Path $path -Force | Out-Null
}

$availableTargets = @()
foreach ($target in $buildTargets) {
    $apiDirectory = Resolve-RevitApiDirectory -Target $target
    if (-not $apiDirectory) {
        $message = "Revit API references for $($target.Key) were not found. " +
            "Install the matching Revit build or place RevitAPI.dll and " +
            "RevitAPIUI.dll in '$ApiReferenceRoot\$($target.Key)'."
        if (-not $AllowPartialMatrix) {
            throw $message
        }

        Write-Warning "$message This target will be omitted from the test installer."
        continue
    }

    if (-not $SkipTests) {
        Write-Host "Testing BimHUB for $($target.Key)..."
        Invoke-DotNet @(
            'test', $testProjectPath,
            '-c', 'Release',
            "-p:RevitVersion=$($target.Version)",
            "-p:RevitRuntime=$($target.Runtime)",
            "-p:RevitApiDirectory=$apiDirectory",
            '-p:NoWarn=MSB3277',
            '--nologo',
            '--verbosity', 'minimal'
        )
    }

    Write-Host "Building BimHUB for $($target.Key)..."
    Invoke-DotNet @(
        'build', $projectPath,
        '-c', 'Release',
        "-p:RevitVersion=$($target.Version)",
        "-p:RevitRuntime=$($target.Runtime)",
        "-p:RevitApiDirectory=$apiDirectory",
        "-p:Version=$Version",
        "-p:FileVersion=$NumericVersion",
        "-p:InformationalVersion=$Version",
        '-p:NoWarn=MSB3277',
        '--nologo',
        '--verbosity', 'minimal'
    )

    $buildOutput = Join-Path $repositoryRoot `
        "src\QBTools.BimHUB\bin\Release\$($target.Key)"
    $payloadOutput = Join-Path $stagingRoot $target.Key
    New-Item -ItemType Directory -Path $payloadOutput -Force | Out-Null

    $payloadFiles = Get-ChildItem -LiteralPath $buildOutput -File |
        Where-Object {
            ($_.Extension -eq '.dll' -or
             $_.Name -eq 'QBTools.BimHUB.dll.config' -or
             $_.Name -eq 'QBTools.BimHUB.deps.json') -and
            $_.Name -notin @('RevitAPI.dll', 'RevitAPIUI.dll')
        }

    if (-not ($payloadFiles.Name -contains 'QBTools.BimHUB.dll')) {
        throw "The $($target.Key) payload does not contain QBTools.BimHUB.dll."
    }

    $payloadFiles | Copy-Item -Destination $payloadOutput -Force
    $availableTargets += $target
    Write-Host "Staged $($payloadFiles.Count) files for $($target.Key)."
}

if ($availableTargets.Count -eq 0) {
    throw 'No compatible Revit build targets were available.'
}

if (-not $AllowPartialMatrix -and $availableTargets.Count -ne $buildTargets.Count) {
    throw 'A release installer requires all seven Revit runtime payloads.'
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
$compilerArguments = @(
    "/DAppVersion=$Version",
    "/DNumericVersion=$NumericVersion",
    "/DPayloadRoot=$stagingRoot",
    "/DOutputRoot=$outputRoot"
)
$compilerArguments += $availableTargets | ForEach-Object { "/D$($_.Define)=1" }
$compilerArguments += $installerScriptPath

& $compilerPath @compilerArguments
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

$installerPath = Join-Path $outputRoot "BimHUB-$Version-Setup.exe"
if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "The expected installer was not created: $installerPath"
}

$hash = Get-FileHash -LiteralPath $installerPath -Algorithm SHA256
$hashPath = "$installerPath.sha256"
Set-Content -LiteralPath $hashPath `
    -Value "$($hash.Hash.ToLowerInvariant())  $([System.IO.Path]::GetFileName($installerPath))" `
    -Encoding ascii

$baseVersion = $Version.Split('-')[0]
$packagePath = Join-Path $outputRoot "BimHUB-$baseVersion-Windows.zip"
Copy-Item -LiteralPath $installerPath -Destination $packageStagingRoot -Force
Copy-Item -LiteralPath $hashPath -Destination $packageStagingRoot -Force
Copy-Item -LiteralPath $packageReadmePath `
    -Destination (Join-Path $packageStagingRoot 'README.txt') -Force
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') `
    -Destination (Join-Path $packageStagingRoot 'LICENSE.txt') -Force
if ($AllowPartialMatrix) {
    $missingTargets = $buildTargets |
        Where-Object { $_.Key -notin $availableTargets.Key } |
        ForEach-Object { $_.Key }
    $testPackageNote = @(
        'BimHUB COMPATIBILITY TEST PACKAGE',
        '=================================',
        '',
        'This package is for local compatibility testing and is not a complete release.',
        '',
        "Included payloads: $($availableTargets.Key -join ', ')",
        "Not included: $($missingTargets -join ', ')",
        '',
        'The public release must be rebuilt without -AllowPartialMatrix so that all',
        'seven supported Revit runtime payloads are present.'
    )
    Set-Content -LiteralPath (Join-Path $packageStagingRoot 'TEST-PACKAGE.txt') `
        -Value $testPackageNote -Encoding ascii
}
Compress-Archive -Path (Join-Path $packageStagingRoot '*') `
    -DestinationPath $packagePath -CompressionLevel Optimal -Force

Write-Host ''
Write-Host 'Installer created successfully.'
Write-Host "Payloads:  $($availableTargets.Key -join ', ')"
Write-Host "Installer: $installerPath"
Write-Host "Package:   $packagePath"
Write-Host "SHA256:   $($hash.Hash.ToLowerInvariant())"
