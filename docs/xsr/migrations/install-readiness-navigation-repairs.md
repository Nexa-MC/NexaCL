# Installation, offline readiness and navigation repairs

CurseForge pack entries remain pinned to the requested project/file identity, SHA-1 and byte length. When its file metadata supplies no download URL, query CurseForge's file download URL endpoint first (unless the project explicitly disallows distribution). Only if CurseForge still supplies no URL may the installer resolve the same bytes through Modrinth's published hash lookup API. It must match hash and length, validate the returned CDN address, and keep the archive's destination name. No guessed CDN addresses, name-based substitutions or skipped required files are permitted. Unresolved files remain failures with a retriable install journal.

Offline readiness remains a local query. Its explicit preparation action uses the existing remediation command for game-file completion and compatible Java selection/download, then repeats the local check. Preparation does not launch Minecraft or claim success from download completion alone.

Navigation changes advance the declared content-host transition key. Ordinary redraws, focus changes and scrolling do not advance it. Reduced-motion preferences remain authoritative.

Only successful tasks complete all stages. Failure/cancellation preserve completed stages and mark the active stage; paused tasks preserve their checkpoint.
