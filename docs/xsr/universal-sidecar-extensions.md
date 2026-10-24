# Universal Sidecar extensions

Accepted product direction, 2026-10-24. This replaces the resource-only exposure
policy in function-patches.md, sidecar-ui-patches.md and sidecar-ui-modules.md.
Implementation and executed verification are recorded separately; a declaration
in this document alone does not establish delivered coverage.

## Required automatic coverage

All product-owned executable function implementations participate in build-time
instrumentation without attributes, special signatures or per-feature adapters.
The catalog includes instance/static/generic/async/iterator methods, accessors,
constructors and compiler-generated implementations. Abstract/runtime declarations
have no managed implementation body; the inventory must identify them explicitly
instead of counting them as instrumented. Required CLR initialization still runs
before constructor body replacement. NativeAOT receives the instrumented IL at
publication; runtime CLR assembly loading, runtime code generation and synchronous
Sidecar round trips are not the patch execution mechanism.

Automatic catalogs contain stable target identities, signatures, phases and
representable value operations. A new product method must appear through the
normal build alone. Unpatched methods retain a small predictable fast path;
argument packing and interpreter work occur only for active targets. Original
exceptions, caller information, generic constraints, finally blocks and async
return identities must retain their behavior. Coverage and performance need
executable regression evidence, including the actual published NativeAOT binary.

## One UI language

Sidecar UI uses the same .nxml pages, .c.nxml components, .d.nxml dynamic fragments
and .nss styles as product UI. Do not add another control-kind enum or translate
the document into the old resource-card node vocabulary. New shared controls and
NXML properties become usable by Sidecars through the common compiler/loader.
Documents, components and styles travel as an immutable hash-verified registration
bundle; compilation/validation precede activation. Source parsing or IPC must not
run on unchanged render frames.

Any named product node can be an append/prepend/before/after, modification or
replacement target. Targets may be registered before a page or dynamic node
exists. The shared NXML projection pipeline discovers later nodes automatically;
feature controllers do not declare extension slots or perform Sidecar rendering.
Selectors use stable keys and ancestry rather than recycled entity indices.

Projection keeps controller-owned logical nodes and bindings alive. Replacement
changes the presented subtree, not the controller's object ownership. Module
nodes have independent namespaces and exposure identities. New intent captures
are checked against the live exposure; teardown cancels outstanding exchanges.
Retirement reveals the preceding live layer or the current product presentation,
including product changes made while the layer was active. Insertions compose
in deterministic activation/registration order; the latest live replacement wins.

## Boundaries and acceptance

The Desktop composition root connects the generic extension coordinator once.
Runtime owns registration, activation, immutable State and asynchronous command
exchange. The renderer consumes ordinary State/intent and shared NXML projection
contracts, with no dependency on concrete Sidecar sessions or Services. Existing
caption/card payloads remain decodable for compatibility, not the future UI model.
Registration/session identity, payload limits, cancellation, shape validation and
deactivation remain enforced while the resource-only target allowlists disappear.

Required regression: add a new ordinary method and an unrelated new NXML page
without extension annotations/adapters; register patches and insert/modify/replace
that page's controls; verify state/intent, dynamic reload, restoration, stale input
rejection, isolation, and NativeAOT execution. Do not label partial signature or
page coverage as universal support.
