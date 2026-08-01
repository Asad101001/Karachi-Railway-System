# Karachi Railway System — Queue Simulation Suite

Desktop simulation software for Karachi Railway, built with C# and WPF on .NET 8.
The application supports three single-server queue models (M/M/1, M/G/1, G/G/1) and features a dynamic UI for exploring passenger flow, live metrics, and comprehensive simulation tables.

---

## What’s New (Recent Updates)

- **Complete UI/UX Overhaul**: Adopted a premium, sleek Light Green and White color palette with Gold/Yellow accents. The typography has been refreshed using the **Outfit** font for distinctive headings and KPI cards, along with smooth micro-animations on interactive elements for a highly polished experience.
- **Sidebar Navigation**: Replaced the previous 3-column layout with a sleek icon-based sidebar, featuring six main tabs:
  - **🏠 Home**: Real-time Animated Flow Diagram and quick KPI tracking.
  - **🧮 Model**: A detailed column-wise **M/M/1 Simulation Table** allowing users to inspect probabilities, inter-arrival times, service times, and wait times row by row.
  - **📈 Metrics**: Visual bar charts for Server Utilization (ρ), Average Queue Wait ($W_q$), and Throughput across multiple simulation runs.
  - **📋 Reports**: A comprehensive DataGrid history of all simulation runs in the current session.
  - **⚙ Settings**: Expanded parameters configuration and playback controls.
  - **ℹ About**: University / Department branding and model description.
- **Live Performance KPIs**: Quick-glance KPI strip providing metrics like Utilization ($\rho$), Wait times ($W_q, W$), Queue Lengths ($L_q, L$), and System Throughput.
- **Playback Controls**: Refined simulation playback with Play, Pause, Step-through, Stop, and Speed controls (0.25x to 2x).

---

## Requirements

- .NET 8 SDK
- Windows OS (WPF app, net8.0-windows)

---

## Run Locally

You can launch the application directly using the .NET CLI:

```bash
dotnet build KarachiRailwaySystem.slnx
dotnet run --project src/KarachiRailway.Desktop/KarachiRailway.Desktop.csproj -c Debug
```

To run unit tests:
```bash
dotnet test KarachiRailwaySystem.slnx -c Debug
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
KarachiRailwaySystem.slnx
├── src/
│   ├── KarachiRailway.Simulation/    # Core queue mathematics and logic
│   └── KarachiRailway.Desktop/       # WPF Application (ViewModels, Views, UI)
└── tests/
    └── KarachiRailway.Tests/         # Unit testing suite
```
