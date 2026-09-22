# Native group pinning feasibility

Historical read-only investigation, 2026-09-21. [Milestone 4c](MILESTONE4C.md) subsequently implements a native request preview and adds production-adapter read-only checks. Actual Windows confirmation and group pin creation remain unverified. The observations below describe the original standalone probe.

## Observed on this machine

A standalone .NET Framework probe ran on a private desktop under a random explicit group-shaped AppUserModelID. It compiled without diagnostics and exited 0 with four passing checks.

| Observation | Result |
| --- | --- |
| Windows build | 26200.9445 |
| Documented registry predicate requires a LAF token | False |
| Desktop support marker | Present, HRESULT 0 |
| TaskbarManager.IsSupported | True |
| IsPinningAllowed in this isolated, non-foreground process | False |
| IsCurrentAppPinnedAsync for its unique unregistered identity | Completed; False |
| Explicit process AppUserModelID readback | Exact match |

These results establish native ABI access from the existing framework without porting the application or injecting into Explorer. Eligibility false is a contextual observation, not proof that foreground group pinning is unsupported. The unique identity had no Start-menu registration; pinned-state false does not prove that Windows can resolve an actual group entry.

The probe declares no pin-request methods, writes no Start-menu links or registry values, launches no targets, and never switches desktops. Its only shell identity change belongs to the disposable child process. Native interfaces, HSTRING and returned identity memory are released. Async state polling has a five-second limit; the parent limits child lifetime to twenty seconds. Unexpected errors are written to the fixture log.

## Source findings

Microsoft's [unpackaged desktop sample](https://github.com/microsoft/Windows-classic-samples/blob/main/Samples/TaskbarManager/CppUnpackagedDesktopTaskbarPin/MainWindow.xaml.cpp) registers a Programs shortcut whose AppUserModelID matches the process, then queries and requests current-app pinning. This supports investigating our existing one-process-per-group design; it does not establish how Windows will distinguish multiple argument-bearing links targeting one executable.

The [current pinning guidance](https://learn.microsoft.com/en-us/windows/apps/develop/windows-integration/pin-to-taskbar) removes the limited-access restriction on newer Windows builds and supplies a registry predicate. It also requires a foreground app, Start-menu entry and explicit user interaction. The sample still contains older token gating, so do not copy that gate unconditionally.

Interface GUIDs and method order were checked against Microsoft's [Windows.UI.Shell bindings](https://github.com/microsoft/windows-rs/blob/master/crates/libs/windows/src/Windows/UI/Shell/mod.rs). The probe calls only GetDefault, the two Boolean properties and the asynchronous pinned-state query. It does not introduce a Windows SDK or runtime dependency into the application.

## Next implementation and acceptance

1. Use an explicit group-publishing action to prepare a matching per-user Programs link. Keep a distinct GUID filename, persisted AppIdKey, actual executable path and ordinary group argument. Preserve existing legacy identities. Validate committed state and reject stale/hidden groups, as GroupPublishing already does.
2. Separate registration preparation from OS pin approval. Ordinary organizer grouping should remain immediate; a failed or cancelled pin request must leave the group and undo history usable. Manage only entries owned by this application and never replace an unrelated Start-menu link.
3. Run any native request in a dedicated group-identity process/window. Set its AppUserModelID before creating windows or TaskbarManager. The organizer process carries the manager identity, so requesting there could pin the wrong app. The existing popup closes on deactivation; it is unsuitable for hosting an outstanding confirmation without a deliberate lifetime change.
4. Check runtime desktop support, LAF requirements, eligibility and current pinned state. Offer a request only after the user interacts with foreground group UI. Keep the guided fallback for unsupported or unavailable cases. Show refusal/cancellation inline, with no retry loop or automatic confirmation.
5. Verify in a disposable interactive Windows environment: two groups using the same EXE must receive distinct pins, preserve exact launch arguments, and retain identity through rename, membership edits, restart and dissolution. Verify denial, cancellation, unavailable notifications and policy. Test stale publishing requests and owned Start-menu entry repair separately.

Do not label native per-group pinning complete until that matrix passes. It provides no evidence for intercepting icon-on-icon dragging on Explorer's actual taskbar.

## Reproduce the read-only probe

Run from the repository in PowerShell; no application or user-profile fixture is required:

```powershell
$Output = Join-Path $PWD 'out\NativePin-Feasibility'
New-Item -ItemType Directory -Force -Path $Output | Out-Null
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:winexe /platform:x64 /r:System.Windows.Forms.dll /r:System.Drawing.dll "/out:$Output\NativePinProbe.exe" (Join-Path $PWD 'verification\NativePinProbe.cs')
if ($LASTEXITCODE -ne 0) { throw 'Probe compilation failed.' }
$Probe = Start-Process "$Output\NativePinProbe.exe" -WindowStyle Hidden -PassThru
if (-not $Probe.WaitForExit(30000)) {
    Stop-Process -Id $Probe.Id
    throw 'Probe parent timed out.'
}
$Probe.Refresh()
Get-Content "$Output\Verification.txt"
if ($Probe.ExitCode -ne 0) { throw "Probe failed: $($Probe.ExitCode)" }
```

Results: verification/NativePinResults.txt. A PASS denotes a probe assertion, while OBSERVE records machine/context-specific capability values. Unsupported desktop APIs or token-required machines may skip queries; a zero exit alone does not establish feature availability. Registry read errors or unexpected types fail rather than granting eligibility.

Application source and the milestone 4b package were unchanged during this historical investigation. Its prior 277 checks were not rerun; the four new probe checks are reported separately. No commit or push was performed.
