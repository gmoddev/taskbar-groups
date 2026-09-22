# Native group pin request preview

Milestone 4c implemented 2026-09-21. Select a group, choose **Pin group…**, then **Windows pin request (preview)…**. The dedicated group window offers **Request pin**, **Show shortcut** and **Close**.

Request pin prepares a per-user Start-menu shortcut and checks Windows eligibility before requesting confirmation. Refusal, unavailable APIs, errors and cancellation appear inline. The ordinary manual shortcut flow remains available. The feature is a preview: actual Windows confirmation and distinct per-group pin creation have not been exercised.

## Identity and lifecycle

The organizer starts the same executable with `--pin-group <group-id> <organizer-revision>` through LaunchService. Both identifiers are validated; stale and hidden groups are rejected. The child sets the persisted group AppUserModelID before constructing the pin window or native adapter. Normal generated links still use the ordinary group argument and open the popup, never this request window.

The dedicated window survives deactivation while Windows handles confirmation. It creates no native session or Start-menu entry merely by opening. Only Request pin performs registration and calls the adapter. The request button disables while work is outstanding; closing cancels the operation and defers form disposal until it unwinds. Cancellation does not unpin anything already approved.

The native adapter verifies its process identity, checks the desktop marker and current LAF registry predicate, and queries support, eligibility and pinned state. It requires its owning STA thread and the requesting window to be foreground immediately before an OS request; it never steals focus. An already-pinned result suppresses another request. Pinned is reported only from a positive Windows query/request result.

Read-only queries are bounded to five seconds; requests to two minutes. Closing or timeout attempts to cancel the outstanding WinRT operation. Windows may already have completed an approved pin, so cancellation/timeout must not be interpreted as proof of an unchanged taskbar. Native COM and HSTRING resources are released on the UI thread. No hooks, injection, pin verbs, taskbar database edits or LAF bypass are used.

See Microsoft's [desktop pinning guidance](https://learn.microsoft.com/en-us/windows/apps/develop/windows-integration/pin-to-taskbar) and the prior [read-only investigation](NATIVE_PIN_FEASIBILITY.md).

## Start-menu publication

The entry is `<per-user Programs known folder>/TaskbarGroups/<group-id>.lnk`. This user-requested shell registration is separate from the main writable store in LocalAppData. Its exact bytes come from the group's committed Launcher.lnk, preserving the executable, ordinary popup argument, icon and persisted AppUserModelID.

Registration uses the existing writer lock and validates the organizer revision, visible group and generated link. It rejects linked directories/files, stages and flushes a same-directory temporary file, atomically moves/replaces it and verifies the result. A matching entry is idempotent. Replacement requires matching group AppUserModelID, argument and current executable path; an unrelated entry is preserved. Temporary files are removed after expected failures.

A refusal or cancellation retains the Start-menu entry for later use. Group data and undo history are unaffected. Registration is not removed automatically on dissolution/deletion, and later edits do not yet refresh its display metadata automatically. Ordinary popup lookup still uses stable identity. An entry pointing at an older executable location is conservatively refused rather than silently taken over.

## Verification

Release compilation on dockerbox / HostPC, MSBuild 17.14, /m:2: **0 warnings, 0 errors**. The worker reported 24 logical CPUs and approximately 14 GiB free memory before compilation; existing caches and intermediate state were retained.

| Private-desktop suite | Passed |
| --- | ---: |
| SurfaceProbe | 64 |
| UiProbe | 45 |
| LaunchProbe | 47 |
| OrganizerProbe | 69 |
| PersistenceProbe | 45 |
| StorageProbe | 29 |
| NativePinProbe | 7 |
| Total | **306** |

All suites exited 0 against the final executable. Surface additions verify exact child command routing, revision rejection, ownership-safe and idempotent registration, denied replacement cleanup, exact repair, no request on opening, group identity passed to the adapter, approval/refusal, already-pinned/unsupported/denied/error states, reentrancy and cancellation during both eligibility checks and a simulated confirmation. Organizer state remains unchanged.

UI request outcomes use IPinClient fakes and a disposable Programs directory. **No automated test invokes a native RequestPin API.** NativePinProbe exercises the actual production adapter only for read-only queries, identity mismatch rejection and pre-cancellation. On Windows 26200.9445, support was true, the documented LAF predicate required no token, and private non-foreground eligibility/pinned state were false. The actual request slot remains interactively unverified.

The two screenshots, out/Milestone4cFinal-Surface/PinWindow.png and Organizer.png, were visually inspected. No input desktop was switched, real profile registered, real pins changed or arbitrary target launched. Only the existing LaunchProbe disposable target helper executed. All probes exited.

## Remaining acceptance

Use a disposable interactive Windows environment to verify native confirmation, Start-menu indexing, Windows display names, two group identities targeting one EXE, exact popup arguments, denial/cancellation, notifications/policy, rename/dissolution, restart and pin persistence. GUID filenames may affect shell display names; this has not been validated. Physical drag/drop and the broader monitor/DPI matrix remain open.

Start-menu lifecycle cleanup and refresh must preserve entries still used by pins and must never overwrite unrelated entries. Real taskbar icon-on-icon grouping remains a separate feasibility question. This milestone changes neither existing Windows app pins nor Explorer ordering.

## Artifacts and reproduction

- Remote source: C:\Sandbox\Codex\Workspaces\taskbar-groups.
- Build state: C:\Sandbox\Codex\Builds\taskbar-groups\Milestone4c.
- Release output: C:\Sandbox\Codex\Artifacts\taskbar-groups\Milestone4c.
- Build log: C:\Sandbox\Codex\Logs\taskbar-groups\Milestone4c.log; local out/Milestone4c.log.
- Local runtime: out/Milestone4c-Build; clean package: out/TaskbarGroups-Milestone4c.zip.
- EXE SHA256: 994AD543BE765F468A7950EE1C0AA1FF83BA6500BE8BEC139D3D2A1C690807D1.
- Results: verification/Milestone4cResults.txt.
- Fixtures: out/Milestone4cFinal-{Surface,Ui,Launch,Organizer,Persistence,Storage,NativePin}.

Build using VERIFICATION.md with the milestone paths above. Compile the seven probes with the Framework64 csc pattern in MILESTONE1.md, referencing the final TaskbarGroups.exe, System.Drawing.dll and System.Windows.Forms.dll. Copy the runtime into fresh fixtures, start each probe hidden with no arguments, and inspect exit code plus Verification.txt. NativePinProbe detects the adjacent application and adds three production-adapter checks to its four standalone checks. Keep actual native request APIs out of automation.

Nothing has been committed or pushed.
