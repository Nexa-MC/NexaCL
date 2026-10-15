# Protected account persistence (SEC-01)

The Desktop live account store uses `ProtectedLaunchProfilePort`. Schema 1 JSON remains
the in-memory serialization and read-only legacy import format, not the on-disk live format.
The whole serialized roster is protected before creating any temporary file. Public state
continues to exclude credentials. Windows uses current-user DPAPI; macOS uses Keychain and
Linux Secret Service to hold random AES-256 keys, with authenticated AES-GCM envelopes.
No secret appears in process arguments. Missing/locked secure storage fails closed, with
an actionable account error; it never falls back to plaintext or silently resets the store.

Migration reads bounded legacy JSON, validates it, encrypts in memory and atomically replaces
the original. The exact old `.invalid` and GUID-named temporary files owned by this port
are protected in place as well. External legacy import sources remain byte-for-byte unchanged.
Corrupt/unreadable data is retained without producing another plaintext quarantine copy.
Any failed load locks this port against writes for the session. Direct Save on an existing
store first validates Load, so constructing a second port cannot overwrite unreadable data.
The legacy JSON port remains a compatibility/test adapter; production composition must use
the protected port. Native platform tests are distinct from injectable cipher contract tests.

The native protection test follows the asynchronous storage contract: await
`InitializeAsync([])` before creating an envelope, and initialize a new protector with the
envelope's key ID before reopening it on Linux/macOS. Both protectors are disposed so their
cached key material is cleared. Windows initialization remains a no-op before current-user
DPAPI operations. The cross-platform CI matrix exercises this same lifecycle.

The 2026-10-15 macOS CI run reached the native protection fixture after a 5 minute
37 second NativeAOT publication and then remained in that fixture for over seven
minutes, whereas the preceding run completed it in under one second. This does
not establish that the keychain was locked: the available live CI state has no
native stack or Security.framework error. It exposes an unbounded headless wait
path because the two legacy Keychain calls can request OS interaction and their
`Task.Run` cancellation token cannot interrupt a call already in progress.

The isolated, explicitly enabled system-keyring fixture therefore disables
Keychain user interaction only within its own process, saving and restoring the
prior setting in `finally`. It still performs real native key creation, reopen,
authenticated decryption and tamper rejection. An interaction requirement fails
the test immediately rather than waiting for an inaccessible prompt; production
protector behavior is unchanged. The macOS CI step reselects its disposable
keychain and search list, explicitly sets its lock interval and unlocks it again
immediately before the fixture. That step has a two-minute deadline, and an
always-run cleanup removes only the CI-owned disposable keychain.

Linux requires `/usr/bin/secret-tool` and an unlocked Secret Service. Native backend failure
is reported as unavailable rather than replaced by a filesystem key. This does not defend
against malware already controlling the user's session or erase historical external backups.
