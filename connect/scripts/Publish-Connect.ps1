param(
    [Parameter(Mandatory)][string]$ArtifactDirectory,
    [Parameter(Mandatory)][string]$NotesFile,
    [string]$Tag = 'connect-v0.1.0-preview.1',
    [switch]$Publish
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$artifactRoot = (Resolve-Path -LiteralPath $ArtifactDirectory).Path
$manifest = Get-Content -LiteralPath (Join-Path $artifactRoot 'build-manifest.json') -Raw | ConvertFrom-Json
$names = @((Split-Path $manifest.AppZip -Leaf), 'RS2-Xbox-Connect-Relay.zip', 'SHA256SUMS.txt')
foreach ($name in $names) { if (-not (Test-Path -LiteralPath (Join-Path $artifactRoot $name) -PathType Leaf)) { throw "Missing artifact: $name" } }
$notes = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $NotesFile).Path)
if (-not $manifest.FrpBundled -and $notes -notmatch 'cannot establish online') { throw 'Developer release notes must explicitly state that it cannot establish online connections.' }
if ($Tag -notmatch '^connect-v[0-9]+\.[0-9]+\.[0-9]+-preview\.[0-9]+$') { throw 'Use a separate connect-vX.Y.Z-preview.N tag.' }
Push-Location $repo
try {
    & python scripts/check-public.py
    if ($LASTEXITCODE -ne 0) { throw 'Source privacy check failed.' }
    $sha = (git rev-parse HEAD).Trim()
    if (-not $Publish) { Write-Output "Dry run: publish $Tag at $sha with $($names -join ', ')"; return }
    $env:GIT_TERMINAL_PROMPT='0'; $env:GCM_INTERACTIVE='never'
    # Use Git's configured credential helper for this exact authorized host;
    # tokens stay in memory and are never written or printed.
    $credential = @{}
    foreach ($line in ("protocol=https`nhost=github.com`n`n" | git credential fill)) {
        $parts = $line.Split('=', 2); if ($parts.Length -eq 2) { $credential[$parts[0]] = $parts[1] }
    }
    if (-not $credential.ContainsKey('password')) { throw 'Authenticate Git with GitHub before publishing.' }
    $headers = @{ Authorization=('Bearer ' + $credential['password']); 'User-Agent'='RS2-Xbox-Connect'; Accept='application/vnd.github+json'; 'X-GitHub-Api-Version'='2022-11-28' }
    $base = 'https://api.github.com/repos/TommySanzCode/rs2-xbox'
    $existing = @(Invoke-RestMethod "$base/releases?per_page=100" -Headers $headers) | Where-Object { $_.tag_name -eq $Tag }
    if ($existing.Count -gt 0) { throw 'This release already exists. Inspect it; never overwrite existing assets implicitly.' }
    $label = if ($manifest.FrpBundled) { 'RS2 Xbox Connect Preview 1' } else { 'RS2 Xbox Connect — Developer Preview 1 (Windows tunnel dependency not bundled)' }
    $body = @{ tag_name=$Tag; target_commitish=$sha; name=$label; body=$notes; draft=$true; prerelease=$true; make_latest='false' } | ConvertTo-Json
    $release = Invoke-RestMethod "$base/releases" -Method Post -Headers $headers -ContentType 'application/json' -Body ([Text.Encoding]::UTF8.GetBytes($body))
    # Upload while draft, then publish only after every expected asset has arrived.
    foreach ($name in $names) {
        $file = Join-Path $artifactRoot $name
        $upload = "https://uploads.github.com/repos/TommySanzCode/rs2-xbox/releases/$($release.id)/assets?name=$([Uri]::EscapeDataString($name))"
        $asset = Invoke-RestMethod $upload -Method Post -Headers $headers -ContentType 'application/octet-stream' -InFile $file
        if ($asset.size -ne (Get-Item -LiteralPath $file).Length) { throw 'Uploaded asset size mismatch. Release left in draft.' }
    }
    $result = Invoke-RestMethod "$base/releases/$($release.id)" -Method Patch -Headers $headers -ContentType 'application/json' -Body '{"draft":false,"prerelease":true,"make_latest":"false"}'
    [pscustomobject]@{ url=$result.html_url; tag=$result.tag_name; id=$result.id; assets=@($result.assets | Select-Object name,size,digest) } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $artifactRoot 'published-release.json')
    Write-Output $result.html_url
} finally {
    if ($credential) { $credential.Clear() }
    $headers=$null
    Pop-Location
}
