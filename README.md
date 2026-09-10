# Wisps

A lightweight [GameHelper2](https://github.com/Gordin/GameHelper) plugin for **Path of Exile 2** that tracks Azmeri wisps in the **Wildwood** (Viridian Wildwood), displays them on the in-game map, and calculates optimal harvest routes.

---

## Features

- **In-Game Map Overlay (Tab):** Displays all discovered Azmeri wisps directly on the large in-game map.
- **Accurate Tier Color-Coding:**
  - 🟣 **Wild** (Purple)
  - 🟡 **Vivid** (Yellow)
  - 🔵 **Primal** (Blue)
  - 🟠 **Sacred** (Orange)
- **Optimal Route Calculation (Main Run):** Generates a walkable route avoiding obstacles, prioritizing dense wisp trails, larger wisps, and higher-tier rewards.
- **Even-Harvest Route (Optional):** Draws a secondary turquoise route aimed at balancing your collected wisp counts based on current deficits and zone scarcity.
- **Deep Area Scan:** Discovers wisps well beyond the local network bubble using GameHelper's sleeping-map scan to reveal the broader layout ahead of time.
- **On-Screen HUD:** Minimalistic, click-through counter showing zone wisp counts by tier, collected tally, and estimated route yields.
- **Bilingual Support:** Full English (`en-US`) and Russian (`ru-RU`) localization, automatically matching GameHelper or set independently.

---

## Installation

### Method 1: Pre-compiled Release (Recommended)
1. Download the latest `Wisps.zip` from the [Releases](https://github.com/InfernalVoooid/Wisps/releases) page.
2. Extract the contents into your GameHelper plugins directory:
   ```text
   GameHelper/Plugins/Wisps/
   ```
   *Ensure the folder structure contains `Wisps.dll` directly inside `Plugins/Wisps/`.*
3. Launch or restart GameHelper.

### Method 2: From Source
```bash
cd GameHelper/Plugins/
git clone https://github.com/InfernalVoooid/Wisps.git
dotnet build Wisps/Wisps/Wisps.csproj -c Release
```

---

## Configuration

Open the plugin settings in the GameHelper overlay:
- **Marker Size:** Adjust the diameter of wisp dots on the map.
- **Draw a collection route:** Toggle the primary optimal harvest route.
- **Even-harvest route:** Toggle the secondary tier-balancing route.
- **Deep area scan:** Reveal wisps outside the network bubble (toggle off if experiencing stutter on lower-end CPUs).
- **On-screen counter:** Position the HUD counter in any screen corner.

---

## Compatibility

- Requires **Path of Exile 2**
- Built for **GameHelper2** (`net10.0-windows`, x64)

---

## License

This project is licensed under the [MIT License](LICENSE).
