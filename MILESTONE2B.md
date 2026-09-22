# Ordered organizer state and durable undo

Implemented 2026-09-21. This supplies the data layer for the requested organizer gestures; **the drag UI and undo buttons are not implemented yet**. Ordinary startup continues using the existing manager. The new organizer store activates only through its internal `Initialize` API, which the future organizer surface will call. Tests activate it solely in disposable profiles.

## Implemented behavior

- An ordered layout contains standalone items and groups, with exactly one owning position for each item. Group membership and member order use stable IDs; launcher metadata remains attached to each item.
- Item onto item creates a group at the target position with an automatic name and composite icon. Item onto group appends it. Drag-out restores a standalone item at a specified insertion position. Separate operations reorder the layout or a group's members.
- One-member groups dissolve into their surviving standalone item; the original launcher identity remains as a hidden redirect with a one-item popup. This does not automatically launch the application. Empty groups become tombstones. Nested groups, self-drops, duplicate identities/ownership, and invalid positions are rejected without changing committed state.
- Undo and redo persist across process restarts, with up to 50 entries in each history stack. New edits clear redo. Undo/redo publish fresh revision tokens, so an old editor cannot become valid merely because the layout returned to an earlier state.
- Existing editor saves/deletes join the same global commit path after activation. Removing members in the editor restores them as standalone items. Deleting a group returns its members to the layout and is undoable. Existing one-member groups remain valid; automatic dissolution occurs through the organizer move operations.

## Storage authority and atomicity

```text
%LOCALAPPDATA%\TaskbarGroups\
    Organizer\Current.xml           single committed organizer pointer
    Organizer\Previous.xml          previous complete pointer
    Organizer\Damaged.xml           quarantined pointer after recovery save
    Organizer\Versions\<id>.xml      immutable layout, membership, history references
    config\<stable-root>\Versions\<id>\
        ObjectData.xml, GroupImage.png, GroupIcon.ico, Launcher.lnk
```

An operation clones the latest state under the shared writer lock and verifies the caller's revision. It stages all changed groups as immutable generations without publishing individual group pointers, validates the complete ownership graph and resources, writes and flushes the snapshot, and atomically replaces the organizer pointer. Staging failures and interrupted pre-commit operations leave the entire old layout authoritative. An interruption after the pointer swap exposes the entire new layout.

Once activated, all group reads, popup lookups, and writes resolve through that snapshot. Old per-group pointers remain preserved but are ignored. The activation lock is shared with ordinary group saves/deletes; those paths recheck activation after acquiring it so they cannot report success writing an obsolete pointer during a race.

Activation imports the healthy existing groups, preserves their IDs, member metadata and legacy aliases/AppUserModelIDs, and leaves original files intact. It is idempotent; interrupted first activation leaves the existing group store authoritative. New groups use GUID paths and launcher arguments. New standalone entries are inert launch specifications: adding/grouping never executes a target or changes Explorer pins.

Generated shortcuts remain recoverable projections of committed data. Publication runs after the commit; `RepairLinks` can retry it, and the existing manager repairs links for its visible groups. Automatic composites refresh when membership changes; explicitly chosen/imported artwork is retained. Undo restores referenced earlier resources rather than rewriting them.

Readers validate the whole referenced snapshot. Corruption can recover the previous complete snapshot; recovery never mixes memberships from different generations. Unsupported future schemas fail rather than downgrade. Unreadable organizer pointers do not intentionally reactivate old per-group data. If neither complete snapshot can be read, startup follows the existing quiet storage-error path.

## Verification

Release compilation on `dockerbox` / `HostPC`, VS 2022 MSBuild 17.14, `/m:2`: **0 warnings, 0 errors**. Existing NuGet packages and incremental state were retained.

Windows 11 private-desktop suites against the same final EXE:

| Suite | Passed | Coverage |
| --- | ---: | --- |
| `OrganizerProbe.cs` | 69 | Activation/interrupted activation and save race; ordered imports; atomic cross-group moves; member ownership; durable undo/redo and branch clearing; dissolution/redirect and empty-group deletion; all model gestures; automatic icons; existing-editor integration; stale revisions; read-only pointer failure; corrupt/nil/duplicate snapshots and future-schema refusal; history limits; manager/popup smoke |
| `PersistenceProbe.cs` | 45 | Previous group-generation, identity, conflict, cache, recovery and startup regressions |
| `StorageProbe.cs` | 29 | Protected-install ACLs, migration, per-user storage, quiet errors and recovery, and startup regressions |

**143 checks passed, zero unexpected failures.** Cross-group tests terminate a child process after the first and second group staging steps, snapshot flush, snapshot validation, pointer flush, and pointer commit. Readers see the complete old or complete new membership. Separate undo/redo processes are terminated after commit, then their results are read back. This verifies process interruption, not physical power-loss durability.

No target applications were launched, no real profile was activated, no pins/Explorer settings were changed, and the input desktop was never switched. Temporary ACL changes in the storage fixture were restored. Source test logs are collected in `verification/Milestone2bResults.txt`.

## Build and artifacts

- Worker source: `C:\Sandbox\Codex\Workspaces\taskbar-groups`.
- Intermediates: `C:\Sandbox\Codex\Builds\taskbar-groups\Milestone2b`.
- Output: `C:\Sandbox\Codex\Artifacts\taskbar-groups\Milestone2b`.
- Log: `C:\Sandbox\Codex\Logs\taskbar-groups\Milestone2b.log`, copied to `out/Milestone2b.log`.
- Clean package: `out/TaskbarGroups-Milestone2b.zip` (no harnesses/fixtures).
- EXE SHA256: `0D9CBFC94929C623F5B36424E74F5D057A8AD0CF9B5D22415B54A1134E75EC07`.

Build with the MSBuild command in `VERIFICATION.md`, substituting the output/intermediate paths above. Compile the three harnesses as described in `MILESTONE2A.md`, with `OrganizerProbe` as the additional harness name. Run each in a separate fresh disposable output directory with `Start-Process -WindowStyle Hidden` and a bounded wait; inspect `Verification.txt` and the process exit code. Final fixtures: `out/Milestone2b-Final-Organizer`, `out/Milestone2b-Final-Persistence`, and `out/Milestone2b-Final-Storage`.

## Remaining work

The next user-facing work still needs the organizer surface, drop hit zones/previews, keyboard actions, undo/redo controls, and supported pin publishing. Finish the launch/failure handling prerequisites in roadmap milestone 3 before calling it ready for daily use.

Do not activate the organizer in normal startup until its surface can display standalone entries: the legacy manager displays only groups. Activation currently imports existing healthy groups once; later out-of-band legacy directories are retained on disk but need a deliberate import/merge action before appearing in the organizer. Recovery is whole-snapshot, not selective salvage of individual damaged resources shared by multiple history entries.

History stack length is bounded, but immutable snapshots, generations, abandoned staging, tombstones and old links are retained on disk. Safe disk retention and a restoration UI remain pending. Do not downgrade an activated profile to an executable that lacks organizer authority: old per-group pointers intentionally remain stale. Physical power-loss behavior, real Explorer pin activation, taskbar-native drops, packaged-app/URI launches, and monitor/DPI behavior remain unverified.
