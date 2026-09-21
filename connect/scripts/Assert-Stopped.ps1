param([Parameter(Mandatory)][string]$Root, [switch]$LockHeld)
$ErrorActionPreference = 'Stop'
. (Join-Path $Root 'scripts\RS2-Server.Common.ps1')
$lock = if (-not $LockHeld) { Open-RS2ControlLock }
try {
    if ((Get-RS2RecordedProcess (Read-RS2Record)) -or @(Get-RS2WrapperProcesses).Count -gt 0) {
        throw 'Stop this server using its Stop command before importing, backing up, or restoring.'
    }
} finally { if ($lock) { $lock.Dispose() } }
