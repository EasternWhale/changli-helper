param([switch]$ServiceTokenOnly, [switch]$FromClipboard)

$ErrorActionPreference = 'Stop'
$credentialDirectory = Join-Path $PSScriptRoot '..\.local'
$credentialFile = Join-Path $credentialDirectory 'coze-token.dpapi'
Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Security\Microsoft.PowerShell.Security.psd1')
$prompt = if ($ServiceTokenOnly) {
    'Paste the new Service Access Token beginning with sat_ and press Enter (input hidden)'
} else {
    'Paste the Coze access token and press Enter (input hidden)'
}
if ($FromClipboard) {
    $value = ([string](Get-Clipboard -Raw)).Trim()
    if ($value -cnotmatch '^sat_[A-Za-z0-9_-]+$') {
        throw 'The clipboard does not contain a service access token.'
    }
    $token = ConvertTo-SecureString $value -AsPlainText -Force
} else {
    do {
        $token = Read-Host $prompt -AsSecureString
        if (-not $ServiceTokenOnly) { break }
        $value = [System.Net.NetworkCredential]::new('', $token).Password
        if ($value -cmatch '^sat_[A-Za-z0-9_-]+$') { break }
        Write-Host 'That was not a service access token. Paste only the value beginning with sat_.'
    } while ($true)
}
if ($token.Length -eq 0) { throw 'The token is empty.' }
New-Item -Path $credentialDirectory -ItemType Directory -Force | Out-Null
$token | ConvertFrom-SecureString | Set-Content -LiteralPath $credentialFile -Encoding ascii
Write-Host 'Token saved. For the Docker deployment, run Start-Docker.cmd to apply it.'
