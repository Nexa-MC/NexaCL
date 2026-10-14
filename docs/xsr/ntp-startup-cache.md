# NTP/1 finite startup cache

Architecture boundary, 2026-10-14. The first Splash may read the launcher settings
snapshot and one fixed `settings/ntp-startup-cache.json` file. It must not enumerate
theme sources or read ZIP, NSS, font, image directories, audio or video resources.

The cache is a last-known-good presentation snapshot produced by the actual native
preparation path after a committed activation. Temporary previews do not write it.
It contains a version, exact installation/source identity, observed generation,
two PNG brand variants, three optional RGBA colours, and a finite Splash motion enum.
It does not convey file grants, resource paths, theme commands or runtime bindings.

The writer validates before replacing the existing cache, writes a sibling temporary
file and flushes it before atomic replacement. The total JSON budget is 768 KiB;
each PNG is at most 256 KiB and 512 pixels on either axis. Native preparation emits
128-pixel, non-interlaced 8-bit RGB/RGBA PNG snapshots. The reader validates JSON
depth and fields, source identity, paired variants, PNG structure/CRC and bounded
scanline decompression. Invalid or incomplete cache files are ignored without writes.

A cache is eligible only when its source identity exactly matches the committed
`appearance.theme-pack-id` in the existing bootstrap settings policy. The default
identity is `builtin`. Invalid policy fields cannot admit a third-party cache.
Safe mode bypasses the cache completely and uses the built-in brand and colours;
the user's committed dark-mode and reduced-motion preferences remain effective.

The cache is display-only. Its recorded generation does not restore a live theme
transaction or authorize resources. The normal startup preparation independently
validates the selected source and then updates the already visible Splash and main
window with the current activation generation. Cache persistence errors are logged
after the successful commit and do not roll back a valid theme activation.
