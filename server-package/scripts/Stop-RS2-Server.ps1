param([ValidateRange(1, 120)][int]$WaitSeconds = 30)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'RS2-Server.Common.ps1')

$controlLock = Open-RS2ControlLock
try {
    $record = Read-RS2Record
    $serverProcess = Get-RS2RecordedProcess $record
    if (-not $serverProcess) {
        $orphaned = @(Get-RS2WrapperProcesses)
        if ($orphaned.Count -gt 0) {
            throw "A local server process exists without a matching PID record (PID $($orphaned.ProcessId -join ', ')). No stop request was sent."
        }
        Write-Output 'The recorded local RS2 server is not running. No process was stopped.'
        return
    }

    $requestPath = Join-Path $rs2Runtime ("server-{0}.stop-request" -f $record.runId)
    Write-RS2Utf8 $requestPath $record.runId
    Write-Output "Graceful shutdown requested for PID $($record.pid); waiting for player saves."
    $deadline = [DateTime]::UtcNow.AddSeconds($WaitSeconds)
    do {
        Start-Sleep -Milliseconds 500
        if (-not (Get-RS2RecordedProcess $record)) {
            $status = Read-RS2Status $record
            if ($status -and $status.status -eq 'stopped' -and $status.exitCode -eq 0) {
                Write-Output 'RS2 server stopped cleanly.'
            } else {
                throw "The server exited without a confirmed clean shutdown. Check $($record.stdoutLog) and $($record.stderrLog)."
            }
            return
        }
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "The server did not stop within $WaitSeconds seconds. Its shutdown request remains pending and the process was left running. No forced termination was attempted. Check $($record.stdoutLog) and $($record.stderrLog)."
} finally {
    $controlLock.Dispose()
}
