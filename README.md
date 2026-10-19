# ULTIMATE PC OPTIMIZER PRO

A recovery-first Windows 10/11 gaming optimizer, hardware scanner, tweak manager and debloater (C# / WPF / .NET 8 / MVVM).

Every value it shows is read from the live system, and every change it makes:

- goes through one transactional engine
- is backed up first
- is protected by a **verified** System Restore point
- is verified after it is applied
- can be rolled back

Tweaks that are undocumented, outdated, or that don't do what the internet claims are not included.

---

## 1. What is implemented

| Area | Status |
|---|---|
| Hardware scanner: CPU (incl. P/E-core topology), GPU (DXGI + WDDM kernel stats), RAM modules, disks/volumes/TRIM, board/firmware/Secure Boot, displays + supported refresh rates, network + drivers, OS/security/gaming state, relevant software | ✅ |
| Live monitor: CPU %, effective clock, **performance limit (throttling)**, RAM/commit, disk, network, GPU util/VRAM/**temperature**/clock | ✅ |
| Weighted, explainable tier classification (low/mid/high, laptop-aware, user override, what-if resolution/FPS) + bottleneck analysis | ✅ |
| Restore points: unique timestamped names, creation **verified by enumeration**, throttle/disabled detection, blocking policy, manual creation, dashboard | ✅ |
| Transactional engine: preflight → approval → backup → restore point → apply → verify → auto-rollback → audit | ✅ |
| 13 change handlers: registry value/token/flags, AppCompat layer, power plan, power setting, service start type, scheduled task, startup entry, Appx removal, display mode, device power, optional feature | ✅ |
| Tweak database: **91 documented tweaks** (70 registry-backed) across 13 categories, 5 profiles | ✅ |
| Debloater: real Appx scan (58 curated packages, protected list), presets, scheduled tasks, logon tasks, optional features, background processes, temp/shader-cache cleanup, Recycle Bin | ✅ |
| Services manager: all services, categorised (38-entry knowledge base), dependencies, locked essentials | ✅ |
| Startup manager (Task Manager-compatible `StartupApproved` mechanism) | ✅ |
| Network: ping/jitter/loss monitor, before/after history, raw-UDP DNS resolver benchmark, traceroute, bufferbloat grade | ✅ |
| Game profiles: Steam/Epic/Riot/uninstall-registry detection for 8 profiles + custom games; per-game GPU preference / FSO / CPU priority; launch | ✅ |
| Benchmark Center: PresentMon capture, CSV import (PresentMon/CapFrameX/OCAT), avg / 1% / 0.1% lows, frame-time graph, telemetry, comparison, HTML report | ✅ (see limits) |
| 22-page dark UI, dialogs, busy overlay, dark native title bar | ✅ |
| Self-contained single-file build, portable ZIP, Inno Setup script | ✅ (installer must be compiled with Inno Setup) |

## 2. Verification performed

| Check | Result |
|---|---|
| `dotnet build` (whole solution) | 0 errors, 0 warnings |
| Automated tests (xUnit, `tests/`) | **55/55 pass** |
| Live, read-only hardware scan on a Ryzen 7 7700 / RTX 5070 / Windows 11 25H2 PC | Real values detected; GPU temperature read via the WDDM kernel interface |
| Live tweak-status evaluation of all tweaks on the same PC | No errors |
| App launched and all 22 pages navigated via UI Automation, with a screenshot of each page | Logged no warnings or errors |
| Self-contained `UltimatePCOptimizerPro.exe` (75 MB) launched without any .NET runtime configured | Starts in ~1.6 s |

The tests cover the engine against an isolated `HKCU\Software\UltimatePCOptimizerPro.Tests` key with a fake restore-point provider:

- apply, then rollback
- blocking when the restore point is throttled
- the override path requires both the policy setting and explicit consent
- a failure mid-batch rolls the whole batch back
- skip when already applied
- exact type/data restore
- created keys are removed on restore
- flag bits are preserved
- token edits leave other tokens intact

The tests also check catalog/safety rules and the statistics/DNS code.

**Not yet exercised on real hardware:** an elevated run that creates a real restore point and writes HKLM / services / power plans. The handlers compile and their logic is unit-tested on HKCU. Do the first elevated run on a test machine or VM.

PresentMon capture is implemented against PresentMon's documented CLI but was not run (PresentMon is not installed here). CSV import and the statistics are tested.

## 3. Architecture

```
UltimatePCOptimizerPro.sln
├─ src/UltimatePCOptimizerPro.Core     (net8.0-windows class library — no UI)
│  ├─ Models/            hardware, tweak, change-spec, operation, benchmark models
│  ├─ Interop/           Win32 P/Invoke, DXGI COM, D3DKMT, WMI helper, process runner
│  ├─ Engine/            OptimizationEngine, IChangeHandler, approval contract
│  │  └─ Handlers/       13 change handlers (capture / apply / verify / restore)
│  ├─ Services/          detection, monitoring, tiering, restore points, backups, audit,
│  │                     catalog, recommendations, debloat, services, startup, cleanup,
│  │                     network, benchmark, games, safety policy
│  ├─ Data/Tweaks/*.json tweak database (one file per category)
│  ├─ Data/Debloat, Data/Services, Data/Games.json   curated knowledge
│  └─ Profiles/*.json    Safe, LowEnd, Balanced, HighEnd, Extreme
├─ src/UltimatePCOptimizerPro          (WPF app, MVVM with CommunityToolkit.Mvvm + DI)
│  ├─ Views/, ViewModels/, Dialogs/, Controls/ (SparklineChart), Converters/, Services/ (UI)
│  └─ Resources/Themes, Resources/Styles, Resources/Icons
├─ tests/UltimatePCOptimizerPro.Tests  (xUnit)
├─ installer/UltimatePCOptimizerPro.iss
└─ build.ps1
```

**Deviation from the original folder plan, and why:** the requested single-project layout was split into **Core + WPF + Tests**. The safety-critical engine, handlers and catalog then have no UI dependency and can be unit-tested headlessly. The spec's `TweakDefinitions.json` became one JSON file per category. Per-tweak compatibility rules live in each tweak's `conditions` block (build range, desktop/laptop, dedicated GPU, SSD, hybrid CPU, software installed) instead of a separate `CompatibilityRules.json`. The safety allow/deny lists are compiled into `SafetyPolicy.cs` **on purpose**, so an edited data file cannot turn off protections.

### The change pipeline (`OptimizationEngine`)

1. **Preflight**: each handler reads the current state and reports support, admin need, and whether the change is already applied.
2. **Approval**: the exact plan is shown (target, current → new value, risk, restart, rollback quality). High/Expert risk requires a tick-box acknowledgement.
3. **Backup**: originals are captured and written to `%LOCALAPPDATA%\UltimatePCOptimizerPro\Operations\<timestamp>_<id>\operation.json`, plus `.reg` exports of the touched keys.
4. **Restore point**: `ULTIMATE-PC-OPTIMIZER | <Operation> | yyyy-MM-dd | HH-mm-ss` (local time). It is created via WMI `SystemRestore.CreateRestorePoint`, then **verified by enumerating restore points**. Windows returns success even when it silently skips creation because of its 24-hour frequency limit, so the return code alone is not trusted.
5. **Policy**: with no verified point the operation is **blocked** (default). An advanced, opt-in setting allows a per-operation override, offered only when *every* change has a verified app-level rollback, and the user must tick an acknowledgement. Appx removal and DISM feature changes never qualify.
6. **Apply + verify** each change; status is saved after every step (crash-safe).
7. **Auto-rollback**: if anything fails, the whole batch is restored in reverse order and verified.
8. **Audit**: one JSON line per operation in `history.jsonl`.

Rollback operations attempt a restore point but are never blocked by throttling, because they return the system to a recorded state.

### Safety rules (enforced in code)

The app never touches:

- Defender, Security Center, the firewall
- Windows Update / servicing
- Secure Boot, LSA, Device Guard / HVCI, code integrity
- Winlogon shell/userinit
- IFEO `Debugger` (only the `PerfOptions` sub-key is writable)
- Spectre/Meltdown overrides
- ~90 protected services
- store/framework/codec/driver-panel packages
- virtualization/.NET features
- update/security/maintenance scheduled tasks

It never forces Realtime priority, disables CPU cores, edits driver registry values, injects into games, or modifies game files or anti-cheat. Unknown services are treated as locked, never as safe.

## 4. Tweak database

91 tweaks:

| Category | Count |
|---|---|
| Gaming | 8 |
| GPU | 2 |
| CPU | 4 |
| Power | 4 |
| Memory | 5 |
| Network | 4 |
| Input | 5 |
| Explorer/UI | 17 |
| Privacy | 20 |
| Startup/System | 5 |
| Services | 6 |
| Scheduled tasks | 10 |
| Recovery | 1 |

By risk: 76 low, 10 medium, 5 expert.

Each tweak records:

- description and the actual mechanism
- registry/target location
- the live current value and the proposed value
- benefit and drawback
- recommended-for, risk, and restart requirement (none / app / Explorer / sign-out / restart)
- compatibility conditions and suitable tiers
- evidence notes

The 100+ target in the brief was deliberately not padded. These popular tweaks are **excluded on purpose**, with the reason:

| Excluded tweak | Reason |
|---|---|
| `TCPNoDelay` (Tcpip registry) | Not honoured for Winsock apps; apps set `TCP_NODELAY` themselves |
| `DisablePagingExecutive`, `LargeSystemCache`, `ClearPageFileAtShutdown` | Outdated or harmful on modern memory management |
| GameDVR `FSEBehaviorMode` family | Undocumented, inconsistent effects |
| `Win32PrioritySeparation` "magic" values | Only the documented `0x26` (Programs) is offered |
| Disabling HVCI/VBS, Defender, Spectre mitigations | Security protections |
| Standby-list clearing / "RAM boosters" | Counter-productive |

`NetworkThrottlingIndex` and `TcpAckFrequency` are available as **Expert** entries with their limits documented. They are excluded from all profiles.

Profiles: **Safe** (low risk, no feature loss) · **Low-End FPS Boost** · **Balanced Gaming** · **High-End Competitive** (max risk Medium) · **Extreme Advanced** (explicit opt-in). Smart Optimize only includes items that fit the profile's risk ceiling, apply to this PC (build, form factor, GPU, SSD…), suit the detected tier, and are not already applied. Every exclusion is listed with its reason.

## 5. Build, test, package

Requirements: Windows 10 1809+ / 11, .NET 8 SDK.

```powershell
dotnet build UltimatePCOptimizerPro.sln -c Release
dotnet test tests\UltimatePCOptimizerPro.Tests -c Release
.\build.ps1            # build + test + self-contained single-file publish + portable ZIP (+ installer if Inno Setup 6 is installed)
```

Outputs:

- `publish\win-x64\UltimatePCOptimizerPro.exe` — self-contained, no runtime needed
- `publish\UltimatePCOptimizerPro-1.0.0-win-x64-portable.zip`
- `publish\installer\UltimatePCOptimizerPro-Setup-1.0.0.exe` — only built when Inno Setup 6 is installed

The app starts **without** elevation, and scanning and diagnostics never need it. Applying changes requires administrator rights because a restore point must be created first. Use **Restart as administrator** in the sidebar.

### Manual test checklist (use a VM or test PC for the elevated steps)

1. Launch → Dashboard shows live CPU/GPU/RAM/disk/network and tier; My PC Specs matches Task Manager / Device Manager.
2. Run as admin → Restore Points shows System Protection status and lists restore points.
3. Registry Tweaks → toggle "Hide Game Bar startup tips" on. Check the plan dialog, then that `ULTIMATE-PC-OPTIMIZER | Registry Tweaks | <date> | <time>` appears in `rstrui.exe` and the switch turns on. Toggle it off: the value is removed.
4. Apply a second tweak within 24 h (default frequency) → expect "Operation blocked — no verified restore point" (or enable the Recovery tweak / backup-only override in Settings).
5. Backup & Recovery → restore an individual change, a whole operation, and "Undo last optimization".
6. FPS Booster → with a monitor set below its max refresh rate, "Set to maximum", then let the 15 s confirmation time out → the mode is reverted.
7. Benchmark Center → select PresentMon, start a game, capture 30 s, capture again, Compare, Export report.

## 6. Known limitations / unsupported (stated, not faked)

| Item | Status |
|---|---|
| CPU temperature | Not shown. Windows has no standard API; vendor kernel drivers would be required. |
| GPU temperature / clock | Use the WDDM kernel interface (as Task Manager does) or `nvidia-smi`; shown as unavailable when the driver doesn't report them. |
| VRR (G-SYNC/FreeSync) state, mouse polling rate, SMART wear (non-admin), memory channel mode | Not exposed by public Windows APIs. Shown as unavailable, or as a labelled estimate (channel mode). |
| Startup "impact" | Task Manager's score is not available to third-party apps. Current CPU/RAM of running startup apps is shown instead. |
| FPS numbers | Require Intel PresentMon (free, open source) or an imported CSV. The app never estimates FPS. |
| Per-process network bandwidth | Not available without ETW kernel tracing; links to Resource Monitor. |
| Appx removal | Current user only. Rollback re-registers if Windows still has the files, otherwise reinstall from the Store. Always requires a verified restore point. |
| Optional features | DISM changes complete after a restart. |
| Service changes | Persist immediately; a running service keeps running until restart. |
| Restore point frequency | Windows allows one per 24 h by default. The app never changes this silently; an explicit tweak exists. |
| Wi-Fi details | Parsed from `netsh` (best effort on non-English Windows). |

## 7. Data locations

`%LOCALAPPDATA%\UltimatePCOptimizerPro\` contains:

- `settings.json`
- `history.jsonl` (audit)
- `Operations\` (backups + .reg exports)
- `Logs\` (JSON-lines)
- `Benchmarks\`
- `Profiles\` (custom tweak profiles)
- `games.json`
- `network-history.json`

See [docs/USER_GUIDE.md](docs/USER_GUIDE.md) for usage and recovery instructions.
