param([switch]$NoBrowser)
$ErrorActionPreference = 'Stop'
$url = 'http://127.0.0.1:8765'
function Test-CozeLocal {
    try {
        return (Invoke-RestMethod -Uri "$url/health" -TimeoutSec 1).service -eq 'coze-local-agent'
    } catch {
        return $false
    }
}

if (-not (Test-CozeLocal)) {
    $node = (Get-Command node.exe -ErrorAction Stop).Source
    $entry = Join-Path $PSScriptRoot 'server.cjs'
    $server = Start-Process -FilePath $node -ArgumentList ('"' + $entry + '"') -WindowStyle Hidden -PassThru
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        if ($server.HasExited -or (Test-CozeLocal)) { break }
        Start-Sleep -Milliseconds 250
    }
    if (-not (Test-CozeLocal)) { throw "The chat service could not start at $url." }
}
if (-not $NoBrowser) { Start-Process $url }
Write-Output "Coze local agent is running at $url"
