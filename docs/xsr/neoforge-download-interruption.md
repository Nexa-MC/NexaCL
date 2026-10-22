# NeoForge download interruption: PCL-DESKTOP-24

## Evidence and code identity

The supplied 2026-10-17 event reports `earlydisplay-4.0.44.jar` and a TLS read
failure containing `SocketException`, wrapped by `HttpRequestException` and
`IOException`. The event release and full Sentry payload were not available in
this investigation; the private mail link could not be read by the connection.
An outer `IOException` alone does not establish file corruption.

The reported `HttpDlConnection.StartCoreAsync`, `DownloadService.DownloadCoreAsync`
and `MinecraftVanillaInstallService.DownloadIfNeededAsync` exist in the archived
`Nexa-MC/PCL-N` `dev` implementation. That installer aggregates source failures
under “下载失败或文件校验失败”; this wording also covers transport failures.
Current `Nexa-MC/NexaCL` `main` at investigation start was `07eac207`.
Its corresponding paths are:

- `ForgeInstallService.InstallAsync` → `PrefetchAsync` → `TransferAsync` for
  NeoForge installer dependencies; `MinecraftInstallService` handles vanilla files.
- `MinecraftInstallService.HttpConnection` performs HTTP/range requests and stream
  reads; TLS/HTTP/read exceptions propagate to `DownloadService` source errors.
- `DownloadService.Sequential` handles ordered source attempts and temporary-file
  commit; `DownloadService.SessionCache` verifies authoritative digest identities.

Default one-pass retry after three seconds predates the event (`8ad27702`). Session
cache reuse was added on 2026-10-19 (`537ece9a`). Neither fact proves that this
particular historical event has been fixed or was merely transient.

## Handling contract and bounded corrections

Each transfer tries its allowed sources in order. A failed pass is retried once
when `network.file-retry` is enabled. Regional/source preferences still apply;
hashless dependencies use original HTTPS and restart rather than resume across
sources. No new mirror policy or TLS validation exception is introduced.

A trusted digest allows session cache resume across staging-directory cleanup.
`FileDownloadWriter` writes `.PCLDownloading`, then atomically renames only on
completion. HTTP 200 ignoring a Range restarts at zero; validated HTTP 206 uses
the complete artifact length and requested offset. HTTP 416 discards the unusable
partial before continuing. Network errors retain reusable private partials until
retry or launcher shutdown; they do not publish incomplete final JARs.

Previously, resume-enabled sequential downloads without metadata size skipped
the response-length check. A premature EOF could reach digest verification and
delete a valid prefix as a digest mismatch, or commit a short uncached response.
Known response lengths are now checked before verification for resumed and fresh
transfers alike. Unknown-length bodies still depend on authoritative digest
verification where available; a complete wrong-digest body is still discarded.

Previously, Forge/NeoForge exhausted transfers threw a new “下载或校验失败”
exception without their causes. They now report “下载失败” and aggregate every
failed-source exception from all passes, preserving HTTP/socket chains and genuine
integrity failures for diagnostics. A successful transfer that fails the final
file verifier contributes a separate explicit integrity exception. No generic
`IOException` is interpreted as proof of corruption.

Deterministic regression fixtures cover short EOF with and without a metadata
size, partial resume/failover, overlong bodies, the NeoForge dependency path with
an injected nested connection-reset exception, delayed retry, wrong-digest cleanup,
staging cleanup, and cache disposal. The injected fault tests handling semantics;
it does not reproduce a real remote TLS session or identify its root cause.

## Verification results

All three new regression cases failed against the unpatched implementation.
After the correction, the managed download/install/trust/retry selection passed
69 cases and the supplemental writer/segment/progress/cancellation selection
passed 12 cases (two overlap). The NativeAOT selection passed 19 cases, including
all new regressions and existing cache/segment/loader contracts. Architecture
checks passed for 72 projects; the changed C# files passed the repository format
check. NativeAOT emitted the existing dependency warning `Jint` `IL2098`.
