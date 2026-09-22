# Baseline verification

This report describes the original unchanged build. For the latest 143-check result and artifact, see [MILESTONE2B.md](MILESTONE2B.md). For the current milestone 1 implementation, artifacts, and 29 storage checks, see [MILESTONE1.md](MILESTONE1.md). `verification/Probe.cs` must only be run against the historical baseline ZIP; use `verification/StorageProbe.cs` for current code.

Date: 2026-09-21. Repository: https://github.com/gmoddev/taskbar-groups. Commit: `edbd4d9116f10597e9c16934df783be9728a98ab` (`master`).

## Result

**Release compilation passed, basic runtime smoke passed, and six targeted fault observations were reproduced. This is not full interactive taskbar certification.** No application source changes were needed to compile. The eight proposed hardening priorities and additional findings are documented in `KNOWN_ISSUES.md`; implementation remains planned.

## Build evidence

- Worker: SSH alias `dockerbox`, hostname `HostPC`; Windows 11 Pro build 26200; 24 logical processors and about 32 GiB memory.
- Toolchain: Visual Studio 2022 Build Tools 17.14; MSBuild `17.14.51+25f168cee`.
- Project: legacy C# WinForms, .NET Framework 4.7.2, Release/AnyCPU, original architecture preferences.
- Command result: exit 0; **0 warnings, 0 errors**, MSBuild elapsed **4.56 seconds**.
- Two MSBuild workers (`/m:2`); no sustained local compilation.
- Source: `C:\Sandbox\Codex\Workspaces\taskbar-groups`.
- Intermediate directory: `C:\Sandbox\Codex\Builds\taskbar-groups\Release`.
- Output: `C:\Sandbox\Codex\Artifacts\taskbar-groups`.
- Log: `C:\Sandbox\Codex\Logs\taskbar-groups\Release.log`; copied locally to `out/Release.log`.
- Restored packages remain in the remote checkout's `packages/` for reuse. No caches were purged or toolchains installed.

Run in a PowerShell session on the worker after cloning the repository:

```powershell
Set-Location C:\Sandbox\Codex\Workspaces\taskbar-groups
& 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe' `
    TaskbarGroups.sln /restore /p:RestorePackagesConfig=true /m:2 `
    /p:Configuration=Release `
    /p:OutDir=C:\Sandbox\Codex\Artifacts\taskbar-groups\ `
    /p:BaseIntermediateOutputPath=C:\Sandbox\Codex\Builds\taskbar-groups\Release\ `
    /fl /flp:logfile=C:\Sandbox\Codex\Logs\taskbar-groups\Release.log
```

Create the task-owned parent output/log directories first. Use full MSBuild because the legacy project has COM references. NuGet restore requires network access on the first build.

## Local artifacts

- `out/TaskbarGroups-Baseline.zip`: clean build output, no test configuration or hardening changes.
- `out/Release/TaskbarGroups.exe`: clean extracted application, alongside its dependencies.
- `out/VerifiedSmokeFinal/Verification.txt`: final private-desktop test log.
- `verification/Probe.cs`: reproducible test harness, separate from the application project.

Keep the three dependency DLLs, EXE config, and `Windows.winmd` with the executable; PDB is included for debugging. The EXE SHA-256 is:

```text
678A6F5EC3632BC919A1D491E76153A872902AF5C65E7108E1559673E3D77D25
```

Do not use the smoke fixture folders as real group stores: the probes intentionally leave corrupted/test data. The clean ZIP and `out/Release` contain no such fixtures.

## Runtime method and observations

The harness was compiled on the worker against the unchanged application assembly. It creates a private desktop, launches a child explicitly on that desktop, validates the desktop name, and does not switch the input desktop. Application child processes are bounded and closed; no persistent task-owned processes remain. A helper executable writes a marker instead of opening an external program. It never pins anything, invokes `runas`, or manipulates Explorer.

Initial worker-session smoke attempts could not observe the application windows and timed out. These were not counted as runtime success. The final UI checks ran locally on Windows 11 Pro 25H2 **26200.9445**, entirely on a private desktop, using the worker-built binaries. Early harness desktop/exit-code bookkeeping errors were corrected before the passing run. Remote and local results are not conflated.

| Check | Result and boundary |
| --- | --- |
| Group XML save/load and image generation | Passed with a disposable group |
| Generated `.lnk` file | File creation passed; normal CreateConfig is called in the harness process, so its FriendlyName-based target is the harness; actual pinned-link activation was not certified |
| Manager executable startup | A visible WinForms window was observed on the private desktop and closed with exit 0 |
| Group popup executable startup | `TaskbarGroups.exe Smoke_Group` displayed a window, tolerated a missing ordinary target, and closed with exit 0 |
| Launch path | `frmMain.OpenFile` launched a harmless helper with quoted marker-path argument and the requested working directory |
| KI-04 cache loop | Nonempty group rebuild erased a sentinel cache and produced zero cache files |
| KI-08 malformed XML | Direct category load threw `InvalidOperationException`, outside the manager's IOException-only boundary |
| KI-08 missing group | Popup construction threw before graceful missing-group handling |
| KI-05 failed serialization | Invalid-surrogate fault replaced/truncated previous valid configuration before throwing |
| KI-01 different working directory | Valid group image could no longer be loaded through the relative path |
| KI-03 default working directory | A new shortcut defaulted to the application executable filename |

Final harness result: **13 checks passed, 0 unexpected failures.** Lines beginning `PASS KNOWN` confirm baseline defects; they do not represent repaired behavior.

## Repeating the harness

Use a **fresh disposable directory** for every run. Copy every runtime file from the clean output into it; do not copy existing config/shortcut/JIT directories. Compile the harness on the worker, substituting a task-owned fresh path for `$Fixture`:

```powershell
$Fixture = 'C:\Sandbox\Codex\Artifacts\taskbar-groups\FreshProbe'
New-Item -ItemType Directory -Path $Fixture | Out-Null
Get-ChildItem 'C:\Sandbox\Codex\Artifacts\taskbar-groups' -File |
    Where-Object Extension -in '.exe', '.dll', '.config', '.winmd', '.pdb' |
    Copy-Item -Destination $Fixture
& C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /platform:x64 `
    /out:$Fixture\Probe.exe /r:$Fixture\TaskbarGroups.exe `
    /r:System.Drawing.dll /r:System.Windows.Forms.dll .\verification\Probe.cs
if ($LASTEXITCODE -ne 0) { throw 'Probe compilation failed' }
$ProbeProcess = Start-Process "$Fixture\Probe.exe" -WindowStyle Hidden -PassThru
if (!$ProbeProcess.WaitForExit(90000)) { $ProbeProcess.Kill(); throw 'Probe timed out' }
Get-Content "$Fixture\Verification.txt"
```

For private-desktop UI verification, transfer the compiled fixture without test data to the local machine and run the final three lines against its new path. The noninteractive SSH worker desktop did not provide the same UI observability. The helper has its own private-desktop launch logic; do not directly start the baseline application on the user's active desktop as a fallback.

## Not verified

- Actual pin/unpin, AppUserModelID grouping in Explorer, or persistence of existing pins after rename/update.
- UWP, URI, directory, and real user `.lnk` targets; elevation denial or protected installation behavior at runtime.
- Mixed DPI, fractional scaling, multiple monitors, auto-hide, taskbar-less secondary monitors, and Windows 10.
- Entire editor interaction sequence, concurrent edits, recovery after process termination/power loss, prolonged GDI/COM leak rates, or clean-VM deployment.
- A dedicated whole-codebase analyzer/security scan. Source review, compiler diagnostics, targeted probes, and selected upstream diff reviews were performed; those do not substitute for such a scan.

These limitations are mapped to roadmap acceptance criteria. The prior incorrect `Taskbar-Organizer` checkout was left untouched after correction; its successful native build and C++ analyzer output are unrelated to this application's verification.
