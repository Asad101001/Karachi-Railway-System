# Karachi Railway System — Queue Simulation Suite

Desktop simulation software for Karachi Railway, built with C# and WPF on .NET 8.
The application supports three single-server queue models (M/M/1, M/G/1, G/G/1) and features a dynamic UI for exploring passenger flow, live metrics, and comprehensive simulation tables.

---

## What’s New (Recent Updates)

- **Complete UI/UX Overhaul**: Adopted a premium, sleek Light Green and White color palette with Gold/Yellow accents. The typography has been refreshed using the **Outfit** font for distinctive headings and KPI cards, along with smooth micro-animations on interactive elements for a highly polished experience.
- **Sidebar Navigation**: Replaced the previous 3-column layout with a sleek icon-based sidebar, featuring six main tabs:
  - **🏠 Home**: Real-time Animated Flow Diagram and quick KPI tracking.
  - **🧮 Model**: A detailed column-wise **M/M/1 Simulation Table** allowing users to inspect probabilities, inter-arrival times, service times, and wait times row by row.
  - **📈 Metrics**: Visual Cartesian charts powered by `LiveChartsCore` for Average Queue Wait ($W_q$) Trend and Average Queue Length ($L_q$) Trend across the latest simulation runs.
  - **📋 Reports**: A comprehensive DataGrid history of all simulation runs in the current session, complete with customer-level records featuring individual arrival times, wait times, turnaround times, and drill-down "View" actions.
  - **⚙ Settings**: Expanded parameters configuration and playback controls.
  - **ℹ About**: University / Department branding and model description.
- **Live Performance KPIs**: Quick-glance KPI strip providing metrics like Utilization ($\rho$), Wait times ($W_q, W$), Queue Lengths ($L_q, L$), and System Throughput.
- **Playback Controls & Speed Slider**: Refined simulation playback with Play, Pause, Step-through, Stop, Reset and a real-time adjustable speed slider (`0.1x` to `3x`).
- **Comprehensive Terminologies**: The About page now features detailed Queueing Theory Terminologies, Formulae, and Developer credits.

---

## Requirements

- .NET 8 SDK
- Windows OS (WPF app, net8.0-windows)

---

## Run Locally

You can launch the application directly using the .NET CLI:

```bash
dotnet build KarachiRailwaySystem.sln
dotnet run --project src/KarachiRailway.Desktop/KarachiRailway.Desktop.csproj -c Debug
```

To run unit tests:
```bash
dotnet test KarachiRailwaySystem.sln -c Debug
```

---

## Model Behavior

### M/M/1
- Inter-arrival times: exponential ($\lambda$)
- Service times: exponential ($\mu$)
- Uses standard M/M/1 exact formulas.

### M/G/1
- Inter-arrival times: exponential ($\lambda$)
- Service times: general (gamma), controlled by Service CV ($C_s$)
- Uses Pollaczek–Khinchine-based metrics.

### G/G/1
- Inter-arrival times: general (gamma), controlled by Arrival CV ($C_a$)
- Service times: general (gamma), controlled by Service CV ($C_s$)
- Uses Kingman approximation for waiting-time metrics.

---

## Build EXE (Release)

Generate a Windows x64 self-contained single-file executable:

```powershell
Get-Process KarachiRailway.Desktop -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet publish src/KarachiRailway.Desktop/KarachiRailway.Desktop.csproj `
  -c Release -r win-x64 --self-contained true `
  /p:PublishSingleFile=true `
  /p:PublishTrimmed=false `
  /p:IncludeNativeLibrariesForSelfExtract=true
```

**Output EXE location:**
`src/KarachiRailway.Desktop/bin/Release/net8.0-windows/win-x64/publish/KarachiRailway.Desktop.exe`

---

## Project Structure

```
KarachiRailwaySystem.sln
KarachiRailwaySystem.slnx
├── src/
│   ├── KarachiRailway.Simulation/    # Core queue mathematics and logic
│   └── KarachiRailway.Desktop/       # WPF Application (ViewModels, Views, UI)
└── tests/
    └── KarachiRailway.Tests/         # Unit testing suite
```
