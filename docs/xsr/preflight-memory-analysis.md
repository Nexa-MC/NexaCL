# Memory analysis temporarily unavailable

The product memory estimator is suspended. The catalog retains a TemporarilyUnavailable memory-analysis fact, not an editable switch or persisted opt-in. The settings form filters out entries without an available editable consumer. Raw hardware memory facts and user-selected JVM memory remain available.

Foundation installs an unavailable estimate projection instead of the local or online estimator. Every estimate capability is explicitly TemporarilyUnavailable, never an observed zero. This path does not calculate the model or read calibration history. Standalone model code stays available for offline development and its algorithm tests.

Preflight skips memory-analysis and estimate-confidence rules when the estimate status is temporarily unavailable, including any stale estimate values in the supplied snapshot. Java, loader, mod dependency, files and actual GPU checks remain active. Platform UI collapses the disabled estimates to one unavailable row and omits estimate-based hardware advice. The suspended analysis does not itself create a launch confirmation or repair action.

Skipped preflight rule capabilities are also TemporarilyUnavailable rather than Available(false); consumers must not interpret unexecuted analysis as a passed check. Their repeated rows are hidden behind the single memory-analysis status.

Historical validation at the original suspension revision: 48 focused settings/estimate/preflight service tests passed; all 267 Desktop tests passed; architecture checks passed for 70 projects. Includes stale-prediction suppression, retained Java blocking, unavailable rule projection, a non-interactive settings row and removal of prior estimate advice.
