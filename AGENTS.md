# Agent instructions (NexaCL)

## Git workflow defaults

- **GIT-01 — Finish requested changes.** After finishing a **fix** (or other self-contained code change the user asked for), **commit and push** by default without waiting for a separate “请提交/推送”, unless the user says not to push, the change is exploratory/WIP, or the push target/branch is unclear.
  **Why:** Completed work should reach the intended branch without another approval round, while explicit user limits and unfinished work remain protected.

- **GIT-02 — Keep the branch target.** Prefer the current tracking branch (`git push origin HEAD` / push the branch that was being worked on).
  **Why:** Keeping the established target avoids sending a change to an unrelated branch.

- **GIT-03 — Explain the commit.** Follow existing commit-message style (conventional commits, focus on why).
  **Why:** Consistent messages make the purpose of a change easier to review and recover from history.

- **GIT-04 — Confirm destructive operations.** Still ask before destructive git operations (force-push, hard reset, amending published history).
  **Why:** These operations can discard work or rewrite history that other contributors rely on.

## XSR migration rules

- **XSR-01 — Isolate the migration checkout.** XSR development belongs on `refactor/xsr` in a dedicated Git worktree outside the active `dev` checkout. Do not build the new architecture inside the `dev` working directory.
  **Why:** Separate checkouts keep migration edits and build outputs from interfering with active legacy development.

- **XSR-02 — Migrate contracts, not the legacy implementation.** This is a clean-slate branch. Do not restore, merge, copy wholesale, add a project reference to, or add a submodule for legacy implementation code. Inspect the `dev` worktree read-only and migrate only documented behavior, algorithms, data contracts, and compatibility requirements, not legacy type or assembly shapes. Every source project on this branch must be created intentionally for XSR; do not recreate the legacy solution or its assembly graph as migration scaffolding. Do not modify a legacy implementation merely to make an XSR migration diff smaller.
  **Why:** XSR must preserve required behavior without inheriting the legacy dependency graph or changing the reference implementation to simplify a migration.

- **XSR-03 — Record boundary decisions first.** Treat `docs/xsr/` as the architecture lock. Update the relevant document before changing a boundary or compatibility promise.
  **Why:** Documenting the decision first makes architectural and compatibility changes explicit and reviewable.

- **XSR-04 — Keep services independent of presentation.** New services must not reference Avalonia, Desktop, renderer internals, ViewModels, or service locators. They receive commands/queries and publish state/events.
  **Why:** Explicit inputs and outputs keep service behavior testable without a UI or hidden dependency resolution.

- **XSR-05 — Keep the renderer on the state/intent boundary.** `Nexa.Xsr.Renderer` is the canonical renderer. It reads state and emits intent; it must not resolve concrete services or call a Sidecar directly.
  **Why:** Routing work through state and intent prevents renderer code from bypassing service and process boundaries.

- **XSR-06 — Preserve the Sidecar data plane.** Sidecar hot paths use generated numeric dispatch and an extensible binary protocol. Do not add synchronous IPC, JSON data-plane messages, reflection dispatch, or CLR object exchange across the process boundary.
  **Why:** An explicit generated binary boundary is intended to avoid blocking hot paths, runtime type coupling, and reflection-dependent dispatch.

- **XSR-07 — Close each migration with evidence.** Every migration task is a closed unit with a migration note, parity or contract tests, architecture tests, and AOT/trim validation where applicable.
  **Why:** These checks provide evidence that migrated behavior, dependency boundaries, and supported deployment modes remain valid.

- **XSR-08 — Do not bless breaks through baselines.** Updating an API baseline does not make a breaking change acceptable.
  **Why:** A baseline records an API surface; changing that record alone does not resolve compatibility obligations.

- **XSR-09 — Keep the defined product-version format.** XSR product versions use the exact dotted forms defined in `docs/xsr/versioning.md`: `2.0.0`, `2.0.0.alpha.N`, `2.0.0.beta.N`, and `2.0.0.ci.ffffff`.
  **Why:** A single documented version format avoids inconsistent product identities across builds and release channels.
