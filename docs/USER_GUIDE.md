# ULTIMATE PC OPTIMIZER PRO — User Guide

## Getting started

1. Start the app. It scans your PC automatically; this is read-only and changes nothing.
2. Check **Dashboard** for:
   - your performance tier
   - live usage
   - detected bottlenecks (for example, a monitor running below its maximum refresh rate, or a single memory module)
3. To change anything, click **Restart as administrator** (bottom-left). Administrator rights are needed because a System Restore point is created before every change.

## The safest way to optimize

1. Open **Smart Optimize**.
2. Pick a profile. The one marked *Suggested for this PC* matches your hardware.
   - **Safe Optimization**: low-risk changes only. A good first step for everyone.
   - **Low-End / Balanced / High-End**: tuned to your hardware tier.
   - **Extreme Advanced**: experimental. You must confirm that you understand this.
3. Click **Scan & generate plan**. Untick anything you don't want. Items that were left out are listed with the reason.
4. Click **Create restore point & apply selected**. A review window shows every change: current value → new value, risk, and whether a restart is needed.
5. When it finishes, a summary shows what changed, the restore point name, and where the backup is stored.
6. Measure: use **Benchmark Center** before and after to see the real effect.

## Individual tweaks

**Registry Tweaks**, **Windows Tweaks** and the module pages (FPS, CPU, GPU, RAM, Network, Input) show tweak cards.

- The switch shows the **real current state** read from Windows.
- **Details** shows what the tweak changes, where, the current and proposed values, benefit, drawback, risk, compatibility and evidence.
- Turning a switch **off** restores the value that existed before this app changed it.
- Use the filters to show only *Recommended*, *Applied*, *Unsupported* or *Favorites*, and **Save as profile** to reuse a selection.

## Restore points

- Names look like `ULTIMATE-PC-OPTIMIZER | Registry Tweaks | 2026-10-19 | 18-45-32` (your PC's local time).
- The app checks that Windows really created the point. If it was skipped, the change is **blocked** by default:
  - **System Protection off** → Restore Points page → *System Protection settings* → select drive C: → *Configure* → *Turn on system protection*.
  - **Windows only allows one restore point per 24 hours (default)** → wait, or enable *Allow restore points more often than once per 24 hours* (Windows Tweaks → Recovery). That change itself also needs a restore point (or the override below).
  - **Advanced**: Settings → Restore point policy → *Allow backup-only override*. You will then be offered, per operation, to continue with the app's own verified backup. This is only offered when every change can be verified when rolled back. An app backup is **not** the same as a restore point.

## Undoing changes

From the easiest option to the most powerful:

1. **Turn the tweak's switch off**, or use *Restore previous value* in its details.
2. **Backup & Recovery**:
   - *Restore selected* changes, or *Restore whole operation*
   - *Undo last optimization*
   - *Restore all original registry values*
3. **Windows System Restore**: Restore Points → *Open Windows System Restore*. Pick the `ULTIMATE-PC-OPTIMIZER | …` point created before the change.
4. **If Windows won't start**: boot to *Advanced startup* (hold Shift while clicking Restart, or interrupt boot three times). Then *Troubleshoot › Advanced options › System Restore*.

Backups are kept in `%LOCALAPPDATA%\UltimatePCOptimizerPro\Operations` even after uninstalling. Each operation folder contains `operation.json` (original values) and `.reg` exports of the registry keys that were changed. Restore anything you want reverted **before** uninstalling.

## Debloater

- **Installed apps**:
  - Presets (*Light / Gaming / Advanced*) only **pre-select** apps for review. Read each note.
  - Protected components (Store, frameworks, codecs, Xbox sign-in, GPU/audio control panels) cannot be removed.
  - Apps are removed for your account only.
- **Scheduled tasks / Optional features**: only items with a documented, non-essential purpose can be changed.
- **Background processes**: shows CPU, RAM and I/O. Processes are only ended after you confirm, and Windows processes cannot be ended.
- **Temporary files**: scan first, then review the file list. Shader caches are not selected by default, because clearing them causes temporary stutter.

## Services and startup

- **Services Optimizer**: essential and unknown services are locked. For optional services, read *Potential side effects* and the dependency lists before changing anything. Changes take effect after a restart.
- **Startup Optimizer**: disabling works exactly like Task Manager. The program stays installed and the original entry is kept.

## Network

- Ping monitor, 100-ping test (saved as Before/After for comparison), DNS benchmark, traceroute, and the bufferbloat test (downloads data for about 10 seconds).
- High latency to your first hop (gateway) is a local problem. Loss or latency only at later hops is on the ISP or server side, and no Windows setting can fix it.

## Benchmarks (real FPS)

1. Download Intel PresentMon (console build) from the link on the page and select it.
2. Start your game and go to a repeatable scene.
3. Enter the game process (e.g. `cs2.exe`) and click **Start capture**.
4. Repeat after changes, then use **Compare**.

Differences under about 3% are normal run-to-run variance.

## Privacy

Everything stays on your PC. The only network traffic is what you start yourself: ping/DNS/traceroute targets, and the bufferbloat download from the URL shown before the test.
