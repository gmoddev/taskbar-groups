# Popup placement hardening

Milestone 5a implemented 2026-09-21. This fixes the source-confirmed KI-07 monitor/taskbar indexing bug and the auto-hide path's use of primary-monitor dimensions for other monitors.

## Changes

The popup selects one Screen from its click position and passes that screen's bounds and working area to PopupPlacement. It no longer builds a filtered taskbar list or pairs entries by monitor index. The unused FindDockedTaskBars implementation is removed.

Placement handles reserved space at any display edge, negative coordinates and clicks outside the current monitor layout. It normally opens above the click, uses room below when needed, and clamps the result to the selected working area with reduced padding for tight fits. Empty/disjoint working areas fall back to monitor bounds. An oversized popup keeps its leading edge visible; this change does not resize or scroll its contents.

The popup uses manual positioning. When a launch error adds a status area, placement is applied again so a normally sized expanded window stays within the working area. No focus activation, hooks or Explorer changes are introduced.

Microsoft documents that [Screen.FromPoint](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.screen.frompoint) returns the nearest screen when no display contains the point. Its [WorkingArea documentation](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.screen.workingarea) describes reserved desktop space and states that auto-hide taskbars expose the full monitor area. Consequently, this geometry fix does not determine the dimensions of an expanded auto-hide taskbar or guarantee avoiding it.

## Verification

Release build on dockerbox / HostPC, VS 2022 MSBuild 17.14, /m:2: **0 warnings, 0 errors**. The worker reported 24 logical CPUs and approximately 15.6 GiB free RAM; existing dependency caches and build state were retained.

| Private-desktop suite | Passed |
| --- | ---: |
| UiProbe | 68 |
| SurfaceProbe | 84 |
| LaunchProbe | 47 |
| OrganizerProbe | 69 |
| PersistenceProbe | 45 |
| StorageProbe | 29 |
| NativePinProbe | 7 |
| Total | **349** |

All seven suites exited 0 against the final executable. Twenty new deterministic geometry cases cover the four reserved edges, negative-X/Y monitors, taskbar-less secondary displays, auto-hide-style bounds, corners, off-monitor/extreme anchors, invalid working areas, multiple reserved edges and exact/tight/oversized fits.

Three additional integration checks show real popups on the available private-desktop displays, verify launch-error expansion stays within their working areas, and verify nearest-monitor placement for an off-screen anchor. Synthetic negative/multi-monitor cases do not replace a physical display compatibility matrix.

Native pin queries remained read-only, pin request outcomes remained faked, and only the existing disposable launch helper executed. No real profile, Start-menu entries, taskbar pins or input desktop changed. All probe processes exited.

## Limits and next work

KI-07 is partially addressed. Per-monitor DPI, display changes while a popup is open, live auto-hide expansion, oversized-content scrolling, Explorer restart and actual pin identity remain open. The popup still uses its existing fixed-size layout; this is not a DPI-awareness migration.

Organizer external drops and durable batch undo remain as implemented in milestone 4d. Popup file drops, native pin acceptance and Start-menu lifecycle remain separate roadmap items.

## Build and artifacts

- Remote source: C:\Sandbox\Codex\Workspaces\taskbar-groups.
- Build state: C:\Sandbox\Codex\Builds\taskbar-groups\Milestone5a.
- Output: C:\Sandbox\Codex\Artifacts\taskbar-groups\Milestone5a.
- Remote log: C:\Sandbox\Codex\Logs\taskbar-groups\Milestone5a.log; local out/Milestone5a.log.
- Local runtime: out/Milestone5a-Build; clean package: out/TaskbarGroups-Milestone5a.zip.
- Results: verification/Milestone5aResults.txt.
- Fixtures: out/Milestone5a-{Ui,Surface,Launch,Organizer,Persistence,Storage,NativePin}.
- EXE SHA256: 022AABF97BCF7A9CD00D008AD8415E27B851D1FBC2C6247921A7B83BF9994580.

Build with the VERIFICATION.md pattern and these paths. Compile the seven harnesses against the final runtime using the MILESTONE4C.md instructions; run hidden parents with fresh private-desktop fixtures and inspect both exit codes and Verification.txt.

Nothing has been committed or pushed.
