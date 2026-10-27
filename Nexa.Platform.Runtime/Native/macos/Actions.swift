import AppKit
import AppIntents
import CoreSpotlight
import SwiftUI
import UniformTypeIdentifiers

@MainActor
final class LocalActions {
    static let shared = LocalActions()
    static let routes = ["launch", "resources", "settings"]
    var activated = false
    private var exitTimer: Timer?
    func open(_ route: String) throws {
        guard Self.routes.contains(route), let url = URL(string: "nexacl://" + route) else { throw ActionError.invalidRoute }
        guard NSWorkspace.shared.open(url) else { throw ActionError.launcherUnavailable }
        activated = true
        exitTimer?.invalidate()
        exitTimer = Timer.scheduledTimer(withTimeInterval: 3, repeats: false) { _ in NSApplication.shared.terminate(nil) }
    }
    enum ActionError: Error { case invalidRoute, launcherUnavailable }
}

struct OpenLauncher: AppIntent {
    static var title: LocalizedStringResource = "Open NexaCL"
    static var openAppWhenRun = true
    @MainActor func perform() async throws -> some IntentResult { try LocalActions.shared.open("launch"); return .result() }
}
struct OpenResources: AppIntent {
    static var title: LocalizedStringResource = "Open NexaCL resources"
    static var openAppWhenRun = true
    @MainActor func perform() async throws -> some IntentResult { try LocalActions.shared.open("resources"); return .result() }
}
struct OpenSettings: AppIntent {
    static var title: LocalizedStringResource = "Open NexaCL settings"
    static var openAppWhenRun = true
    @MainActor func perform() async throws -> some IntentResult { try LocalActions.shared.open("settings"); return .result() }
}
struct NexaShortcuts: AppShortcutsProvider {
    static var appShortcuts: [AppShortcut] {
        AppShortcut(intent: OpenLauncher(), phrases: ["Open \(.applicationName)"], shortTitle: "Open NexaCL", systemImageName: "play.circle")
        AppShortcut(intent: OpenResources(), phrases: ["Open resources in \(.applicationName)"], shortTitle: "Resources", systemImageName: "square.grid.2x2")
        AppShortcut(intent: OpenSettings(), phrases: ["Open settings in \(.applicationName)"], shortTitle: "Settings", systemImageName: "gearshape")
    }
}

@MainActor
final class ActionsDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        let args = CommandLine.arguments
        if args.contains("--self-test") {
            guard LocalActions.routes == ["launch", "resources", "settings"] else { exit(1) }
            print("NexaCL local actions: ready")
            exit(0)
        }
        NexaShortcuts.updateAppShortcutParameters()
        if args.contains("--index") {
            let titles = ["launch": "NexaCL — Launch", "resources": "NexaCL — Resources", "settings": "NexaCL — Settings"]
            let items = LocalActions.routes.map { route -> CSSearchableItem in
                let attributes = CSSearchableItemAttributeSet(contentType: .item)
                attributes.title = titles[route]
                attributes.contentDescription = "NexaCL Firefly"
                attributes.keywords = ["NexaCL", "Minecraft", route]
                return CSSearchableItem(uniqueIdentifier: "nexacl." + route, domainIdentifier: "org.nexacl.actions", attributeSet: attributes)
            }
            CSSearchableIndex.default().indexSearchableItems(items) { error in
                if error != nil { fputs("Core Spotlight indexing unavailable\n", stderr); exit(1) }
                print("indexed"); exit(0)
            }
            DispatchQueue.main.asyncAfter(deadline: .now() + 4) { exit(2) }
        } else {
            DispatchQueue.main.asyncAfter(deadline: .now() + 2) {
                if !LocalActions.shared.activated {
                    do { try LocalActions.shared.open("launch") }
                    catch { fputs("NexaCL protocol handler unavailable\n", stderr); NSApplication.shared.terminate(nil) }
                }
            }
        }
    }
    func application(_ application: NSApplication, continue userActivity: NSUserActivity,
                     restorationHandler: @escaping ([NSUserActivityRestoring]) -> Void) -> Bool {
        guard userActivity.activityType == CSSearchableItemActionType,
              let identifier = userActivity.userInfo?[CSSearchableItemActivityIdentifier] as? String,
              identifier.hasPrefix("nexacl.") else { return false }
        do { try LocalActions.shared.open(String(identifier.dropFirst(7))); return true } catch { return false }
    }
}

@main
struct NexaActionsApp: App {
    @NSApplicationDelegateAdaptor(ActionsDelegate.self) var delegate
    var body: some Scene { Settings { EmptyView() } }
}
