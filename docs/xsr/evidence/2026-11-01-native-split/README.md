# Complete native payload delta measurement

Linux x64 NativeAOT runtime payloads assembled from the production bootstrap, UI,
Host, nine business/engine images, JVM Host and independent update helper. Packaging
validation rejected missing required files and excluded development/update state.
The source and target retain the same product build to isolate one implementation
change: Updates' private HTTP timeout changes from 20 to 19 minutes. Delta metadata
uses ordered alpha.18/alpha.19 labels; neither experimental payload was published.
The temporary source edit was restored immediately after the second AOT publish.

`measurement.json` records actual file totals, compressed delta size, verified
round-trip reconstruction, per-module new bytes and both SHA-256 values. Every
module except `nexa-updates.so` is byte-for-byte identical and contributes no new
module bytes. These are real native images, not synthetic unit-test payloads.
The final verified runtime retains the original 20-minute timeout.
