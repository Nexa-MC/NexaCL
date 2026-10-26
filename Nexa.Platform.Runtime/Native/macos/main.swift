import Foundation
import Network

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
} else {
    fputs("Unsupported local platform operation\n", stderr)
    exit(2)
}
