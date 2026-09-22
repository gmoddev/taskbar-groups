# Direct grouping as the primary UI

Milestone 4e implemented 2026-09-22 in response to the user's correction: direct dragging is the product workflow; hardening and pin support must not displace it.

## What changed

The toolbar now contains only Add apps, Undo and Redo. Open, Group with, Group settings, Reload and Pin are no longer persistent toolbar/header controls. Right-click actions and keyboard equivalents retain the relevant optional operations.

Dragging still creates and extends groups immediately with automatic names, icons, saved membership and launcher links. Drag-out, one-member dissolution, edge reorder and atomic multi-file import retain their existing behavior. The member panel collapses when no group is selected, leaving the main app area available.

The organizer no longer constructs frmClient or frmGroup. Rename uses a compact inline text field: Enter saves, Escape cancels. RenameGroup is a revision-checked undoable store operation preserving identity and the existing automatic-icon policy. Choose icon saves through the existing recoverable group store, without opening the legacy editor, and is undoable.

Right-click a group for Open, Rename, Choose icon or Pin to taskbar. Right-click an app for Open or the optional keyboard-style Group with action. Context actions that re-render controls are deferred until menu handling completes. Ctrl+G, Enter, F5 and the existing movement/undo shortcuts remain available.

The README now describes the actual drag workflow and removes the old editor-first usage instructions.

## Scope

This is organizer-mediated grouping, not native Windows taskbar icon-on-icon interception. Actual Windows pin confirmation remains an optional, unverified preview. No taskbar hooks or forced pinning were added.

Legacy editor classes remain in source for regression coverage and compatibility work, but the organizer no longer exposes them. Older advanced appearance/argument-editing controls are not exposed in this minimal surface; persisted configurations remain intact. A fuller compact item-details experience can be added separately.

## Verification

Release on dockerbox / HostPC, VS 2022 MSBuild 17.14, /m:2: **0 warnings, 0 errors**. Worker inspection reported 24 logical CPUs and approximately 17.4 GiB free RAM; caches and incremental state were retained.

| Private-desktop suite | Passed |
| --- | ---: |
| SurfaceProbe | 89 |
| UiProbe | 68 |
| LaunchProbe | 47 |
| OrganizerProbe | 69 |
| PersistenceProbe | 45 |
| StorageProbe | 29 |
| NativePinProbe | 7 |
| Total | **354** |

All suites exited 0 against the final executable. Surface checks cover the minimal toolbar, collapsed member area, actual group context menu, inline rename/Enter, cancellation/Escape, no legacy windows, identity/automatic-icon preservation, custom icon save and undo, plus all existing grouping/import/publishing checks. Historical editor bridge assertions were replaced by inline behavior assertions.

The rendered organizer was inspected at out/Milestone4e-Surface/Organizer.png. Its fixture-failure status is deliberately produced by the import rollback test. Native request outcomes remain faked; native queries are read-only. No real pins, Start-menu entries, profile or input desktop changed. Only the disposable launch helper executed. All probes exited.

Physical OLE dragging, actual Windows pinning and the wider Windows/DPI matrix remain unverified.

## Artifacts

- Remote source: C:\Sandbox\Codex\Workspaces\taskbar-groups.
- Build state: C:\Sandbox\Codex\Builds\taskbar-groups\Milestone4e.
- Output: C:\Sandbox\Codex\Artifacts\taskbar-groups\Milestone4e.
- Build log: C:\Sandbox\Codex\Logs\taskbar-groups\Milestone4e.log; local out/Milestone4e.log.
- Local runtime: out/Milestone4e-Build.
- Clean package: out/TaskbarGroups-Milestone4e.zip.
- Results: verification/Milestone4eResults.txt.
- Fixtures: out/Milestone4e-{Surface,Ui,Launch,Organizer,Persistence,Storage,NativePin}.
- EXE SHA256: 0A7CD7E0B732DD91EF327249366471B4D9BED282D7810C1CCEC9E37A893A20AA.

Build and test using VERIFICATION.md and the harness instructions in MILESTONE4C.md with these paths. Nothing has been committed or pushed.
