# Guided group publishing

Milestone 4b implemented 2026-09-21. Select a group and choose **Pin group…** to show inline Windows instructions. **Show shortcut** prepares the group's saved link and requests Explorer to select that exact file. There is no file-search step in the organizer.

This is a guided manual pinning flow. The application does not invoke a pin verb, force a pin, remove existing pins, register Start-menu entries, or claim that pinning succeeded. Windows completes the user's final action if it offers Pin to taskbar.

## Platform findings

Microsoft documents current-app pin requests and requires foreground UI, an app Start-menu entry and user approval. The current guidance says the limited-access restriction is being removed starting with KB5074105 (builds 26200.7705 / 26100.7705). It is therefore incorrect to treat LAF approval as a universal blocker on current Windows. [Current desktop pinning guidance](https://learn.microsoft.com/en-us/windows/apps/develop/windows-integration/pin-to-taskbar).

The reviewed APIs accept the current app or an AppListEntry, rather than an arbitrary .lnk path. Whether a separately launched group identity plus an appropriate Start-menu entry can reliably request its own pin remains a useful next prototype; it is not disproved by this review. [RequestPinAppListEntryAsync](https://learn.microsoft.com/en-us/uwp/api/windows.ui.shell.taskbarmanager.requestpinapplistentryasync). Secondary-tile pinning is separately documented as restricted access and is not a drop-in replacement for the current launcher architecture. [Secondary-tile guidance](https://learn.microsoft.com/en-us/windows/uwp/launch-resume/secondary-tiles-pin-to-taskbar).

The inline fallback follows the user's Windows context-menu action, including Show more options where applicable. It says “if offered” because shell policy and context-menu availability are not verified here. [Microsoft support guidance](https://support.microsoft.com/en-us/office/collab-files/create-a-desktop-shortcut-for-an-office-program-or-file).

## Implementation

GroupPublishing resolves an active group from committed organizer data and rejects stale revisions, standalone items and hidden dissolved groups. It prepares the stable GUID-named shortcut, repairing a missing/outdated projection from its committed generation. It verifies exact shortcut bytes and rechecks the organizer revision before returning a path.

The publishing directory is validated before repair. Failed replacement of a corrupt read-only shortcut cannot proceed to Explorer dispatch. A correct existing shortcut can remain usable even if an unnecessary repair attempt fails; byte comparison determines whether it matches committed data.

Show shortcut dispatches the known Windows Explorer executable through LaunchService with a separately quoted /select argument. Expected failures stay in the organizer status and Publishing/Storage log. No pinning result is inferred from process startup.

Guidance is inline, named for the selected group, dismissible, and hidden when selection or the organizer revision changes. Preparation does not change organizer state or add an undo entry. Original group identities, launch arguments and AppUserModelIDs remain unchanged.

## Verification

Final Release on dockerbox / HostPC, VS 2022 MSBuild 17.14, /m:2: **0 warnings, 0 errors**. Existing dependency caches/build state were retained; worker inspection reported approximately 17.8 GiB free RAM.

| Private-desktop suite | Passed |
| --- | ---: |
| SurfaceProbe, including publishing | 42 |
| UiProbe | 45 |
| LaunchProbe | 47 |
| OrganizerProbe | 69 |
| PersistenceProbe | 45 |
| StorageProbe | 29 |
| Total | **277** |

All suites exited 0 against the same final executable. Publishing additions verify disabled state without a group, the stable shortcut path, repair of a missing link, identity readback, named nonmodal guidance, unchanged organizer revision, exact Explorer selection arguments, no false pin-success claim, stale revision rejection, hidden-group rejection and read-only stale-projection failure.

Explorer dispatch uses the internal launch seam in tests: **no real Explorer window or pin action was invoked**. Actual context-menu availability, final pin creation, per-group native pin requests, pin persistence after rename/reboot and existing-pin transitions remain unverified. Only LaunchProbe's disposable helper executed; no real profile, pins or input desktop changed. Probes exited and fixture restrictions were restored.

The rendered guide was inspected from out/Milestone4b-Surface/Organizer.png. Logs: verification/Milestone4bResults.txt.

## Build and artifacts

- Source: C:\Sandbox\Codex\Workspaces\taskbar-groups.
- Intermediates: C:\Sandbox\Codex\Builds\taskbar-groups\Milestone4b.
- Output: C:\Sandbox\Codex\Artifacts\taskbar-groups\Milestone4b.
- Remote log: C:\Sandbox\Codex\Logs\taskbar-groups\Milestone4b.log; local out/Milestone4b.log.
- Clean package: out/TaskbarGroups-Milestone4b.zip.
- EXE SHA256: 0C4C2B01FC99224E8D961C2BE520C06483D52B5332B9820DEB28BB3FC3001464.

Build using VERIFICATION.md with these paths. Compile the six Framework64 harnesses using the MILESTONE1.md pattern. Use fresh disposable fixtures with a hidden parent and bounded wait; all harnesses verify their private desktop. Final fixture names: out/Milestone4b-Surface, -Ui, -Launch, -Organizer, -Persistence and -Storage.

## Next work

Prototype a native current-app request for an individual group in a disposable interactive environment, checking desktop API support, Start-menu identity resolution, policy and cancellation. Do not call the API or alter pins on the user's gaming desktop during automated verification.

This guided flow does not automatically remove old individual app pins or mirror organizer order in Explorer. True taskbar-native dragging remains a separate feasibility track. Legacy-data merging, richer imports/editing, drag polish, disk retention, analyzer/resource work, Windows/DPI compatibility and .NET migration remain in ROADMAP.md. Nothing has been committed or pushed.
