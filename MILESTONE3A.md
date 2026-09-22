# Shared launch contract and quiet popup failures

Implemented 2026-09-21 as milestone 3a. This completes the popup launch portion of milestone 3; icon/editor isolation, the asynchronous release lookup, analyzer work, and the drag organizer UI remain pending.

## Behavior

All popup mouse clicks, number keys and Ctrl+Enter open-all use `LaunchService`. Executables, resolved shell links, packaged apps, directories and registered URIs have explicit rules. Arguments remain a separate field; the launcher never builds a command by joining an executable path to its arguments or invokes a command interpreter implicitly.

Expected validation, I/O, COM and process-start failures return a failed result, write a `[TaskbarGroups:Launch]` entry to the existing user-data `Logs/Storage.log`, and appear in a nonmodal popup label. Open-all continues after a failed member and honors the group's opt-out. Deactivation during a batch cannot dispose the controls before later members are processed. The app does not reactivate itself to show an error. Out-of-range number keys are ignored explicitly instead of relying on swallowed indexing exceptions.

## Launch contract and compatibility changes

| Input | Behavior |
| --- | --- |
| Executable | Existing absolute `.exe` or `.com`; environment variables in paths expand. Arguments are passed unchanged. Nonempty working directories must exist; empty means the target's parent. |
| Shell link | Read stored target, arguments, working directory and AppUserModelID through COM without calling `Resolve`. Link arguments precede additional item arguments, separated by one space. Explicit item working directory overrides link working directory. An empty target can use a packaged AppUserModelID. Missing/malformed/unresolvable links fail quietly. |
| Packaged app | The Windows-app flag selects package-family!application identity, optionally prefixed with `shell:AppsFolder\`. Activate through IApplicationActivationManager, preserving arguments and requesting AO_NOERRORUI. Working directory is not applicable. |
| Directory | Existing absolute folder; separate arguments are rejected. |
| URI / internet shortcut | Absolute registered URL protocol, or a bounded `.url` file with one URL entry in its InternetShortcut section. Separate arguments, file URLs, arbitrary shell namespaces, javascript/data schemes and unregistered protocols are rejected. |

Stored fields/schema are unchanged. Existing nonempty working directories, including historical install-directory defaults, count as explicit overrides; this change does not silently rewrite saved metadata.

Relative paths/PATH searches, implicit script/document association launches and file URLs are now rejected. Choose an explicit interpreter executable if a script is intentional. Only executable extensions above are accepted; this is a contract restriction, not executable authenticity verification. Link-specific run-as/show/hotkey settings, advertised/MSI links and arbitrary shell-namespace links are not preserved by target extraction. Nested/cyclic links are not a promised supported format: the tested Windows COM reader rejected synthetic fixtures before the managed traversal; errors were returned quietly. Managed traversal also has an eight-link limit and cycle detection.

Shell execution is explicitly enabled with verb `open` and `ErrorDialog = false`; there is no automatic `runas`. A target may still request its own elevation or display its own UI. These settings control our launch error handling, not another application's behavior. Framework and modern .NET have different defaults, so the setting is explicit. [Microsoft ProcessStartInfo documentation](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.useshellexecute).

The link reader uses SLGP_RAWPATH without repair/search UI. [Microsoft IShellLinkW.GetPath documentation](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishelllinkw-getpath). Packaged activation uses AO_NOERRORUI (2). [Microsoft ActivateApplication documentation](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-iapplicationactivationmanager-activateapplication).

The shared ShellLink writer now clears its allocated PROPVARIANT and releases the COM object in finally paths. The reader releases property values and COM ownership similarly. IPersistFile slot declarations and the icon-location string-buffer declaration were corrected; PROPVARIANT reserves sufficient native storage for x64. This is targeted interop cleanup, not a completed native-resource audit.

## Verification

Release built on `dockerbox` / HostPC with VS 2022 MSBuild 17.14 and `/m:2`: **0 warnings, 0 errors**. The worker had about 18.5 GiB free memory; existing package caches and older build directories were preserved.

| Private-desktop suite | Passed |
| --- | ---: |
| LaunchProbe | 47 |
| OrganizerProbe | 69 |
| PersistenceProbe | 45 |
| StorageProbe | 29 |
| Total | **190** |

All four suites exited 0 with zero unexpected failures against the same EXE. Launch tests cover ordinary and malformed links, exact arguments, spaces/Unicode, working-directory precedence, path expansion, missing paths, unsupported types, bad protocols, packaged metadata, click/key parity, batch continuation/deactivation, inline status and absence of native modal windows. The only actual targets were two invocations of the disposable helper (direct and through a link); their arguments, working directories and private desktop were read back.

Folder/URI/packaged dispatch is tested using an internal delegate seam, with no production CLI/environment switch. **Actual browser, Explorer and packaged-app activation remains unverified**, including packaged COM activation on different Windows builds. Ordinary .lnk COM read/write and real helper execution were exercised. No real profile was activated, no pins changed, no Explorer restart occurred, and the input desktop was not switched. All probe/helper processes exited and fixture ACL restrictions were restored.

Earlier test-development failures exposed Windows link-authoring flattening/native rejection of synthetic chains; final assertions distinguish ordinary successful links from unsupported native chains. Full logs: `verification/Milestone3aResults.txt`.

## Build and artifacts

- Remote source: `C:\Sandbox\Codex\Workspaces\taskbar-groups`.
- Intermediates: `C:\Sandbox\Codex\Builds\taskbar-groups\Milestone3a`.
- Remote output: `C:\Sandbox\Codex\Artifacts\taskbar-groups\Milestone3a`.
- Remote log: `C:\Sandbox\Codex\Logs\taskbar-groups\Milestone3a.log`; local `out/Milestone3a.log`.
- Clean runtime package: `out/TaskbarGroups-Milestone3a.zip`, excluding probe executables and fixtures.
- EXE SHA256: `7CA4ED85BA2849864A4B567F557C8A3397BB04249E7650107AC89B31AC851D11`.

Use the MSBuild command in VERIFICATION.md with these output/intermediate paths. Compile LaunchProbe.cs using the Framework64 csc command in MILESTONE1.md, replacing StorageProbe with LaunchProbe. Run each suite in a fresh disposable directory with a hidden parent process and bounded wait; the harness creates and verifies its private desktop. Final fixtures: `out/Milestone3a-Final-Launch`, `out/Milestone3a-Organizer`, `out/Milestone3a-Persistence`, `out/Milestone3a-Storage`.

## Next work

Finish milestone 3's icon-kind handling/cache regeneration and ownership, saturated-color/layout validation, editor/import error boundaries, the remaining editor MessageBox, release-link/Explorer helper error boundaries, and asynchronous release lookup. Then implement the requested organizer surface and gestures using the tested model. Whole-codebase analyzer and real Windows taskbar/DPI compatibility work remain separate checks. This build is not a daily-use readiness certification.
