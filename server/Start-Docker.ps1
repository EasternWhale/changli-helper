param([switch]$NoLinks)
$ErrorActionPreference = 'Stop'
$docker = (Get-Command docker.exe -ErrorAction Stop).Source
$credentialDirectory = Join-Path $PSScriptRoot '..\.local'
$tokenFile = Join-Path $credentialDirectory 'coze-token.dpapi'
$accessFile = Join-Path $credentialDirectory 'coze-lan-access.txt'
$publicOriginFile = Join-Path $credentialDirectory 'coze-public-origin.txt'
$composeFile = Join-Path $PSScriptRoot 'compose.yaml'

$lanIp = Get-NetIPConfiguration | Where-Object IPv4DefaultGateway |
    ForEach-Object { $_.IPv4Address.IPAddress } | Select-Object -First 1
if (-not $lanIp) { throw 'Connect this computer to the local network and try again.' }

if (-not (Test-Path -LiteralPath $tokenFile)) {
    throw 'Run Configure-Coze.cmd to save the Coze API token first.'
}
$token = (Get-Content -LiteralPath $tokenFile -Raw).Trim() | ConvertTo-SecureString
if (-not (Test-Path -LiteralPath $accessFile)) {
    $bytes = New-Object byte[] 24
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    [BitConverter]::ToString($bytes).Replace('-', '').ToLowerInvariant() |
        Set-Content -LiteralPath $accessFile -Encoding ascii
}
$accessCode = (Get-Content -LiteralPath $accessFile -Raw).Trim()

try {
    $env:COZE_DOCKER_LAN_IP = $lanIp
    $env:COZE_DOCKER_API_TOKEN = [System.Net.NetworkCredential]::new('', $token).Password
    $env:COZE_DOCKER_ACCESS_CODE = $accessCode
    if (Test-Path -LiteralPath $publicOriginFile) {
        $env:COZE_PUBLIC_ORIGIN = (Get-Content -LiteralPath $publicOriginFile -Raw).Trim()
    }
    $images = & $docker image ls --format '{{.Repository}}:{{.Tag}}'
    if ($images -notcontains 'node:24-alpine' -and $images -contains 'node:20-alpine') {
        $env:COZE_DOCKER_NODE_IMAGE = 'node:20-alpine'
    }
    & $docker compose -f $composeFile up --build --force-recreate --wait --wait-timeout 60
    if ($LASTEXITCODE -ne 0) { throw 'Docker Compose could not start the chat service.' }
} finally {
    Remove-Item Env:COZE_DOCKER_LAN_IP, Env:COZE_DOCKER_API_TOKEN, Env:COZE_DOCKER_ACCESS_CODE, Env:COZE_DOCKER_NODE_IMAGE -ErrorAction SilentlyContinue
}
if (-not $NoLinks) {
    Write-Host "Local: http://127.0.0.1:8766/#access=$accessCode"
    Write-Host "LAN:   http://${lanIp}:8766/#access=$accessCode"
}
