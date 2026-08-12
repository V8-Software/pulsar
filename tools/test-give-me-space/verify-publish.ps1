param(
    [string]$ProjectPath = (Join-Path $PSScriptRoot "TestGiveMeSpace.App\TestGiveMeSpace.App.csproj"),

    [string]$Configuration = "Release",

    [string]$OutputDir = (Join-Path ([System.IO.Path]::GetTempPath()) "test-give-me-space-publish-$([guid]::NewGuid().ToString('N'))")
)

$ErrorActionPreference = 'Stop'

function Set-EnvDefault {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($Name)) -and
        -not [string]::IsNullOrWhiteSpace($Value)) {
        [Environment]::SetEnvironmentVariable($Name, $Value, 'Process')
    }
}

$userProfile = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
$appData = [Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)
$localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
$programData = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)
$windowsDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::Windows)
$systemDirectory = [Environment]::SystemDirectory
$programFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
$programFilesX86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
$tempDirectory = [System.IO.Path]::GetTempPath().TrimEnd([System.IO.Path]::DirectorySeparatorChar)

Set-EnvDefault 'APPDATA' $appData
Set-EnvDefault 'LOCALAPPDATA' $localAppData
Set-EnvDefault 'USERPROFILE' $userProfile
Set-EnvDefault 'HOME' $userProfile
Set-EnvDefault 'SystemRoot' $windowsDirectory
Set-EnvDefault 'WINDIR' $windowsDirectory
Set-EnvDefault 'ComSpec' (Join-Path $systemDirectory 'cmd.exe')
Set-EnvDefault 'TEMP' $tempDirectory
Set-EnvDefault 'TMP' $tempDirectory
Set-EnvDefault 'ProgramFiles' $programFiles
Set-EnvDefault 'ProgramFiles(x86)' $programFilesX86
Set-EnvDefault 'ProgramW6432' $programFiles
Set-EnvDefault 'ProgramData' $programData
Set-EnvDefault 'DOTNET_CLI_HOME' $userProfile
Set-EnvDefault 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE' '1'
Set-EnvDefault 'DOTNET_CLI_TELEMETRY_OPTOUT' '1'
Set-EnvDefault 'DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE' '1'

function Remove-OldDefaultOutputDirs {
    $tempRoot = (Resolve-Path -LiteralPath ([System.IO.Path]::GetTempPath())).Path
    $tempRootWithSeparator = $tempRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) +
        [System.IO.Path]::DirectorySeparatorChar
    $currentOutputDir = [System.IO.Path]::GetFullPath($OutputDir)
    $currentOutputDir = $currentOutputDir.TrimEnd([System.IO.Path]::DirectorySeparatorChar)

    $oldDirs = Get-ChildItem -LiteralPath $tempRoot -Directory -Filter "test-give-me-space-publish-*" -ErrorAction SilentlyContinue
    foreach ($dir in $oldDirs) {
        $resolvedDir = (Resolve-Path -LiteralPath $dir.FullName).Path
        $resolvedDir = $resolvedDir.TrimEnd([System.IO.Path]::DirectorySeparatorChar)
        if ($resolvedDir.Equals($currentOutputDir, [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        $resolvedDirWithSeparator = $resolvedDir + [System.IO.Path]::DirectorySeparatorChar
        if (-not $resolvedDirWithSeparator.StartsWith($tempRootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove output outside temp: $resolvedDir"
        }

        Remove-Item -LiteralPath $resolvedDir -Recurse -Force
    }
}

Remove-OldDefaultOutputDirs
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

& dotnet publish $ProjectPath -c $Configuration --output $OutputDir -m:1 /p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$requiredFiles = @(
    "test-give-me-space.exe",
    "test-give-me-space-server.exe",
    "guard.wav"
)

foreach ($fileName in $requiredFiles) {
    $path = Join-Path $OutputDir $fileName
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Published output does not contain required file: $fileName"
    }

    $file = Get-Item -LiteralPath $path
    if ($file.Length -le 0) {
        throw "Published file is empty: $fileName"
    }
}

$unexpectedServerFiles = Get-ChildItem -LiteralPath $OutputDir -File -Filter "test-give-me-space-server.*" |
    Where-Object { $_.Name -ne "test-give-me-space-server.exe" } |
    Select-Object -ExpandProperty Name
if (@($unexpectedServerFiles).Count -ne 0) {
    throw "Published output contains unexpected server file(s): $($unexpectedServerFiles -join ', ')"
}

[pscustomobject]@{
    success = $true
    outputDir = $OutputDir
    requiredFiles = $requiredFiles
} | ConvertTo-Json -Compress
