param([Parameter(Mandatory=$true)][string]$Artifacts, [Parameter(Mandatory=$true)][string]$Payload)
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'Live trust/install tests are restricted to disposable GitHub runners.' }
function RunInstaller([string]$Path, [string[]]$Arguments) {
    $process = Start-Process -FilePath $Path -ArgumentList $Arguments -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0) { throw ('Installer operation failed: ' + $process.ExitCode) }
}
$leaf = New-Object Security.Cryptography.X509Certificates.X509Certificate2((Join-Path $Payload 'ShellIntegration/signer.cer'))
function AssertTrust([bool]$Expected) {
    $store = New-Object Security.Cryptography.X509Certificates.X509Store('TrustedPeople','LocalMachine')
    $store.Open('ReadOnly')
    try { $found = $store.Certificates.Find('FindByThumbprint',$leaf.Thumbprint,$false).Count -gt 0 }
    finally { $store.Close() }
    if ($found -ne $Expected) { throw 'Unexpected machine certificate trust state.' }
}
$installation = Join-Path $env:ProgramFiles 'NexaCL'
$registration = Join-Path $installation 'ShellIntegration/Nexa.ShellRegistration.exe'
AssertTrust $false
# Default silent installation must not add trust or launch the application.
RunInstaller (Join-Path $Artifacts 'Nexa-test.setup.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/TASKS=')
AssertTrust $false
RunInstaller (Join-Path $installation 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART')
AssertTrust $false
foreach ($kind in @('exe','msi')) {
    try {
        if ($kind -eq 'exe') {
            RunInstaller (Join-Path $Artifacts 'Nexa-test.setup.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/TASKS=explorerintegration')
        } else {
            RunInstaller 'msiexec.exe' @('/i', ('"' + (Join-Path $Artifacts 'Nexa-test.msi') + '"'), '/qn','/norestart','ADDLOCAL=Launcher,ExplorerIntegration')
            RunInstaller $registration @('--register')
        }
        AssertTrust $true
        & $env:NEXA_TEST_NATIVE --registered
        if ($LASTEXITCODE -ne 0) { throw 'Registered native COM activation failed.' }
    } finally {
        if ($kind -eq 'exe' -and (Test-Path (Join-Path $installation 'unins000.exe'))) {
            RunInstaller (Join-Path $installation 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART')
        } elseif ($kind -eq 'msi') {
            RunInstaller 'msiexec.exe' @('/x', ('"' + (Join-Path $Artifacts 'Nexa-test.msi') + '"'), '/qn','/norestart')
        }
    }
    AssertTrust $false
}
Write-Host 'PASS: explicit trust in both installers, native COM registration, uninstall cleanup, default installation without trust.'
