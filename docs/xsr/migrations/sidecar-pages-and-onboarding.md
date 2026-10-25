# Sidecar pages and onboarding

Sidecars can register standalone NXML pages in addition to projecting into existing
Host elements. `SidecarFeatures.Pages` is required alongside `Nxml`; schema 3 gains
append-only operations `Page = 7` and `OnboardingPage = 8`. A page target is a single
`#Key`, unique within its session. Its compiled root must be a Page. Existing
projection operations and the four built-in destinations retain their contracts.

The Desktop composition edge owns page entities, dynamic content, selection,
navigation, and retirement. `ui.sidecar.page.open` takes the target as its scalar
argument and can resolve only a Page in the invoking module's active session.
`ui.sidecar.onboarding.continue` advances the current onboarding page. These are
reserved Host intents, never Sidecar command implementations. Cross-session page
access and stale-source navigation are rejected. No concrete account provider is
part of the Host API.

On first run, admitted onboarding pages precede the existing setup sequence.
Continuing or retiring an onboarding page cancels its pending actions, destroys
its input drafts, and resumes the next page or the original setup page;
normal navigation resumes the built-in destinations. Session retirement cancels
pending calls, removes projected controls and pages, and restores a live Host page.
The navigation footer is a stable container above the shell-owned expand control.
The four primary items expose stable keys NavigationLaunch, NavigationInstall,
NavigationResources, and NavigationSettings for ordinary projection operations.

Discovery and signature verification also run during first-run startup. Failed or
absent Sidecars do not prevent setup. Construction and registration remain inert;
external connections start only after ACTIVATE. The existing binary protocol,
numeric dispatch, source budgets, and signature policy remain in force.

Validation covers negotiation, same-session resolution, page/open/return/retirement,
onboarding continuation, footer placement, architecture gates, and publish checks.
Per-frame work reuses the immutable snapshot; compilation and IPC remain outside
the render hot path. This is an independent optional feature, not a protocol or
product version bump.

## Validation evidence (2026-10-24)

- Runtime: 182 cases, including required feature negotiation, duplicate page
  targets, same-session resolution, and rejection after retirement.
- Protocol: 36 cases and the existing bounded-allocation/round-trip gates.
- Renderer: 124 cases. Desktop: 373 cases, including external page open/return,
  retirement, onboarding continuation, draft destruction, and pending-call CANCEL.
- NXML: 85 cases published and executed as a Windows x64 NativeAOT application;
  standalone document reload retains focus/drafts and rejects changed structure.
- Architecture: all 72 projects pass the existing ownership/reference gates.
- The external Sidecar binary fixture decodes actual declarations and snapshots,
  compiles five modules, checks a 1024x600 setup page, footer placement, blank page
  navigation, and authenticated market visibility above Settings.
- A real self-contained executable renamed to .nsc completes bootstrap challenge,
  handshake, registration, initial snapshot, READY and shutdown against the Host.
  That test does not activate credentials or call a real authentication service.

The production signature requirement is unchanged. Native binary contract tests
are not release-signature or real-user login evidence. An unchanged presentation
snapshot reuses compiled documents and performs no IPC or source parsing per frame.
