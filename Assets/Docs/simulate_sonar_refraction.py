#!/usr/bin/env python3
"""
Simulate a 2D sonar beam refracted by a temperature-dependent sound-speed profile.

This mirrors the simplified layered Snell approach used by the Unity prototype:
the medium is horizontally layered, sound speed is c(depth), and the horizontal
slowness invariant is preserved step by step.

Outputs:
  - CSV: x_m, y_m, depth_m, sound_speed_mps
  - SVG: visual comparison of the refracted point and the apparent point
    placed on the unrefracted straight beam at the same path length
"""

from __future__ import annotations

import argparse
import csv
import math
from dataclasses import dataclass
from pathlib import Path


@dataclass
class Sample:
    depth_m: float
    temperature_c: float


@dataclass
class Point:
    x_m: float
    y_m: float
    depth_m: float
    sound_speed_mps: float


@dataclass
class ApparentPoint:
    x_m: float
    y_m: float
    depth_m: float
    acoustic_range_m: float
    travel_time_s: float
    reference_speed_mps: float


def geometric_path_length(points: list[Point]) -> float:
    total = 0.0
    for a, b in zip(points, points[1:]):
        total += math.hypot(b.x_m - a.x_m, b.y_m - a.y_m)
    return total


def point_on_straight_beam(
    samples: list[Sample],
    start_depth_m: float,
    launch_angle_deg: float,
    range_m: float,
) -> Point:
    angle_rad = math.radians(launch_angle_deg)
    x = math.cos(angle_rad) * range_m
    depth = max(0.0, start_depth_m - math.sin(angle_rad) * range_m)
    return Point(x, -depth, depth, sound_speed_at_depth(samples, depth))


def load_temperature_profile(path: Path) -> list[Sample]:
    samples: list[Sample] = []
    with path.open(newline="", encoding="utf-8-sig") as f:
        reader = csv.DictReader(f)
        for row in reader:
            samples.append(
                Sample(
                    depth_m=float(row["depth_m"]),
                    temperature_c=float(row["temperature_c"]),
                )
            )

    if not samples:
        raise ValueError(f"No usable rows in {path}")

    return sorted(samples, key=lambda s: s.depth_m)


def temperature_at_depth(samples: list[Sample], depth_m: float) -> float:
    if depth_m <= samples[0].depth_m:
        return samples[0].temperature_c
    if depth_m >= samples[-1].depth_m:
        return samples[-1].temperature_c

    for a, b in zip(samples, samples[1:]):
        if a.depth_m <= depth_m <= b.depth_m:
            t = (depth_m - a.depth_m) / (b.depth_m - a.depth_m)
            return a.temperature_c + (b.temperature_c - a.temperature_c) * t

    return samples[-1].temperature_c


def freshwater_sound_speed_mps(temperature_c: float, depth_m: float) -> float:
    t = temperature_c
    t2 = t * t
    t3 = t2 * t
    t4 = t3 * t
    t5 = t4 * t
    surface_speed = (
        1402.388
        + 5.03830 * t
        - 5.81090e-2 * t2
        + 3.3432e-4 * t3
        - 1.47797e-6 * t4
        + 3.1419e-9 * t5
    )
    return surface_speed + 1.63e-2 * max(0.0, depth_m)


def sound_speed_at_depth(samples: list[Sample], depth_m: float) -> float:
    temperature = temperature_at_depth(samples, depth_m)
    return freshwater_sound_speed_mps(temperature, depth_m)


def trace_refracted_path(
    samples: list[Sample],
    start_depth_m: float,
    launch_angle_deg: float,
    max_range_m: float,
    step_m: float,
) -> list[Point]:
    x = 0.0
    depth = start_depth_m
    angle_rad = math.radians(launch_angle_deg)

    horizontal = math.cos(angle_rad)
    vertical_up = math.sin(angle_rad)
    vertical_sign = 1.0 if vertical_up >= 0.0 else -1.0

    c0 = sound_speed_at_depth(samples, depth)
    invariant = horizontal / c0
    points = [Point(x, -depth, depth, c0)]

    traveled = 0.0
    while traveled < max_range_m:
        segment = min(step_m, max_range_m - traveled)

        x += horizontal * segment
        depth -= vertical_up * segment
        depth = max(0.0, depth)
        traveled += segment

        c = sound_speed_at_depth(samples, depth)
        horizontal = max(0.0, min(0.9999, invariant * c))
        vertical_mag = math.sqrt(max(0.0, 1.0 - horizontal * horizontal))
        vertical_up = vertical_sign * vertical_mag

        points.append(Point(x, -depth, depth, c))

        if depth <= 0.0 and vertical_up > 0.0:
            break

    return points


def trace_straight_path(
    samples: list[Sample],
    start_depth_m: float,
    launch_angle_deg: float,
    max_range_m: float,
    step_m: float,
) -> list[Point]:
    x = 0.0
    depth = start_depth_m
    angle_rad = math.radians(launch_angle_deg)
    dx = math.cos(angle_rad)
    vertical_up = math.sin(angle_rad)
    points = [Point(x, -depth, depth, sound_speed_at_depth(samples, depth))]

    traveled = 0.0
    while traveled < max_range_m:
        segment = min(step_m, max_range_m - traveled)
        x += dx * segment
        depth -= vertical_up * segment
        depth = max(0.0, depth)
        traveled += segment
        points.append(Point(x, -depth, depth, sound_speed_at_depth(samples, depth)))
        if depth <= 0.0 and vertical_up > 0.0:
            break

    return points


def write_csv(path: Path, points: list[Point]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", newline="", encoding="utf-8") as f:
        writer = csv.writer(f)
        writer.writerow(["x_m", "y_m", "depth_m", "sound_speed_mps"])
        for p in points:
            writer.writerow([f"{p.x_m:.6f}", f"{p.y_m:.6f}", f"{p.depth_m:.6f}", f"{p.sound_speed_mps:.6f}"])


def svg_polyline(points: list[Point], sx: float, sy: float, ox: float, oy: float) -> str:
    coords = " ".join(f"{ox + p.x_m * sx:.2f},{oy - p.y_m * sy:.2f}" for p in points)
    return coords


def write_svg(
    path: Path,
    refracted: list[Point],
    straight: list[Point],
    samples: list[Sample],
    start_depth_m: float,
    launch_angle_deg: float,
) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)

    all_points = refracted + straight
    max_x = max(p.x_m for p in all_points)
    min_y = min(p.y_m for p in all_points)
    max_y = 0.0
    width = 1200
    height = 760
    margin = 90
    plot_w = 900
    plot_h = 560
    sx = plot_w / max(1.0, max_x)
    sy = plot_h / max(1.0, max_y - min_y)
    ox = margin
    oy = margin + (max_y * sy)

    layer_lines = []
    for s in samples:
        y = oy + s.depth_m * sy
        if margin <= y <= margin + plot_h:
            layer_lines.append(
                f'<line x1="{margin}" y1="{y:.2f}" x2="{margin + plot_w}" y2="{y:.2f}" '
                'stroke="rgba(255,255,255,0.35)" stroke-width="2" stroke-dasharray="8 8"/>'
            )
            layer_lines.append(
                f'<text x="{margin + 12}" y="{y - 8:.2f}" class="tiny">{s.depth_m:g} m, {s.temperature_c:g} C</text>'
            )

    x_ticks = []
    tick_step_m = 5.0
    tick_count = int(math.floor(max_x / tick_step_m))
    for i in range(tick_count + 1):
        x_m = i * tick_step_m
        x = ox + x_m * sx
        x_ticks.append(
            f'<line x1="{x:.2f}" y1="{margin + plot_h:.2f}" x2="{x:.2f}" y2="{margin + plot_h + 8:.2f}" '
            'stroke="#d2ebf8" stroke-width="2"/>'
        )
        x_ticks.append(
            f'<text x="{x - 10:.2f}" y="{margin + plot_h + 30:.2f}" class="tiny">{x_m:g}</text>'
        )
        if i > 0:
            x_ticks.append(
                f'<line x1="{x:.2f}" y1="{margin:.2f}" x2="{x:.2f}" y2="{margin + plot_h:.2f}" '
                'stroke="rgba(255,255,255,0.13)" stroke-width="1"/>'
            )

    refracted_points = svg_polyline(refracted, sx, sy, ox, oy)
    straight_points = svg_polyline(straight, sx, sy, ox, oy)
    r_end = refracted[-1]
    s_end = straight[-1]
    r_cx = ox + r_end.x_m * sx
    r_cy = oy + r_end.y_m * sy
    s_cx = ox + s_end.x_m * sx
    s_cy = oy + s_end.y_m * sy

    path.write_text(
        f"""<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}">
  <defs>
    <style>
      .label {{ font-family: Arial, sans-serif; fill: #f6fbff; font-size: 28px; font-weight: 700; }}
      .small {{ font-family: Arial, sans-serif; fill: #e5f6ff; font-size: 20px; font-weight: 600; }}
      .tiny {{ font-family: Arial, sans-serif; fill: #d2ebf8; font-size: 15px; }}
    </style>
  </defs>
  <rect width="{width}" height="{height}" fill="#061824"/>
  <rect x="{margin}" y="{margin}" width="{plot_w}" height="{plot_h}" fill="#0b4f7a" stroke="#9eeeff" stroke-width="3"/>
  {''.join(x_ticks)}
  {''.join(layer_lines)}
  <polyline points="{straight_points}" fill="none" stroke="#ffffff" stroke-width="4" stroke-dasharray="14 10" opacity="0.75"/>
  <polyline points="{refracted_points}" fill="none" stroke="#00f0ff" stroke-width="7" stroke-linecap="round" stroke-linejoin="round"/>
  <circle cx="{ox:.2f}" cy="{oy + start_depth_m * sy:.2f}" r="5" fill="#0b2738" stroke="#d7f7ff" stroke-width="2"/>
  <circle cx="{s_cx:.2f}" cy="{oy - s_end.y_m * sy:.2f}" r="5" fill="#ff5bd7" stroke="#ffd0f4" stroke-width="2"/>
  <circle cx="{r_cx:.2f}" cy="{oy - r_end.y_m * sy:.2f}" r="5" fill="#ffdd6e" stroke="#fff5c0" stroke-width="2"/>
  <text x="90" y="58" class="label">2D Sonar Refraction Simulation</text>
  <text x="1020" y="130" class="small">Launch</text>
  <text x="1020" y="160" class="tiny">start depth: {start_depth_m:g} m</text>
  <text x="1020" y="184" class="tiny">angle: {launch_angle_deg:g} deg</text>
  <text x="1020" y="208" class="tiny">max x: {max_x:.1f} m</text>
  <text x="1020" y="240" class="small">Legend</text>
  <line x1="1020" y1="275" x2="1090" y2="275" stroke="#ffffff" stroke-width="4" stroke-dasharray="14 10"/>
  <text x="1020" y="305" class="tiny">unrefracted line</text>
  <line x1="1020" y1="345" x2="1090" y2="345" stroke="#00f0ff" stroke-width="7"/>
  <text x="1020" y="375" class="tiny">actual refracted beam</text>
  <circle cx="1032" cy="408" r="8" fill="#ff5bd7" stroke="#ffd0f4" stroke-width="3"/>
  <text x="1050" y="414" class="tiny">apparent point</text>
  <text x="1020" y="430" class="small">Endpoint</text>
  <text x="1020" y="460" class="tiny">apparent x={s_end.x_m:.2f} m</text>
  <text x="1020" y="484" class="tiny">apparent y={s_end.y_m:.2f} m</text>
  <text x="1020" y="520" class="tiny">actual x={r_end.x_m:.2f} m</text>
  <text x="1020" y="544" class="tiny">actual y={r_end.y_m:.2f} m</text>
  <text x="1020" y="590" class="small">Difference</text>
  <text x="1020" y="620" class="tiny">actual - apparent</text>
  <text x="1020" y="644" class="tiny">dx={r_end.x_m - s_end.x_m:+.3f} m</text>
  <text x="1020" y="668" class="tiny">dy={r_end.y_m - s_end.y_m:+.3f} m</text>
  <text x="1020" y="692" class="tiny">ddepth={r_end.depth_m - s_end.depth_m:+.3f} m</text>
  <text x="{margin + plot_w * 0.43:.2f}" y="{margin + plot_h + 55:.2f}" class="small">Horizontal distance x [m]</text>
  <text x="90" y="738" class="tiny">Coordinates: x is horizontal range; y is Unity-style vertical position, with water surface at y=0 and depth positive downward.</text>
</svg>
""",
        encoding="utf-8",
    )


def main() -> None:
    parser = argparse.ArgumentParser(description="Simulate sonar beam refraction through a temperature profile.")
    parser.add_argument("--profile", type=Path, default=Path("Assets/TemperatureProfiles/temperature_profile.csv"))
    parser.add_argument("--start-depth", type=float, default=30.0)
    parser.add_argument("--angle-deg", type=float, default=30.0, help="Launch angle above horizontal in degrees.")
    parser.add_argument("--max-range", type=float, default=45.0)
    parser.add_argument("--step", type=float, default=0.25)
    parser.add_argument("--out-csv", type=Path, default=Path("Assets/Docs/sonar_refraction_simulated_path.csv"))
    parser.add_argument("--out-svg", type=Path, default=Path("Assets/Docs/sonar_refraction_simulated_path.svg"))
    args = parser.parse_args()

    samples = load_temperature_profile(args.profile)
    refracted = trace_refracted_path(samples, args.start_depth, args.angle_deg, args.max_range, args.step)
    refracted_length = geometric_path_length(refracted)
    apparent_point = point_on_straight_beam(samples, args.start_depth, args.angle_deg, refracted_length)
    straight = trace_straight_path(samples, args.start_depth, args.angle_deg, refracted_length, args.step)
    if straight[-1].x_m != apparent_point.x_m or straight[-1].y_m != apparent_point.y_m:
        straight.append(apparent_point)
    write_csv(args.out_csv, refracted)
    write_svg(args.out_svg, refracted, straight, samples, args.start_depth, args.angle_deg)

    r_end = refracted[-1]
    s_end = straight[-1]
    print(f"Wrote {args.out_csv}")
    print(f"Wrote {args.out_svg}")
    print(f"Refracted path length: {refracted_length:.3f} m")
    print(f"Apparent point: x={s_end.x_m:.3f} m, y={s_end.y_m:.3f} m, depth={s_end.depth_m:.3f} m")
    print(f"Actual point:   x={r_end.x_m:.3f} m, y={r_end.y_m:.3f} m, depth={r_end.depth_m:.3f} m")
    print(f"Actual - apparent: dx={r_end.x_m - s_end.x_m:.3f} m, dy={r_end.y_m - s_end.y_m:.3f} m")


if __name__ == "__main__":
    main()
