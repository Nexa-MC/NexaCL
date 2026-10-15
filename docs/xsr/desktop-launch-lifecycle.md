# Desktop launch lifecycle follow-up

Read together with architecture-updated.md, capability-fabric-updated.md and sidecar-protocol-updated.md.

Host Services own Java selection, runtime acquisition, process supervision and crash classification. Desktop projects sealed state and dispatches commands; it never inspects a Process or classifies log text. No Sidecar or remote analysis dependency is introduced.

- Java major selection is checked against the pending requirement. An installed matching runtime wins; otherwise a supported runtime is acquired after the explicit version choice. Stale choices cannot resolve another launch.
- Redirected process output is continuously drained with bounded retention. Abnormal exit triggers asynchronous Service analysis; normal exit and user cancellation do not. Analysis reports are retained per session in sealed collection state.
- The task capsule has one continuous input owner, including its expanded area. Process controls address a specific session; logs remain an unavailable entry.
- The selected launching instance exposes a disabled launch action with progress. Version rows place the selected check before the name and expose modify, settings and delete actions.

Validation: Desktop input/navigation regressions, Service process/Java contracts, architecture checks, trim and NativeAOT shell smoke.

Shell bubbles share a bottom-up vertical dock. Hidden and closing bubbles reserve no slot; overlay motion supplies interruptible entry, exit and reflow with reduced-motion support. Version rows reserve a check column before the icon, and actions use the existing expanding-capsule primitive.

Shell task, launch and process bubbles use intrinsic single-line caption measurement,
not fixed expanded widths. Their 48 DIP collapsed circle keeps its right dock anchor;
expanded width is bounded by the 420 DIP authored maximum and the available window
width. Overflowing captions retain their complete accessible label and can move in
both directions through the ordinary wheel, horizontal trackpad, horizontal thumb
or Left/Right while the bubble has keyboard focus. Enter/Space still invoke the
original action. The task progress track stretches inside the caption area.

This reuses the renderer's capsule and scroll state. NXML `Button Scroll="true"`
is admitted only with `HoverExpand="true"`; it scrolls the caption, never its child
business content. Vertical scrolling, stick-to-end and content drag gestures remain
stack-only contracts. Desktop presenters continue to supply text, visibility and
commands, while intrinsic measurement, scroll bounds and input stay in the renderer.

Overlay sibling reordering preserves native attachment and in-flight animation. Capsule cursor feedback uses the same stable hover target as expansion; busy launch progress retains the enabled primary-action color.
