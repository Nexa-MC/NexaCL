# macOS native chrome and application identity

The Avalonia application name is `NexaCL` before native application menus are
created, for both the early splash lifetime and the standalone shell lifetime.
Bundle metadata alone does not override Avalonia's default application name.

Cocoa owns the outer window shape. macOS must not apply the Windows/Linux
24-DIP scene clip inside that native shape: the two different radii leave a
visible duplicate arc in the title bar. Full-width renderer title painting and
the measured traffic-light safe area remain intact, including resize/restore.
The extension-height hint does not move Cocoa's 28-DIP traffic-light container.
The backend aligns the standard NSWindow buttons and their NSView containers to
the renderer's 52-DIP caption, preserving the container's top edge. It reapplies
this after native resize/show transitions and leaves fullscreen geometry to Cocoa.

Native regression evidence must include Cocoa frames and a compositor capture,
not only RenderTargetBitmap (which excludes native traffic lights and menus).
Both macOS architectures and light/dark modes are checked by the native probe.
