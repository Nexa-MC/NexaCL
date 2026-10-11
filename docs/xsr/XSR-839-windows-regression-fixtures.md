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

A later complete run reached a launch-progress teardown which killed its shell
and immediately deleted the game directory. That fixture now waits for the
entire process tree, disposes its own process service, and acquires the real
exclusive game-directory lease before cleanup. The lease acquisition proves
that the executor's asynchronous shared receipt was released. The authoritative
lock keeps its original sharing requirements; no deletion retry hides the race.

Focused Windows regressions passed: four launcher/auxiliary Java lease cases,
ten managed Java lifecycle cases, all four launch-hook platform cases, and the
launch-progress lifecycle case. These
fixtures do not replace physical Minecraft client or publisher-signing evidence.
