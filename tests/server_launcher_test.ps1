param([string]$Package = (Join-Path $PSScriptRoot '..\server-package'))
$ErrorActionPreference = 'Stop'
$root = Join-Path $env:TEMP ('RS2 launcher test & spaces ' + [Guid]::NewGuid().ToString('N'))
$scripts = Join-Path $root 'scripts'
$runtime = Join-Path $root 'Server225\runtime\bun-windows-x64'
[void][IO.Directory]::CreateDirectory($scripts)
[void][IO.Directory]::CreateDirectory($runtime)
Copy-Item -LiteralPath (Join-Path $Package 'scripts\RS2-Server.Common.ps1'),(Join-Path $Package 'scripts\Configure-Server.ps1') -Destination $scripts
$worker = Join-Path $scripts 'Get-RS2SystemState.ps1'
$bun = Join-Path $runtime 'bun.exe'
# A small test executable lets CI exercise the real PowerShell wrapper and
# native argv boundary without downloading Bun or creating any real accounts.
$stub = @'
using System;
using System.IO;
using System.Threading;
public class LauncherFixture {
  public static void Main(string[] args) {
    bool version = args.Length == 1 && args[0] == "--version";
    if (args.Length > 2 && args[2] == "--hold") { File.WriteAllText(Path.Combine(Path.GetDirectoryName(args[1]),"fixture-running"),"ready"); Thread.Sleep(60000); return; }
    if (version && Environment.GetEnvironmentVariable("RS2_FIXTURE_STAGE") == "runtime" ||
        !version && Environment.GetEnvironmentVariable("RS2_FIXTURE_STAGE") == "setup") { Thread.Sleep(60000); return; }
    if (version) { Console.WriteLine("fixture-runtime"); return; }
    if (args.Length < 2 || args[0] != "run" || !File.Exists(args[1])) Environment.Exit(9);
    File.WriteAllText(Path.Combine(Environment.CurrentDirectory,"Xbox-config.ini"),"fixture-only");
    Console.WriteLine("Configuration ready.");
  }
}
'@
Add-Type -TypeDefinition $stub -OutputAssembly $bun -OutputType ConsoleApplication
[IO.File]::WriteAllText((Join-Path $scripts 'initialize-server.mjs'),'// Test stub; the fixture executable only checks this argument path.')
. (Join-Path $scripts 'RS2-Server.Common.ps1')
function Check([bool]$condition,[string]$message) { if (-not $condition) { throw $message }; Write-Output "PASS: $message" }
$ps5 = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
function Configure([string]$stage = '',[int]$probe = 2,[int]$setup = 2) {
    $env:RS2_FIXTURE_STAGE = $stage
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = $ps5
    $info.Arguments = (@('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',(Join-Path $scripts 'Configure-Server.ps1'),'-ProbeTimeoutSeconds',"$probe",'-SetupTimeoutSeconds',"$setup") | ForEach-Object { ConvertTo-RS2ProcessArgument $_ }) -join ' '
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true; $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    $process = New-Object Diagnostics.Process; $process.StartInfo = $info
    try {
        [void]$process.Start(); $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(20000)) { $process.Kill(); throw 'Fixture exceeded its outer deadline.' }
        return [pscustomobject]@{ Code=$process.ExitCode; Out=$stdout.Result; Error=$stderr.Result }
    } finally { $process.Dispose(); Remove-Item Env:\RS2_FIXTURE_STAGE -ErrorAction SilentlyContinue }
}
try {
    [IO.File]::WriteAllText((Join-Path $root 'options.ini'),"server_address = 127.0.0.1`n")
    [IO.File]::WriteAllText($worker,"throw 'Network worker must never run for a manual address.'")
    $result = Configure
    Check ($result.Code -eq 0 -and $result.Out.Contains('automatic discovery skipped') -and (Test-Path -LiteralPath (Join-Path $root 'Xbox-config.ini'))) 'manual address bypasses discovery; native arguments handle spaces and ampersands'
    $snapshot = [IO.File]::ReadAllBytes((Join-Path $root 'Xbox-config.ini'))
    $result = Configure -stage runtime -probe 1
    Check ($result.Code -ne 0 -and $result.Out.Contains('[3/4]') -and $result.Error.Contains('timed out')) 'stalled Bun runtime exits with a bounded, named error'
    $result = Configure -stage setup -setup 1
    Check ($result.Code -ne 0 -and $result.Out.Contains('[4/4]') -and $result.Error.Contains('timed out')) 'stalled configuration exits with a bounded, named error'
    Check ([Convert]::ToBase64String($snapshot) -eq [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $root 'Xbox-config.ini')))) 'timeout failures preserve existing configuration'
    [IO.File]::WriteAllText((Join-Path $root 'options.ini'),"server_address = auto`n")
    [IO.File]::WriteAllText($worker,"param(`$Query,`$BunPath,`$WrapperPath); Start-Sleep -Seconds 60")
    $result = Configure -probe 1
    Check ($result.Code -ne 0 -and $result.Error.Contains('Automatic address detection failed') -and $result.Error.Contains('options.ini')) 'stalled discovery returns manual-address recovery instructions'
    [IO.File]::WriteAllText($worker,"param(`$Query,`$BunPath,`$WrapperPath); '{`"address`":`"127.0.0.1`"}'")
    $result = Configure
    Check ($result.Code -eq 0 -and $result.Out.Contains('[2/4] Detecting')) 'automatic address result reaches the native initializer'
    Copy-Item -LiteralPath (Join-Path $Package 'scripts\Get-RS2SystemState.ps1') -Destination $worker
    $hold = Start-Process -FilePath $bun -ArgumentList @('run',('"'+$rs2Wrapper+'"'),'--hold') -WindowStyle Hidden -PassThru
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(5)
        while (-not (Test-Path -LiteralPath (Join-Path $root 'Server225\fixture-running')) -and -not $hold.HasExited -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 50 }
        $orphans = @(Get-RS2WrapperProcesses)
        if ($orphans.Count -ne 1 -or $orphans[0].ProcessId -ne $hold.Id) {
            $observed = Get-CimInstance Win32_Process -Filter "ProcessId = $($hold.Id)" -OperationTimeoutSec 5
            [pscustomobject]@{ FixtureAlive=(-not $hold.HasExited); ExpectedBun=$rs2Bun; ExpectedWrapper=$rs2Wrapper; ObservedExecutable=$observed.ExecutablePath; ObservedCommandLine=$observed.CommandLine } | ConvertTo-Json -Compress | Write-Output
        }
        Check ($orphans.Count -eq 1 -and $orphans[0].ProcessId -eq $hold.Id) 'bounded orphan check identifies the matching folder and command line'
        $result = Configure
        Check ($result.Code -ne 0 -and $result.Error.Contains('unrecorded server process')) 'orphan protection prevents a duplicate server'
        $stall = Join-Path $scripts 'stall.ps1'
        [IO.File]::WriteAllText($stall,'Start-Sleep -Seconds 60')
        $timedOut = $false
        try { Invoke-RS2BoundedProcess -Executable $ps5 -Arguments @('-NoProfile','-File',$stall) -TimeoutSeconds 1 -Stage 'Test worker' | Out-Null }
        catch { $timedOut = $_.Exception.Message.Contains('timed out') }
        Check $timedOut 'worker deadline remains enforced while another server process exists'
        Check (-not $hold.HasExited) 'configuration failure leaves the existing process running'
    } finally { if (-not $hold.HasExited) { $hold.Kill(); [void]$hold.WaitForExit(2000) }; $hold.Dispose() }
    $echoScript = Join-Path $scripts 'echo-args.ps1'
    [IO.File]::WriteAllText($echoScript,'param([string[]]$Items); ConvertTo-Json -InputObject $Items -Compress')
    $values = @('spaces & symbols','trailing\','embedded"quote','')
    # Pass one JSON value as a scalar to check all Windows quoting cases together.
    $echo = Invoke-RS2BoundedProcess -Executable $ps5 -Arguments @('-NoProfile','-File',$echoScript,'-Items',($values | ConvertTo-Json -Compress)) -TimeoutSeconds 5 -Stage 'Argument echo'
    $encoded = ConvertFrom-Json -InputObject $echo
    $roundTrip = ConvertFrom-Json -InputObject ([string]$encoded)
    Check (($roundTrip -join '|') -ceq ($values -join '|')) 'native argv quoting retains quotes, backslashes and empty values'
    Write-Output 'Launcher regression tests passed. Only disposable fixture processes were terminated.'
} finally {
    # Keep the non-sensitive fixture for failed-test diagnosis. No game world,
    # account, database or key material is created by this suite.
    Write-Output 'Disposable launcher fixture retained in the Windows temporary directory.'
}
