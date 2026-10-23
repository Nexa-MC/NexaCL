# CurseForge credential routing

The product composition root reads the packaged CurseForge key once and passes
it to resource browsing, installation catalog and modpack installation/update.
Services never inspect Desktop assembly metadata or change environment variables.
The common resolver keeps the existing override order: NEXA_CURSEFORGE_API_KEY,
Nexa_CURSEFORGE_API_KEY, CURSEFORGE_API_KEY, then the packaged fallback; blank
values are ignored. The key is sent only to the official CurseForge API, never
to mirrors, Modrinth or artifact downloads, and is not part of cache identities.

Official builds already embed and validate the key. The missing behavior was
delivery from that product setting to the installer and addon catalog. Regression
coverage exercises official modpack metadata, addon fallback and update reuse.
