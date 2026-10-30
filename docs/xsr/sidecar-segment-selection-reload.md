# State-driven segmented navigation reload

Sidecar market navigation changes Selected declarations when entering a category or following a website detail link. Reload previously updated button flags but retained the segmented track's initial selected entity, so its thumb continued to paint over Discover.

After applying children, reconcile selection only when a child declaration changes. Preserve the track and transition instances and let the existing renderer animate the new target. An unrelated reload does not override interaction state. No IPC, protocol, capability, permission or service ownership changes.

Focused regression reproduces the stale target before the fix, then checks forward/back selection, empty selection and retained motion objects. Validation remains atomic before application; cancellation and invalid module retirement semantics are unchanged. The scan is linear in direct segment children only during reload, allocation-free and outside the render hot path.
