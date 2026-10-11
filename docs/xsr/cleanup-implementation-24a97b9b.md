# Cleanup implementation of inventory 24a97b9b

Scope: C01-C07, D01-D07 review, and S01-S13 responsibility splits. Original inventory is historical evidence; current sources are rechecked.

Splits retain types, assemblies, member bodies, visibility, initialization order, test registration order and cancellation/ownership semantics. Public API, protocol IDs and generated PXML output stay unchanged. Existing uncommitted changes are retained and excluded from cleanup commits, including changes relocated into partial files.

D05/D06/D07 remain deliberately separate: six-line HTTP stream reads do not justify a new inter-project dependency; cancellation-source disposal depends on each owner's lock and terminal protocol; small test assertions do not justify a shared test assembly. D04 may share a Desktop-only visibility helper, without using animation-owning DesktopBubbleLayout.

Validation: Services 724, Desktop 264, UI.Next 105, PXML 40 and Avalonia scene backend 30 tests pass. Release builds report no warnings or errors. NativeAOT publication is not claimed by these managed test runs.
