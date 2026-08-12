param(
    [Parameter(Mandatory = $true)]
    [string]$ExePath,

    [int]$RequestWaitTimeoutSec = 25
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $ExePath)) {
    throw "Executable not found: $ExePath"
}

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class TgmWindowProbe {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
}
"@

function Invoke-GuardJson {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $output = & $ExePath @Arguments
    $exitCode = $LASTEXITCODE
    $json = $output | ConvertFrom-Json
    [pscustomobject]@{
        ExitCode = $exitCode
        Json = $json
        Raw = ($output -join "`n")
    }
}

function Test-ServerOverlayVisible {
    $server = Get-Process -Name 'test-give-me-space-server' -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -eq $server) {
        return $false
    }

    $script:tgmOverlayVisible = $false
    [TgmWindowProbe]::EnumWindows({
        param([IntPtr]$hwnd, [IntPtr]$lparam)

        [uint32]$windowProcId = 0
        [TgmWindowProbe]::GetWindowThreadProcessId($hwnd, [ref]$windowProcId) | Out-Null
        if ($windowProcId -eq [uint32]$server.Id -and
            [TgmWindowProbe]::IsWindowVisible($hwnd) -and
            -not [TgmWindowProbe]::IsIconic($hwnd)) {
            $rect = New-Object TgmWindowProbe+RECT
            [TgmWindowProbe]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
            if (($rect.Right -gt $rect.Left) -and ($rect.Bottom -gt $rect.Top)) {
                $script:tgmOverlayVisible = $true
                return $false
            }
        }

        return $true
    }, [IntPtr]::Zero) | Out-Null

    return $script:tgmOverlayVisible
}

function Wait-ServerOverlayVisible {
    param(
        [Parameter(Mandatory = $true)]
        [TimeSpan]$Timeout
    )

    $deadline = [DateTimeOffset]::UtcNow.Add($Timeout)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if (Test-ServerOverlayVisible) {
            return $true
        }

        Start-Sleep -Milliseconds 100
    }

    return $false
}

function Assert-NoServerConsoleWindow {
    $consoleWindows = Get-Process |
        Where-Object { $_.MainWindowTitle -like 'test-give-me-space-server-*' } |
        Select-Object -ExpandProperty MainWindowTitle

    if (@($consoleWindows).Count -ne 0) {
        throw "Unexpected console window(s): $($consoleWindows -join '; ')"
    }
}

$initial = Invoke-GuardJson -Arguments @('status')
if ($initial.Json.status -ne 'idle') {
    throw "Guard is not idle before verification: $($initial.Raw)"
}

$owner = "codex-lifecycle-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) "test-give-me-space-lifecycle"
New-Item -ItemType Directory -Force -Path $tempDir | Out-Null
$stdoutPath = Join-Path $tempDir "$owner.stdout.json"
$stderrPath = Join-Path $tempDir "$owner.stderr.txt"

$job = Start-Job -ScriptBlock {
    param($exe, $owner, $stdoutPath, $stderrPath)

    $process = Start-Process `
        -FilePath $exe `
        -ArgumentList @('request', '--purpose', 'test', '--owner', $owner) `
        -Wait `
        -PassThru `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath

    [pscustomobject]@{
        ExitCode = $process.ExitCode
        Stdout = (Get-Content -LiteralPath $stdoutPath -Raw -ErrorAction SilentlyContinue)
        Stderr = (Get-Content -LiteralPath $stderrPath -Raw -ErrorAction SilentlyContinue)
    }
} -ArgumentList $ExePath, $owner, $stdoutPath, $stderrPath

if (-not (Wait-ServerOverlayVisible -Timeout (New-TimeSpan -Seconds 12))) {
    $status = Invoke-GuardJson -Arguments @('status')
    if ($status.Json.owner -eq $owner) {
        Invoke-GuardJson -Arguments @('cancel', '--owner', $owner) | Out-Null
    }

    Wait-Job -Job $job -Timeout 5 | Out-Null
    Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
    throw "server overlay did not become visible; status=$($status.Raw)"
}

Assert-NoServerConsoleWindow

$completed = Wait-Job -Job $job -Timeout $RequestWaitTimeoutSec
if (-not $completed) {
    $status = Invoke-GuardJson -Arguments @('status')
    if ($status.Json.owner -eq $owner) {
        Invoke-GuardJson -Arguments @('finish', '--owner', $owner) | Out-Null
    }

    Wait-Job -Job $job -Timeout 5 | Out-Null
    Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
    throw "request did not complete under Start-Process -Wait in $RequestWaitTimeoutSec seconds; status=$($status.Raw)"
}

$requestResult = Receive-Job -Job $job
Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
if ($requestResult.ExitCode -ne 0) {
    throw "request failed with exit code $($requestResult.ExitCode): $($requestResult.Stdout) $($requestResult.Stderr)"
}

$requestJson = $requestResult.Stdout | ConvertFrom-Json
$guardStarted = $false
try {
    if ($requestJson.status -ne 'granted') {
        throw "request returned unexpected status: $($requestResult.Stdout)"
    }

    $guardStarted = $true
    $running = Invoke-GuardJson -Arguments @('status')
    if ($running.Json.status -ne 'running' -or $running.Json.owner -ne $owner) {
        throw "status after request is not running for owner ${owner}: $($running.Raw)"
    }

    if (-not (Test-ServerOverlayVisible)) {
        throw "server overlay is not visible after request: $($running.Raw)"
    }

    Assert-NoServerConsoleWindow

    $finish = Invoke-GuardJson -Arguments @('finish', '--owner', $owner)
    if ($finish.Json.status -ne 'finished') {
        throw "finish returned unexpected status: $($finish.Raw)"
    }

    $guardStarted = $false
    $afterFinish = Invoke-GuardJson -Arguments @('status')
    if ($afterFinish.Json.status -ne 'idle') {
        throw "status after finish is not idle: $($afterFinish.Raw)"
    }

    [pscustomobject]@{
        success = $true
        owner = $owner
        requestStatus = $requestJson.status
        finalStatus = $afterFinish.Json.status
    } | ConvertTo-Json -Compress
}
finally {
    if ($guardStarted) {
        try {
            $status = Invoke-GuardJson -Arguments @('status')
            if ($status.Json.owner -eq $owner) {
                Invoke-GuardJson -Arguments @('finish', '--owner', $owner) | Out-Null
            }
        }
        catch {
            Write-Warning "cleanup failed for owner ${owner}: $_"
        }
    }
}
