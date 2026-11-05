param([string]$CreatePfxPath, [Security.SecureString]$ExportPassword)
$ErrorActionPreference = 'Stop'
# Creating a persistent identity is explicit; ordinary builds never generate a new key.
if ($CreatePfxPath) {
    if (-not [IO.Path]::IsPathRooted($CreatePfxPath) -or (Test-Path -LiteralPath $CreatePfxPath)) { throw 'Choose a new absolute PFX path outside the repository.' }
    $password = if ($ExportPassword) { $ExportPassword } else { Read-Host 'New signing PFX password' -AsSecureString }
    $certificate = New-SelfSignedCertificate -Type Custom -Subject 'CN=NexaCL Explorer Integration' -FriendlyName 'NexaCL Explorer Integration' -CertStoreLocation Cert:\CurrentUser\My -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyUsage DigitalSignature -KeyExportPolicy Exportable -NotAfter (Get-Date).AddYears(3) -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
    try { Export-PfxCertificate -Cert $certificate -FilePath $CreatePfxPath -Password $password | Out-Null }
    finally { Remove-Item -LiteralPath ('Cert:\CurrentUser\My\' + $certificate.Thumbprint) -DeleteKey }
    Write-Host 'Persistent signing identity exported. Keep the PFX and password outside source control.'
    exit
}
if (-not $env:NEXA_SHELL_SIGNING_PFX_BASE64 -or -not $env:NEXA_SHELL_SIGNING_PFX_PASSWORD) { throw 'Configure NEXA_SHELL_SIGNING_PFX_BASE64 and NEXA_SHELL_SIGNING_PFX_PASSWORD. Reuse the same dedicated signing identity across releases.' }
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('nexa-shell-' + [Guid]::NewGuid().ToString('N') + '.pfx')
try {
    [IO.File]::WriteAllBytes($temporary, [Convert]::FromBase64String($env:NEXA_SHELL_SIGNING_PFX_BASE64))
    $password = ConvertTo-SecureString $env:NEXA_SHELL_SIGNING_PFX_PASSWORD -AsPlainText -Force
    $certificates = @(Import-PfxCertificate -FilePath $temporary -CertStoreLocation Cert:\CurrentUser\My -Password $password)
    $certificate = @($certificates | Where-Object HasPrivateKey)
    if ($certificate.Count -ne 1) { throw 'Signing PFX must contain exactly one signing identity.' }
    if ($env:GITHUB_ENV) { 'NEXA_WINDOWS_SIGNING_THUMBPRINT=' + $certificate[0].Thumbprint | Out-File -FilePath $env:GITHUB_ENV -Encoding utf8 -Append }
    else { $env:NEXA_WINDOWS_SIGNING_THUMBPRINT = $certificate[0].Thumbprint }
} finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary } }
