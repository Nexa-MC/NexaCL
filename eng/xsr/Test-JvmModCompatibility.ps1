param(
    [Parameter(Mandatory = $true)][string]$HostExecutable,
    [Parameter(Mandatory = $true)][string]$JavaHome
)
$ErrorActionPreference = 'Stop'
$hostPath = (Resolve-Path -LiteralPath $HostExecutable).Path
$suffix = if ($IsWindows) { '.exe' } else { '' }
$javaPath = (Resolve-Path -LiteralPath (Join-Path $JavaHome "bin/java$suffix")).Path
$javacPath = (Resolve-Path -LiteralPath (Join-Path $JavaHome "bin/javac$suffix")).Path
$unicodeToken = '中文'
$unsupportedVmText = $null
if ($IsWindows) {
    Add-Type -TypeDefinition @'
using System.Runtime.InteropServices;
using System.Text;
public static class NexaJvmFixtureNativeEncoding {
    [DllImport("kernel32.dll")] public static extern uint GetACP();
    [DllImport("kernel32.dll")] public static extern uint GetSystemDefaultLCID();
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, ExactSpelling=true)]
    public static extern int GetLocaleInfoW(uint locale, uint type, StringBuilder data, int length);
}
'@
    [Text.Encoding]::RegisterProvider([Text.CodePagesEncodingProvider]::Instance)
    $localeText = [Text.StringBuilder]::new(16)
    if ([NexaJvmFixtureNativeEncoding]::GetLocaleInfoW([NexaJvmFixtureNativeEncoding]::GetSystemDefaultLCID(), 0x1004, $localeText, 16) -le 1) { throw 'Cannot read native JVM fixture encoding' }
    $localeCodePage = [int]$localeText.ToString()
    if ($localeCodePage -eq 0) { $localeCodePage = 65001 }
    $processCodePage = [int][NexaJvmFixtureNativeEncoding]::GetACP()
    $nativeEncodings = @($localeCodePage, $processCodePage) | Select-Object -Unique | ForEach-Object {
        [Text.Encoding]::GetEncoding($_, [Text.EncoderExceptionFallback]::new(), [Text.DecoderExceptionFallback]::new())
    }
    $unicodeToken = $null
    foreach ($candidate in @('中文', 'é', '日本語', 'Ж', '한국어', 'مرحبا', 'ไทย', 'א')) {
        $exact = $true
        foreach ($encoding in $nativeEncodings) {
            try { if ($encoding.GetString($encoding.GetBytes($candidate)) -cne $candidate) { $exact = $false } }
            catch [Text.EncoderFallbackException] { $exact = $false }
        }
        if ($exact) { $unicodeToken = $candidate; break }
    }
    if (!$unicodeToken) { throw 'No exact non-ASCII fixture text for native runtime encoding' }
    $releaseVersion = (Get-Content -LiteralPath (Join-Path $JavaHome 'release') | Where-Object { $_.StartsWith('JAVA_VERSION=') } | Select-Object -First 1).Substring(13).Trim('"')
    $legacyEncoding = $releaseVersion.StartsWith('1.8.') -or ($releaseVersion -match '^17\.0\.([0-4])(?:\D|$)') -or $releaseVersion -eq '17'
    $vmEncoding = [Text.Encoding]::GetEncoding($(if ($legacyEncoding) { $localeCodePage } else { $processCodePage }),
        [Text.EncoderExceptionFallback]::new(), [Text.DecoderExceptionFallback]::new())
    foreach ($candidate in @('private-vm-option-fixture-😀', 'private-vm-option-fixture-中文', 'private-vm-option-fixture-é')) {
        try { $null = $vmEncoding.GetBytes($candidate) }
        catch [Text.EncoderFallbackException] { $unsupportedVmText = $candidate; break }
    }
}
$scratch = Join-Path ([IO.Path]::GetTempPath()) ("nexa-mod-compat-" + [Guid]::NewGuid().ToString('N') + ' ' + $unicodeToken + ' with space')
[IO.Directory]::CreateDirectory($scratch) | Out-Null
try {
    # Windows javac may decode command-line paths using the system code page.
    # Keep the real Unicode directory, but pass only relative ASCII compiler args.
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'fixtures/JvmHostModCompatibility.java') -Destination $scratch
    $compileStart = [Diagnostics.ProcessStartInfo]::new($javacPath)
    $compileStart.WorkingDirectory = $scratch
    $compileStart.UseShellExecute = $false
    $compileStart.CreateNoWindow = $true
    $compileStart.RedirectStandardOutput = $true
    $compileStart.RedirectStandardError = $true
    foreach ($argument in @('-encoding', 'UTF-8', '-d', '.', 'JvmHostModCompatibility.java')) { $compileStart.ArgumentList.Add($argument) }
    $compiler = [Diagnostics.Process]::Start($compileStart)
    try {
        $compileOutput = $compiler.StandardOutput.ReadToEndAsync()
        $compileErrors = $compiler.StandardError.ReadToEndAsync()
        if (!$compiler.WaitForExit(30000)) { throw 'Compatibility fixture javac timed out' }
        $compileOutput = $compileOutput.GetAwaiter().GetResult()
        $compileErrors = $compileErrors.GetAwaiter().GetResult()
        if ($compiler.ExitCode -ne 0) { throw "Compatibility fixture javac failed: $compileOutput $compileErrors" }
        foreach ($class in @('JvmHostModCompatibility.class', 'JvmHostModCompatibility$Child.class')) {
            if (!(Test-Path -LiteralPath (Join-Path $scratch $class))) { throw 'Compatibility fixture class missing from Unicode directory' }
        }
    }
    finally {
        if (!$compiler.HasExited) { $compiler.Kill($true); $compiler.WaitForExit() }
        $compiler.Dispose()
    }
    function Write-ModField($writer, [string]$value) {
        $bytes = [Text.Encoding]::UTF8.GetBytes($value)
        $writer.Write([int]$bytes.Length)
        $writer.Write($bytes)
    }
    $modes = @('malformed-option', 'normal', 'exit', 'wait', 'orphan')
    if ($unsupportedVmText) { $modes = @('unsupported-option') + $modes }
    foreach ($mode in $modes) {
        $modeDirectory = Join-Path $scratch $mode
        [IO.Directory]::CreateDirectory($modeDirectory) | Out-Null
        $payload = [IO.MemoryStream]::new()
        $writer = [IO.BinaryWriter]::new($payload)
        $writer.Write([int]0x4E4A564D)
        $writer.Write([int]1)
        Write-ModField $writer $javaPath
        Write-ModField $writer $modeDirectory
        Write-ModField $writer 'JvmHostModCompatibility'
        $jvm = @('-Xmx64m', '-Xcheck:jni', ('-Dfixture.vm.unicode=' + $unicodeToken), '-cp', $scratch)
        if ($mode -eq 'unsupported-option') { $jvm += '-Dfixture.private=' + $unsupportedVmText }
        $writer.Write([int]$jvm.Length)
        foreach ($value in $jvm) {
            if ($mode -eq 'malformed-option' -and $value.StartsWith('-Dfixture.vm.unicode=')) {
                # UTF-8 encoding of an isolated UTF-16 surrogate is invalid in the private protocol.
                $invalid = [byte[]]@(0xed, 0xa0, 0x80)
                $writer.Write([int]$invalid.Length)
                $writer.Write($invalid)
            }
            else { Write-ModField $writer $value }
        }
        $game = @($mode, $hostPath, $javaPath, $modeDirectory, 'private-mod-compat-token-fixture', '中文😀', $unicodeToken)
        $writer.Write([int]$game.Length)
        foreach ($value in $game) { Write-ModField $writer $value }
        $bytes = $payload.ToArray()
        $writer.Dispose(); $payload.Dispose()
        $start = [Diagnostics.ProcessStartInfo]::new($hostPath)
        $start.ArgumentList.Add('--jvm-host')
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardInput = $true
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $parent = [Diagnostics.Process]::Start($start)
        $descendantId = $null
        try {
            $stdout = $parent.StandardOutput.ReadToEndAsync()
            $stderr = $parent.StandardError.ReadToEndAsync()
            $pipe = [IO.BinaryWriter]::new($parent.StandardInput.BaseStream)
            $pipe.Write([int]$bytes.Length); $pipe.Write($bytes); $pipe.Flush()
            $parent.StandardInput.Close()
            if ($mode -eq 'wait') {
                $pidFile = Join-Path $modeDirectory 'child-pid.txt'
                $deadline = [DateTime]::UtcNow.AddSeconds(15)
                while (!(Test-Path -LiteralPath $pidFile) -and !$parent.HasExited -and [DateTime]::UtcNow -lt $deadline) {
                    Start-Sleep -Milliseconds 50
                }
                if (!(Test-Path -LiteralPath $pidFile)) { throw 'CA-like Java child did not start' }
                $descendantId = [int](Get-Content -LiteralPath $pidFile -Raw)
                if ($parent.WaitForExit(200)) { throw 'Waiting parent exited early' }
                $parent.Kill($true)
            }
            if (!$parent.WaitForExit(15000)) { throw 'CA-like host process timed out' }
            $output = $stdout.GetAwaiter().GetResult()
            $errorOutput = $stderr.GetAwaiter().GetResult()
            if ($output -match 'private-mod-compat-token-fixture' -or $errorOutput -match 'private-mod-compat-token-fixture') { throw 'Private game token leaked' }
            if ($mode -eq 'malformed-option') {
                if ($parent.ExitCode -ne 1 -or $errorOutput -notmatch 'Invalid or incomplete JVM host bootstrap request\.' -or $errorOutput -match 'VM created') { throw 'Malformed surrogate option was not rejected by the private bootstrap boundary' }
                Write-Output 'PASS: JNI private bootstrap rejects malformed UTF-8 surrogate without disclosure'
                continue
            }
            if ($mode -eq 'unsupported-option') {
                if ($parent.ExitCode -ne 1 -or $errorOutput -notmatch "JVM options cannot be represented in the selected Java runtime's native encoding\." -or $errorOutput -match 'VM created') { throw 'Unsupported VM option did not fail before VM creation with its fixed diagnostic' }
                if ($output.Contains($unsupportedVmText) -or $errorOutput.Contains($unsupportedVmText) -or $errorOutput.Contains('fixture.private')) { throw 'Unsupported VM option leaked its value' }
                Write-Output 'PASS: JNI native VM option rejects unrepresentable text without disclosure'
                continue
            }
            $expectedCode = if ($mode -eq 'exit') { 7 } else { 0 }
            if ($mode -ne 'wait' -and $parent.ExitCode -ne $expectedCode) { throw "CA-like $mode exit=$($parent.ExitCode): $errorOutput" }
            if ($mode -eq 'normal' -and ($output -notmatch 'NEXA_CA_CHILD_STDOUT' -or $errorOutput -notmatch 'NEXA_CA_CHILD_STDERR' -or $output -notmatch 'NEXA_CA_PARENT_RETURNED')) { throw "CA-like output missing: $output $errorOutput" }
            if ($mode -eq 'exit' -and $output -notmatch 'NEXA_CA_CHILD_EXIT_13') { throw 'Java child exit was not forwarded' }
            if ($mode -eq 'wait') {
                if ($output -notmatch 'NEXA_CA_CHILD_WAITING') { throw 'CA-like wait never reached child' }
                # Unix may briefly retain a killed zombie until the platform reaps it.
                $deadline = [DateTime]::UtcNow.AddSeconds(5)
                $alive = $true
                do {
                    $alive = $false
                    try {
                        $descendant = [Diagnostics.Process]::GetProcessById($descendantId)
                        try {
                            $alive = !$descendant.HasExited
                            if ($IsLinux -and $alive) {
                                $stat = Get-Content -LiteralPath "/proc/$descendantId/stat" -Raw -ErrorAction SilentlyContinue
                                if ($stat -match '^\d+ \(.+\) Z ') { $alive = $false }
                            }
                        } finally { $descendant.Dispose() }
                    } catch [ArgumentException] { }
                    if ($alive) { Start-Sleep -Milliseconds 50 }
                } while ($alive -and [DateTime]::UtcNow -lt $deadline)
                if ($alive) { throw 'Tree stop left a running CA-like Java child' }
            }
            if ($mode -eq 'orphan') {
                $survival = Join-Path $modeDirectory 'child-survived.txt'
                $deadline = [DateTime]::UtcNow.AddSeconds(10)
                while (!(Test-Path -LiteralPath $survival) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 50 }
                if (!(Test-Path -LiteralPath $survival) -or (Get-Content -LiteralPath $survival -Raw) -ne 'NEXA_CA_CHILD_SURVIVED') { throw 'Natural game exit prevented the CA-like analysis child' }
            }
            Write-Output "PASS: CA-like executable child $mode"
        }
        finally {
            [Array]::Clear($bytes, 0, $bytes.Length)
            if (!$parent.HasExited) { $parent.Kill($true); $parent.WaitForExit() }
            $pidFile = Join-Path $modeDirectory 'child-pid.txt'
            if (!$descendantId -and (Test-Path -LiteralPath $pidFile)) { $descendantId = [int](Get-Content -LiteralPath $pidFile -Raw) }
            if ($descendantId) {
                try {
                    $remaining = [Diagnostics.Process]::GetProcessById($descendantId)
                    try { if (!$remaining.HasExited) { $remaining.Kill($true) } } finally { $remaining.Dispose() }
                } catch [ArgumentException] { }
            }
            $parent.Dispose()
        }
    }
}
finally {
    $resolved = [IO.Path]::GetFullPath($scratch)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (!$resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or !(Split-Path $resolved -Leaf).StartsWith('nexa-mod-compat-')) { throw 'Invalid compatibility cleanup path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
