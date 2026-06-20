# BV5000

Unity HDRP project for simulating BV5000-style sonar scanning and exporting point-cloud data.

## Environment

- Unity: `6000.3.9f1`
- Render pipeline: HDRP
- Main scene: `Assets/OutdoorsScene.unity`
- Output folder: `Assets/SonarOutput/`

## Main Features

- 360-degree sonar scan simulation
- BV5000-style angular sampling preset
- Fan beam ray tracing
- Optional temperature-profile influence on acoustic range and refraction
- CSV point-cloud export for CloudCompare-style Z-up coordinates
- Terrain-only point-cloud density shaping
- Adjustable sparse/thick point-cloud regions with blending, rotation, ellipsoid shape, and thickness variation

## Key Scripts

- `Assets/Script/SonarScanner.cs`  
  Runs immediate or animated sonar scans, stores recent points, and writes CSV output.

- `Assets/Script/SonarConfig.cs`  
  Main scan, beam, physics, output, intensity, temperature, and density settings.

- `Assets/Script/SonarBeamTracer.cs`  
  Traces sonar rays, fan beams, acoustic range, and refraction-aware hits.

- `Assets/Script/SonarDensityRegionVolume.cs`  
  Component for setting terrain-local regions where point clouds become thicker or more sparse.

- `Assets/Script/PointCloudCsvWriter.cs`  
  Exports scanned points to CSV.

## Basic Usage

1. Open the project with Unity `6000.3.9f1`.
2. Open `Assets/OutdoorsScene.unity`.
3. Select the sonar object with `SonarScanner` and `SonarConfig`.
4. In the Inspector, use:
   - `Run Scan`
   - `Run Animated Scan`
   - `Test Fan Once`
5. CSV files are written to `Assets/SonarOutput/`.

## Terrain Density Regions

Add `Sonar Density Region Volume` to a Terrain object to control local point-cloud thickness.

Important settings:

- `Terrain Hits Only`  
  When enabled, density shaping affects only points that hit a Terrain collider.

- `Shape`  
  `Box` or `Ellipsoid`.

- `Local Center`  
  Region center in Terrain-local coordinates.

- `Local Euler Angles`  
  Region rotation. Use the Y value to rotate the region horizontally.

- `Local Size`  
  Region size. For `Ellipsoid`, the XYZ ratio controls the ellipse shape.

- `Keep Probability`  
  Probability that the original hit point is kept inside the region.

- `Extra Points Per Hit`  
  Number of additional points generated around each kept hit.

- `Blend Distance Meters`  
  Fades density smoothly near the region boundary.

- `Lateral Jitter Meters`  
  Random spread along the terrain surface.

- `Normal Thickness Meters`  
  Random spread along the terrain normal, creating visible thickness.

- `Thickness Variation`  
  Per-point random variation of thickness.

- `Thickness Patch Scale Meters`  
  Size of broad thick/thin patches.

- `Thickness Patch Variation`  
  Strength of spatial thickness variation.

## Suggested Density Settings

Subtle sparse/thick terrain:

```text
Keep Probability = 0.7
Extra Points Per Hit = 4
Blend Distance Meters = 2
Lateral Jitter Meters = 0.08
Normal Thickness Meters = 0.2
Thickness Variation = 0.4
Thickness Patch Scale Meters = 6
Thickness Patch Variation = 0.4
```

Large uneven thickness:

```text
Keep Probability = 0.8
Extra Points Per Hit = 6
Blend Distance Meters = 3
Lateral Jitter Meters = 0.12
Normal Thickness Meters = 0.35
Thickness Variation = 0.6
Thickness Patch Scale Meters = 10
Thickness Patch Variation = 0.7
```

## Git Notes

Generated Unity folders such as `Library/`, `Temp/`, `Logs/`, `UserSettings/`, recovery scenes, Python cache files, and sonar CSV output are ignored by `.gitignore`.
