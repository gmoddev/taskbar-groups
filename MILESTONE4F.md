# Automatic pinned-shortcut import and compact strip

Implemented 2026-09-22. Normal startup imports available `.lnk` files from the user's Windows pinned-shortcut folder. Existing organizer groups/order are retained; new discoveries append alphabetically. The organizer uses one horizontally scrolling strip, expanding for selected members and optional controls.

PinnedApps preserves link bytes in immutable SHA-256-named files under LocalAppData/TaskbarGroups/ImportedPins. Arguments, working directory and shell metadata survive removal of the original pin. Nested shortcut targets and external icon resources remain external dependencies. Own group launchers are excluded by AppUserModelID/current executable target. Invalid inputs are logged/skipped and retried on restart.

Imported items and source-path receipts commit atomically in one undo operation. Receipts survive undo/redo so restarting does not resurrect an undone import. Identical snapshots deduplicate; links previously imported from different paths are not semantically deduplicated. Previously imported source paths are not refreshed if their contents change. New filenames are discovered on later startup; source removal never removes organizer entries. Unused staging files can remain after failures. Retention and explicit reimport controls remain future work. Older binaries do not preserve receipts on save; avoid downgrading this profile.

Production explicitly passes the pinned folder to frmOrganizer. Parameterless construction and startup with a disposable LocalAppData override do not import the real user's pins.

## Real taskbar limits

Creating a group automatically reveals inline Windows pin options. It does not open another window or dispatch a pin request automatically. The dedicated pin window still requires a button action and Windows confirmation; actual per-group pin creation remains unverified.

Shortcut discovery is not a complete ordered taskbar representation. Packaged/system pins without a discoverable link and running-only apps may be absent. Initial order is alphabetical. No real pins are automatically moved, replaced or unpinned when grouping, dissolving or undoing.

Microsoft's [pin request guidance](https://learn.microsoft.com/en-us/windows/apps/develop/windows-integration/pin-to-taskbar) requires user interaction/foreground eligibility and Windows consent. [Managed taskbar policies](https://learn.microsoft.com/en-us/windows/configuration/taskbar/pinned-apps) provide managed ordering with policy refresh semantics, not ordinary live per-drag synchronization. No supported arbitrary live reorder API was found within the no-hooks architecture. No Taskband writes, policy changes, Explorer restart or injection were added.

## Verification and artifacts

Release built on dockerbox / HostPC, VS MSBuild 17.14, /m:2: **0 warnings, 0 errors**. Caches/intermediates retained. Private-desktop suites: Surface 107, UI 68, Launch 47, Organizer 69, Persistence 45, Storage 29, NativePin 7: **372 checks passed**, all exited 0.

New checks cover startup import, independent snapshots, exact arguments/working directory, original pin removal, own/corrupt link exclusion, restart idempotence, undo/restart/redo, pre-commit failure/retry, stale revisions, repaired inputs, duplicate receipts without history, compact strip and inline pin guidance. PinnedStrip.png was visually inspected. Native requests remain faked; native queries are read-only. No real profile, pins or input desktop changed. Physical OLE and actual pin confirmation remain unverified.

Runtime: out/Milestone4f-Build. Package: out/TaskbarGroups-Milestone4f.zip. Build log: out/Milestone4f.log. Results: verification/Milestone4fResults.txt. Surface fixture: out/Milestone4f-SurfaceVerified; other fixtures: out/Milestone4f-Final-{Ui,Launch,Organizer,Persistence,Storage,NativePin}.

Worker workspace: C:\Sandbox\Codex\Workspaces\taskbar-groups. Intermediates: C:\Sandbox\Codex\Builds\taskbar-groups\Milestone4f. Output: C:\Sandbox\Codex\Artifacts\taskbar-groups\Milestone4f.

EXE SHA256: 9E5BCD851130A0D689FF887D85B696F9A1B62B5F85C3479FCD12C22B90C5DD8B. Nothing committed or pushed.
