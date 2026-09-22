# Broader taskbar app discovery

Fixed 2026-09-22 after the user showed missing taskbar apps. Milestone 4f only scanned the pinned-shortcut directory: this machine had five links, so that source alone could not supply Steam, Codex, Edge or Terminal. A previous import logged an unavailable target; Brave was available during the new scan and imported successfully.

Startup now merges available pinned shortcuts with visible application windows. RunningApps reads top-level window visibility/style/cloaking and process image/package identity using read-only Win32 APIs. It excludes shell surfaces, tool/owned dialog windows, cloaked windows and Taskbar Groups itself. It reads window AppUserModelID/relaunch properties, preserves packaged identities, and resolves Steam's embedded browser window to the Steam launcher. It does not capture window titles or reuse arbitrary live process command lines.

Pinned launch specifications take precedence. Running candidates deduplicate by resolved launch kind/target against pins and existing items; multiple profiles for the same executable are therefore represented by an existing launcher. Additional items and running-source receipts join the same atomic import batch. Receipts survive Undo, and repeated scans do not resurrect undone imports. Existing groups/order remain intact. F5 rescans the configured sources, including apps opened after startup. No polling service or hook is installed.

Production opts into live discovery explicitly. Disposable profile startup and injected pinned-source constructors continue to avoid the real desktop; tests feed fixture running entries. A separate read-only live discovery run imported into a disposable out/DiscoveryLiveImport profile, never the user's actual profile. It found nine unique entries with zero skipped: Brave, Directory Opus, Task Manager, VS Code, VPN Pro Controller, Codex, Microsoft Edge, Steam and Windows Terminal.

## Limits

This is broader discovery, not an exact Explorer taskbar mirror. Closed packaged/system pins without a discoverable shortcut can remain absent. Inaccessible/custom-hosted apps and unusual relaunch commands may be skipped. Other virtual-desktop cloaked windows are excluded. Desktop app fallback saves its executable with default arguments; it does not recover document/session-specific relaunch state. Discovery order is alphabetical within source batches, not Windows pin order. No automatic pin/reorder changes or actual packaged-app launch tests were added.

Windows API sources: [EnumWindows](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumwindows), [GetApplicationUserModelId](https://learn.microsoft.com/en-us/windows/win32/api/appmodel/nf-appmodel-getapplicationusermodelid), and [window application identity](https://learn.microsoft.com/en-us/windows/win32/properties/props-system-appusermodel-id).

## Verification

Release build on dockerbox / HostPC, VS MSBuild 17.14, /m:2, retained caches: zero warnings/errors. **387 private-desktop checks passed**, all seven suites exited 0: Surface 122, UI 68, Launch 47, Organizer 69, Persistence 45, Storage 29, NativePin 7. New checks cover window filtering, relaunch parsing, combined import/deduplication, packaged identity, invalid-item isolation, restart/undo receipts, missing pin folders and F5 refresh. Native pin requests remain faked; no real app targets launched except the existing disposable launch helper.

Runtime: out/DiscoveryBuild. Package: out/TaskbarGroups-Milestone4g.zip. Local convenience copy: out/Latest. Fixtures: out/Discovery-{Surface,Ui,Launch,Organizer,Persistence,Storage,NativePin}. Results: verification/Milestone4gResults.txt. Worker output/intermediates use the Milestone4g directories under C:\Sandbox\Codex; build log is out/Milestone4g.log.

EXE SHA256: 7374C3EF12D5A20EFE1536318D1CC294C387352AE525A48BE1F3483ECBB30DE5. Nothing committed or pushed. Real profile/group/pin data was not modified by verification.
