import Foundation
import Network
import AppKit
import QuickLookUI

final class ScreenshotPreview: NSObject, QLPreviewPanelDataSource {
    let url: NSURL
    init(url: URL) { self.url = url as NSURL }
    func numberOfPreviewItems(in panel: QLPreviewPanel!) -> Int { 1 }
    func previewPanel(_ panel: QLPreviewPanel!, previewItemAt index: Int) -> QLPreviewItem! { url }
}

let arguments = CommandLine.arguments
if arguments.count == 2 && arguments[1] == "network-cost" {
    let monitor = NWPathMonitor()
    let complete = DispatchSemaphore(value: 0)
    let queue = DispatchQueue(label: "NexaCL.NetworkCost")
    let lock = NSLock()
    var printed = false
    func emit(_ value: String) {
        lock.lock()
        defer { lock.unlock() }
        if printed { return }
        printed = true
        print(value)
        complete.signal()
    }
    monitor.pathUpdateHandler = { path in
        if path.status != .satisfied { emit("unknown") }
        else { emit(path.isExpensive || path.isConstrained ? "metered" : "unmetered") }
    }
    monitor.start(queue: queue)
    if complete.wait(timeout: .now() + 3) == .timedOut { emit("unknown") }
    monitor.cancel()
} else if arguments.count == 2 && arguments[1] == "thermal-state" {
    switch ProcessInfo.processInfo.thermalState {
    case .nominal: print("normal")
    case .fair: print("fair")
    case .serious: print("serious")
    case .critical: print("critical")
    @unknown default: print("unknown")
    }
} else if arguments.count == 3 && arguments[1] == "preview" {
    let url = URL(fileURLWithPath: arguments[2])
    guard arguments[2].hasPrefix("/"), url.pathExtension.lowercased() == "png",
          FileManager.default.fileExists(atPath: url.path) else { exit(2) }
    let app = NSApplication.shared
    app.setActivationPolicy(.accessory)
    let source = ScreenshotPreview(url: url)
    guard let panel = QLPreviewPanel.shared() else { exit(3) }
    panel.dataSource = source
    let closeObserver = NotificationCenter.default.addObserver(forName: NSWindow.willCloseNotification,
        object: panel, queue: .main) { _ in app.terminate(nil) }
    panel.reloadData()
    panel.makeKeyAndOrderFront(nil)
    app.activate(ignoringOtherApps: true)
    print("ready")
    fflush(stdout)
    withExtendedLifetime((source, closeObserver)) { app.run() }
} else {
    fputs("Unsupported local platform operation\n", stderr)
    exit(2)
}
