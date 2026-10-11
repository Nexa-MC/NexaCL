# XSR-839 — Windows regression fixtures

The complete local Services suite reached two fixtures which did not preserve the
real Windows contracts. The managed Java fixture combined `bin/java.exe` without
canonicalizing separators, while the real planner canonicalizes the final path.
It now uses `Path.GetFullPath` too. Journal path, digest, ownership and lease
validation are unchanged.

The launch-hook fixture returned from inside a `cmd.exe` `for /L` block. A direct
shell-only reproduction returned 0 instead of the requested 17, independently of
the product hook worker. The readiness loop now jumps to a label and returns
outside the block. Exit-code, live-child, process-tree cleanup and detached-child
assertions remain unchanged.

Focused Windows regressions passed: four launcher/auxiliary Java lease cases,
ten managed Java lifecycle cases, and all four launch-hook platform cases. These
fixtures do not replace physical Minecraft client or publisher-signing evidence.
