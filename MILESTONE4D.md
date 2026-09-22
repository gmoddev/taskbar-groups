# External shortcut drops and atomic import

Milestone 4d implemented 2026-09-21. Drop files or shortcuts from Explorer directly onto an organizer app to create a group, or onto an existing group to add them. Edge drops insert standalone items in source order. Drops onto the selected group's member strip or a member tile append to that group.

## Behavior

- External drops advertise Copy only. Move-only sources are rejected, including a forced drop event. Original files and shortcuts are never moved, deleted or executed by import.
- A multi-file drop, background import or multi-select Add apps operation creates one save and one undo entry. Undo removes the entire import and any group it created; redo restores the same identities.
- Unavailable inputs are skipped during initial validation and reported inline. Accepted inputs are revalidated under the writer lock. If validation or staging then fails, none of that batch is published.
- Exact launch specifications already imported anywhere in the organizer, including earlier entries in the batch, are skipped. Use internal organizer dragging to move an existing item between groups. Duplicate-only imports create no new revision or history.
- Duplicate comparison uses path, arguments, working directory and packaged-app flag. Links remain links: import preserves the source .lnk path, while LaunchService reads its target, arguments and working directory without launching or resolving shell UI. Different link paths are not collapsed merely because they target the same executable.
- Group names/icons and launcher links are generated automatically through the existing store. Imported members preserve source order, after the target app or existing members. The created/extended group is selected.
- Revision checks reject stale state. Tile targets carry their render revision. Failures appear in the organizer status area and log, without modal dialogs.

This operates inside the organizer. It does not implement Explorer taskbar icon-on-icon dragging or drops onto an actual pinned taskbar shortcut.

## Implementation

OrganizerStore.ImportItems clones the current state under the shared writer lock, validates the target or insertion position, checks each launch specification, filters duplicates, and applies all imports/grouping before one existing Commit call. No schema change is required. The normal snapshot/generation publication boundary and durable history remain authoritative.

frmOrganizer routes external tile, edge and member drops through ImportPaths. Add apps and background drops use the same batch path. Internal reorder/group gestures retain their existing behavior and now honor the source's allowed Move effect. Imported paths are normalized before validation.

Faults before the organizer pointer commit preserve the complete previous layout. Existing crash-recovery semantics still apply if a failure happens after publication. Unreferenced staging/generations are retained under the existing retention policy.

## Verification

Remote Release build on dockerbox / HostPC using VS 2022 MSBuild 17.14, /m:2: **0 warnings, 0 errors**. Worker inspection reported 24 logical CPUs and approximately 17.5 GiB free RAM. Dependency caches and build state were retained.

| Private-desktop suite | Passed |
| --- | ---: |
| SurfaceProbe | 84 |
| UiProbe | 45 |
| LaunchProbe | 47 |
| OrganizerProbe | 69 |
| PersistenceProbe | 45 |
| StorageProbe | 29 |
| NativePinProbe | 7 |
| Total | **326** |

All suites exited 0 against the final executable. Twenty new surface checks cover batch history, center grouping, group/member additions, ordered edge insertion, link metadata, duplicate/invalid inputs, undo/redo identities, move-only rejection, injected pre-commit failure, stale revision and late-item validation failure. The tests verify no import dispatch and unchanged source files.

Drag tests invoke WinForms drag handlers with file-drop data on a private desktop. They are not a physical Explorer-to-window OLE mouse test. The rendered organizer was inspected in out/Milestone4d-Surface/Organizer.png; the fixture error in its status area intentionally demonstrates nonmodal rollback handling.

Native pin requests remain faked in UI tests; the native adapter is queried read-only. No real taskbar pin, Start-menu registration, user profile or input desktop changed. Only LaunchProbe's disposable target helper ran. All probe processes exited.

## Artifacts

- Remote source: C:\Sandbox\Codex\Workspaces\taskbar-groups.
- Build state: C:\Sandbox\Codex\Builds\taskbar-groups\Milestone4d.
- Release output: C:\Sandbox\Codex\Artifacts\taskbar-groups\Milestone4d.
- Build log: C:\Sandbox\Codex\Logs\taskbar-groups\Milestone4d.log; local out/Milestone4d.log.
- Local runtime: out/Milestone4d-Build.
- Clean package: out/TaskbarGroups-Milestone4d.zip.
- EXE SHA256: 5FB9E526580FC54A7DB25EE74C4A3D0FD72ACB225C4512A0F7E331DA69843BB1.
- Results: verification/Milestone4dResults.txt.
- Fixtures: out/Milestone4d-{Surface,Ui,Launch,Organizer,Persistence,Storage,NativePin}.

Rebuild using VERIFICATION.md with these milestone paths. Compile/run the seven harnesses using the MILESTONE4C.md instructions and fresh fixtures.

Physical OLE dragging, native pin confirmation/two-group identity, Start-menu lifecycle, legacy-data merging, richer item editing, storage retention, analyzer coverage and the Windows/DPI matrix remain on the roadmap. Nothing has been committed or pushed.
