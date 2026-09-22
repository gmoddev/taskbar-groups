# Build and verification

Latest application result: **393 passing checks**, zero unexpected failures; Release has zero warnings/errors. The documentation/artifact cleanup does not alter application code and does not require recompilation. Retained detailed output: [verification/Results.txt](verification/Results.txt).

| Suite | Passed |
| --- | ---: |
| SurfaceProbe | 128 |
| UiProbe | 68 |
| LaunchProbe | 47 |
| OrganizerProbe | 69 |
| PersistenceProbe | 45 |
| StorageProbe | 29 |
| NativePinProbe | 7 |

Tests run with fresh disposable profiles on private desktops without switching the input desktop. Only a disposable launch helper runs. Native pin requests are faked; real capability queries are read-only. Separate checks confirmed Brave import and Codex artwork. These results do not certify physical drag/drop, real native pinning, all activation kinds or the full DPI/Windows matrix.

## Build

Use the remote-build-worker skill and SSH host dockerbox. The legacy .NET Framework 4.7.2 WinForms project requires full Visual Studio MSBuild and its COM tools. Keep Prefer32Bit=false in Release and Debug. Inspect worker capacity first, use /m:2 and retain caches.

On the worker, create project-owned output/log directories and run:

```powershell
Set-Location C:\Sandbox\Codex\Workspaces\taskbar-groups
& 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe' TaskbarGroups.sln /restore /p:RestorePackagesConfig=true /m:2 /p:Configuration=Release /p:OutDir=C:\Sandbox\Codex\Artifacts\taskbar-groups\Current\ /p:BaseIntermediateOutputPath=C:\Sandbox\Codex\Builds\taskbar-groups\Current\ /fl /flp:logfile=C:\Sandbox\Codex\Logs\taskbar-groups\Current.log
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
```

Copy the EXE, EXE.config, three dependency DLLs and Windows.winmd together. PDB is optional. The published executable SHA256 is `7208258231EEFEEE37D25A6AEBE16837FB6026ABA0B24300690724E638ADB6B8`.

## Run the suites

Use a fresh fixture directory for each suite. Copy only clean runtime files into it; never reuse prior test state. Compile one lightweight harness as follows, substituting its name and absolute paths:

```powershell
$Name = 'Surface'
$Fixture = Join-Path $PWD "out\Fresh-$Name"
New-Item -ItemType Directory $Fixture | Out-Null
Copy-Item out\Release\* $Fixture
$Source = Join-Path $PWD "verification\${Name}Probe.cs"
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:winexe /platform:x64 /r:System.Windows.Forms.dll /r:System.Drawing.dll "/r:$Fixture\TaskbarGroups.exe" "/out:$Fixture\${Name}Probe.exe" $Source
if ($LASTEXITCODE -ne 0) { throw 'Harness compile failed' }
$Probe = Start-Process "$Fixture\${Name}Probe.exe" -WindowStyle Hidden -PassThru
if (-not $Probe.WaitForExit(45000)) { throw "Probe timeout: $($Probe.Id)" }
Get-Content "$Fixture\Verification.txt"
if ($Probe.ExitCode -ne 0) { throw 'Probe failed' }
```

Repeat for Ui, Launch, Organizer, Persistence, Storage and NativePin. Read the assertions and final failure count; capability observations alone do not establish pin eligibility. x64 assembly-loading tests do not independently establish executable startup architecture; SurfaceProbe also checks PE flags.

## Artifacts and history

Local out/ contains Release/, the published TaskbarGroups-Milestone4h.zip and Build.log. The ZIP remains byte-for-byte the published asset; its bundled historical docs are preserved for release integrity. Source documentation now uses HISTORY.md.

Historical milestone reports and logs are retrievable from Git commit `cebee5d`. Old local binaries and disposable fixtures are gathered under out/Archive because automatic approval review blocked deletion. Remote compiler/dependency caches were retained.
