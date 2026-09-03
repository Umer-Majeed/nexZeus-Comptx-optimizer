# NexZeus — Gaming Intelligence Booster  Fast


NexZeus is a Windows performance and optimization tool that manages DNS, registry tweaks, background processes, RAM, startup apps, MSI interrupts, power plans, and an in-game FPS overlay all from a single dashboard.
 
--- ---

## The Setup here (before first launch)

1. **Run as Administrator.** The app requests elevation automatically via `app.manifest`. If the prompt doesn't appear, right-click the `.exe` → **Run as administrator**. Without admin rights, DNS changes, tweaks, debloat actions, MSI optimization, and the overlay will all fail silently.
2. On first launch, a **splash screen** appears, followed by the main window.
3. The app **minimizes to the system tray** — the close button doesn't quit the app, it sends it to the tray. To fully exit, right-click the tray icon → **Exit**.

---

## Top Bar (Hardware Telemetry)

Always visible at the top of the window:
- **CPU / GPU name** — your hardware identifiers
- **CPU %, RAM usage (used/total GB)** — live, updates every 2 seconds
- **Ping** — live ping (ms) to the active game/server

---

## ☁ CLOUD PROFILES ON

Share and browse community DNS + tweak configurations.

| Button | What it does |
|---|---|
| **Find Matches** | Searches for profiles from users with your exact CPU + GPU. If no exact match is found, it falls back to the community's top-rated profiles. |
| **Apply This Profile** | Applies that profile's DNS settings and tweaks to your system (with a confirmation prompt). |
| **👍 (upvote)** | Rates a profile, signaling to the community that it works well. |
| **Share My Current Setup** | Uploads your CPU, GPU, RAM, current DNS servers, and currently-enabled tweaks to the public list. No personal files or identifying information are sent. |

> Requires an internet connection. On failure, the status text shows a reason; a detailed error is logged to `Documents\NexZeus\Logs\`.

---

## ⚡ GAME TWEAKS FAST 

Registry-level tweaks that can each be toggled individually via checkbox. Enabled tweaks are saved to `AutoApplyTweakIds` and persist across app restarts.

| Tweak | What it does |
|---|---|
| Disable Nagle's Algorithm | Sends TCP packets immediately, reducing delay — useful for competitive online games |
| Disable Windows Animations | Turns off UI animations, freeing up minor system resources |
| Disable Transparency Effects | Disables Aero transparency (reduces visual overhead) |
| Disable Network Throttling Index | Removes Windows' multimedia network throttle |
| Disable TCP Timestamps | Removes extra TCP header overhead |
| Boost GPU Priority for Games | Gives games priority in GPU scheduling |
| Boost CPU Priority for Games | Gives games priority in CPU scheduling |
| Lower SystemResponsiveness Reservation | Reduces the CPU % reserved for background tasks, leaving more for the foreground app |
| Disable TCP Delayed ACK | Sends acknowledgment packets immediately (improves ping/latency) |
| Max Out 'Games' MMCSS Task Priority | Sets Windows' multimedia scheduler "Games" class to the highest priority |
| Disable Fullscreen Optimizations (System-wide) | Overrides Windows' fullscreen-borderless mode, improving raw fullscreen input latency |

**How to use:** toggle a checkbox ON to apply it immediately to the registry. Toggle OFF to revert to the original value.

--- --- 

## 🛡 DEBLOAT & PRIVACY SHIELD

Disables background Windows services and telemetry that consume CPU/RAM.

**Categories:**
- **Telemetry** — Set Telemetry to Minimum, Disable DiagTrack (Connected User Experiences), Disable WAP Push Message Routing, Disable Compatibility Appraiser Task, Disable CEIP Consolidator Task
- **Cortana** — Disable Cortana (Policy), Revoke Cortana Search Consent
- **Xbox** — Disable Xbox Live Auth Manager, Xbox Live Game Save, Xbox Live Networking Service, Xbox Accessory Management, Disable Game DVR / Xbox Game Bar Capture
- **BackgroundTasks** — Block UWP Apps Running in Background, Disable SysMain (Superfetch)

**How to use:** toggle ON to apply the debloat action (disable service / set registry value), toggle OFF to restore the original state.

---

## 🎮 GAME PROFILE SWITCHER 

Automatically switches your Windows Power Plan when a specific game `.exe` launches, and reverts to your previous plan when it closes.

**How to use:**
1. Type the process name (e.g. `VALORANT-Win64-Shipping`, without `.exe`) into "Add profile type here"
2. Select a power plan from the dropdown (e.g. High Performance)
3. Click **Add Profile**
4. When the game starts, the status text confirms: "`GameName`: switched to 'PlanName'"
5. When the game exits, the power plan automatically reverts
6. Each profile can be enabled/disabled via checkbox or removed with ✕

---

## 🔧 MSI INTERRUPT OPTIMIZER

An advanced feature that pins GPU/network card interrupts to specific CPU cores to reduce interrupt latency — aimed at competitive gaming setups.

**How to use:**
1. Click **Refresh** to list MSI-capable devices (GPU, NIC, etc.)
2. For each device: enable MSI Interrupts and assign a CPU core to route its interrupts to
3. **This is an advanced feature** — if you're unsure what it does, it's safe to skip. Assigning the wrong core can make latency worse, not better.

---

## 🌐 NETWORK & TCP OPTIMIZER (One-Click)

A single button that applies all network-related tweaks at once as a shortcut.

---

## ⚙ WINDOWS OPTIMIZATION

| Button | What it does |
|---|---|
| **Check Optimization** | Checks current settings (Game Mode on/off, active power plan, etc.) and displays the results |
| **Apply Fixes** | Enables Windows Game Mode and switches the power plan to **High Performance** (with confirmation) |

---

## 🚀 STARTUP APPS

Lists apps registered to launch at Windows startup (`Run` registry keys + Startup folder), which can be enabled or disabled to reduce boot time.

**How to use:** toggle the checkbox next to each app to enable/disable it.

---

## 🧹 TEMP FILE CLEANER

Scans Windows temp folders for junk files and deletes them to free up disk space.

**How to use:**
1. A scan runs automatically, showing size and file count for each target folder
2. Select the folders you want to clean
3. Click Clean — the number of deleted files and freed space are displayed

---

## 🧠 RAM OPTIMIZER

**Trim Standby Memory** — force-clears Windows' "standby list" (cached RAM that should be reclaimable), immediately increasing available RAM. Shows an estimated freed MB.

---

## 🌐 DNS OPTIMIZER

| Button | What it does |
|---|---|
| **Benchmark** | Pings popular DNS servers (Cloudflare, Google, Quad9, etc.) to measure latency, ranking the fastest at the top |
| **Apply** (on any result) | Sets that DNS server on your active network adapter |
| **Revert** | Restores the original/automatic DNS settings |

---

## Background Processes (Process Manager)

Displays running processes grouped together, which can be **Suspended** (paused temporarily) or **Resumed** — freeing up RAM/CPU without closing them.

**How to use:**
1. **Refresh** — updates the process list
2. Select an action (Suspend/Resume) for each group
3. **Apply Actions** — applies all selected actions at once
4. A process can be **Excluded** so it's never auto-suspended

---

## Predictive Eco Mode

When enabled, the app automatically detects when a game is running (via foreground fullscreen window detection) and automatically suspends/throttles background apps (browsers, launchers, etc.) while the game runs, restoring them once it closes.

**How to use:** toggle **Enable Predictive Eco Mode** ON from the main checkbox or Settings. The target process list can be customized from the Settings window.

---

## Session Recorder

Records CPU, RAM, ping, and stutter count throughout a gaming session.

| Button | What it does |
|---|---|
| **Start Diagnostics** | Begins recording |
| **Stop Session** | Stops recording, saves a `.csv` report (to `Documents\NexZeus\Sessions`), and shows an analysis |
| **View History** | Lists the last 5 saved sessions |

---

## FPS Overlay

An in-game overlay that displays live FPS, frame time, ping, and stutter warnings (transparent and click-through when locked).

**How to use:**
1. Enable the overlay from the tray icon menu
2. By default it's **locked** (click-through — it won't intercept clicks or block gameplay)
3. Unlock it to drag and reposition, then re-lock — the position is saved automatically
4. The overlay automatically stays within screen bounds even if your monitor setup changes

> **Requires admin** — PresentMon (which captures FPS) needs elevated permissions. Without admin rights, FPS will display as `--`.

---

## Settings Window

Opened via the gear icon:
- Ping threshold, CPU threshold (for alerts)
- BloxStrike/Roblox Place ID (for Roblox-specific tracking, if used)
- Start with Windows toggle
- Auto-optimize on game start
- Auto-suspend background apps
- Predictive Eco Mode target process list

---

## Logs & Troubleshooting

- **Crash/error logs:** `Documents\NexZeus\Logs\nexzeus-YYYY-MM-DD.log` — if a feature fails, the exact reason will be here
- **Settings file:** `Documents\NexZeus\settings.json`
- **Session reports:** `Documents\NexZeus\Sessions\*.csv`
- If a feature isn't working, the first thing to check is: **is the app running as Administrator?**

---

## Auto-Update

On startup, the app silently checks GitHub Releases for a newer version. If one is found, an "Update Available" prompt appears — clicking Yes opens the download page in your browser.

---

## Building & Distributing (for developers)

```bash
dotnet publish NexZeus.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o publish
```

---

## Disclaimer

This tool modifies registry values, services, and system settings. Creating a restore point or backing up important settings beforehand is recommended. Toggling any tweak/debloat setting off reverts it to its original value, but every feature should be used with an understanding of what it does — particularly advanced settings like the MSI Interrupt Optimizer.
