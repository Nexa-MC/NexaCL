# XSR-836: configured storage locations

## Closed boundary

The global Storage settings page exposes configured launcher settings, logs and cache
paths, registered Minecraft library roots, the active root, its `versions` directory,
the selected instance directory, and Foundation's actual managed Java runtime roots.
These are read-only facts. This slice adds no storage preference, migration policy,
recursive inventory, disk estimate, or folder creation on query.

Setup owns `StorageLocationsQuery` and the immutable snapshot. Its service computes
and freezes path facts. Composition captures one service-owned Minecraft Library
snapshot and passes only the sealed selection DTO to Setup. Desktop consumes the
sealed query and library revision; it neither constructs directories nor reads files.
No new project-reference edge is required.

Java roots come from the configured runtime registry roots, never from the currently
selected game folder. Cache is nullable when the host did not configure it. A missing
instance stays unselected. The selected path is checked against the root-qualified
`versions` directory. Results copy every collection and retain the source revision.

## Presentation and lifetime

Paths appear in ordinary padded form rows with literal, wrapped, right-aligned values.
Multiple library or Java roots remain separate lines. Unconfigured paths and absent
selection have explicit localized labels, not a guessed default or empty fake fact.
Reads run through the asynchronous query router. Navigation or disposal cancels the
pending read. Absent optional Library state or query yields an explicit missing-selection
or disconnected presentation. A changed library revision clears obsolete current-root
facts, retries a failure from the previous scope, and retires the old request; a late snapshot for
an obsolete revision cannot replace current paths. Completion uses the existing
storage rebuild deferral so a settings click is consumed before nodes are rebuilt.

Four catalog entries now declare delivered `State` consumers: Minecraft library,
instance directory, managed Java roots, and download cache. Default placement policy,
temporary directory aggregation, disk use, copy-on-write, and foreign launcher
migration remain reserved. They are not inferred from these configured paths.

## Regression coverage

Services cover configured roots, collection ownership, no filesystem creation,
root switching with identical instance IDs, missing configuration, foreign selection,
cancellation before source access, and exact catalog availability boundaries.
Desktop coverage checks query-backed visible right-aligned literal rows, changed root
projection, missing Library/query composition, failure retry on scope change, and retirement
of a delayed obsolete scope without guessing Java roots or creating folders.
