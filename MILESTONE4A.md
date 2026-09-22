# Organizer surface: drag-to-group

Milestone 4a implemented 2026-09-21. Ordinary startup now opens the organizer window. Group-ID and legacy-name shortcuts still open the existing popup launcher.

## Available workflow

- Add apps using the file picker or drop executable/shortcut files onto empty space in the top grid. Imports validate targets without launching them. External drops advertise Copy; target files are never moved or deleted.
- Drop one app onto another tile's center to create a group, with an automatic name, composite icon and generated launcher. Drop onto an existing group to add the app.
- Select a group to show its member strip. Drag a member into empty space or an edge in the top grid to move it out. A two-member group dissolves into its survivor; its old identity continues resolving to a one-item popup.
- Drop at the left/right quarter of a top-level tile to reorder rather than group. The center half is the grouping zone. Preview borders distinguish centers from insertion edges. Group tiles can be reordered but cannot be nested.
- Reorder members at member-tile edges, or drop in the member strip's empty space to append to that group. Cross-group moves use the destination group tile or empty member strip.
- Undo/Redo buttons and Ctrl+Z/Ctrl+Y use persistent history. Alt+Left/Right reorders the focused item, Ctrl+Shift+M moves a member out, and Ctrl+G / Group with opens a keyboard-accessible target menu.
- Open launches the selected/focused app through LaunchService or opens the selected group's existing popup entry path. Group settings opens the optional existing editor for renaming, artwork and member configuration.
- F5/Reload refreshes saved state. Escape cancels a drag. Failed mutations keep/reload committed state and report in-window status and an Organizer log entry.

Grouping has no required editor, name entry, icon picker or generated-file browsing step. Actual taskbar pin publishing remains separate: the organizer generates links but does not pin groups or change existing Explorer app pins.

## Activation and persistence

The new surface calls OrganizerStore.Initialize because it can display both groups and standalone items. First activation imports existing healthy groups into the global snapshot; subsequent starts load it. Popup-only startup does not activate the organizer. All group readers/writers retain the global authority rules from MILESTONE2B.md.

Drag payloads are private, same-window objects with source identity, parent and snapshot revision. Previews reject self-drops and stale payloads. Each mutation passes its revision to the store; a concurrent writer causes an inline conflict and reload rather than an overwrite. The UI does not move tiles optimistically before a commit. Generated link repair remains retryable on Reload.

Multi-file imports currently commit one accepted file at a time, with one undo entry per file; duplicate/unavailable entries are skipped and counted. A later failure does not roll back earlier accepted files. Duplicate detection uses the stored target path for empty-argument entries; it is not a complete canonical launch-spec equivalence check.

## Verification

Final Release compilation on dockerbox / HostPC, VS 2022 MSBuild 17.14, /m:2: **0 warnings, 0 errors**. Existing package caches and worker build state were retained. Worker inspection reported approximately 16.5 GiB free memory.

| Private-desktop suite | Passed |
| --- | ---: |
| SurfaceProbe | 31 |
| UiProbe | 45 |
| LaunchProbe | 47 |
| OrganizerProbe | 69 |
| PersistenceProbe | 45 |
| StorageProbe | 29 |
| Total | **266** |

All six suites exited 0 against the same final executable. SurfaceProbe raises the real WinForms drag-enter/drop/query-continue events and invokes wired button/key actions. It verifies center/edge semantics, group creation/append, member reorder, drag-out/dissolution, surviving launcher identity, undo/redo, keyboard grouping/moves, stale revisions, injected write failure, optional editor disposal/refresh, launch-plan dispatch and reopening with durable history.

The native DoDragDrop mouse loop was not driven with physical input. Drag threshold behavior, prolonged hover, OLE interaction with Explorer and real multi-monitor/DPI operation still need interactive testing. The rendered private-desktop form was inspected from out/Milestone4a-Verified-Surface/Organizer.png; placeholder fixture apps share the application's icon.

Testing found and fixed a menu lifetime error: ToolStrip must complete its own close processing before its menu is disposed. The optional editor bridge also defers disposal/refresh until the editor's save/close handler finishes.

No real profile was activated, no pins changed, no Explorer restart or input-desktop switch occurred. Only LaunchProbe's harmless disposable helper was executed; SurfaceProbe uses a dispatch seam for Open. Final probe/helper processes exited and storage fixture ACL restrictions were restored. Logs: verification/Milestone4aResults.txt.

## Artifacts

- Worker source: C:\Sandbox\Codex\Workspaces\taskbar-groups.
- Intermediates: C:\Sandbox\Codex\Builds\taskbar-groups\Milestone4a.
- Remote output: C:\Sandbox\Codex\Artifacts\taskbar-groups\Milestone4a.
- Remote log: C:\Sandbox\Codex\Logs\taskbar-groups\Milestone4a.log; local out/Milestone4a.log.
- Runtime package: out/TaskbarGroups-Milestone4a.zip (no probes or fixtures).
- EXE SHA256: DF3A167E771B2AC6CF8DBC3D5A577D4E220ED33C9863C037CE03804F9D4613D4.

Use the MSBuild command in VERIFICATION.md with these paths. Compile SurfaceProbe like the Framework64 winexe harnesses in MILESTONE1.md, then run in a fresh disposable fixture with a hidden parent and bounded wait. Final fixtures are out/Milestone4a-Verified-Surface, -Ui, -Launch, -Organizer, -Persistence and -Storage.

## Remaining product work

Supported inline taskbar pin publishing, existing-pin transitions and true taskbar-native drop feasibility remain unimplemented. The current organizer order is not Explorer's pin order.

Later out-of-band legacy groups still need a deliberate merge/import action after activation. Packaged/running-app pickers, direct external-file drops onto group tiles, richer standalone launch editing/removal, atomic batch import, drag auto-scroll/long-hover polish and a fuller accessibility pass are pending. Keyboard grouping uses the target menu; moving a grouped item between particular member positions across groups is currently an append then reorder workflow.

The legacy editor remains optional and retains its own limits. Earlier package artwork/cache retention, resource/analyzer audit, selective recovery, Windows compatibility and .NET migration work also remains open. Save and icon operations are synchronous, so large profiles or slow/network targets can pause rendering. This is an implemented first organizer surface, not daily-use certification.

Nothing has been committed or pushed.
