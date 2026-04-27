"""
graph_profiler.py
Generates thesis-quality figures and a drop-in LaTeX table from
StainedGlassProfiler CSVs.

Run three profiler sessions (one per configuration) then point this script
at all three CSVs.  It writes four files next to the first CSV:

  profiler_timeseries.pdf  — rolling-average GPU ms, one line per config
  profiler_barchart.pdf    — mean ± 1σ bar chart, grouped by boid count
  profiler_scaling.pdf     — mean GPU ms vs. boid count (only if >1 count)
  profiler_table.tex       — drop-in \\begin{table}...\\end{table} block

Usage:
  python graph_profiler.py                        # all StainedGlassProfile_*.csv here
  python graph_profiler.py a.csv b.csv c.csv      # explicit files
"""

import sys
import re
import os
from pathlib import Path
from io import StringIO

import numpy as np
import pandas as pd
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import matplotlib.ticker as ticker

# ── Publication style ─────────────────────────────────────────────────────────
plt.rcParams.update({
    "font.family":       "serif",
    "font.size":         10,
    "axes.labelsize":    10,
    "axes.titlesize":    11,
    "legend.fontsize":   9,
    "xtick.labelsize":   9,
    "ytick.labelsize":   9,
    "axes.grid":         True,
    "grid.alpha":        0.3,
    "grid.linestyle":    "--",
    "figure.dpi":        150,
    "savefig.dpi":       300,
    "savefig.bbox":      "tight",
})

# One color + marker + linestyle per configuration so figures read in B&W print
CONFIG_ORDER  = ["Static", "Dynamic", "Hybrid"]
CONFIG_STYLES = {
    "Static":  {"color": "#1f77b4", "marker": "o", "ls": "-",  "hatch": ""},
    "Dynamic": {"color": "#d62728", "marker": "s", "ls": "--", "hatch": "///"},
    "Hybrid":  {"color": "#2ca02c", "marker": "^", "ls": "-.", "hatch": "..."},
}

ISOLATION_RE = re.compile(
    r"^#\s*ISOLATION,baseline_ms=([0-9.]+),shaderOff_ms=([0-9.]+),cost_ms=([-.0-9]+)"
)
META_RE = re.compile(r"^#\s*([A-Za-z_]+):\s*(.+)")

ROLLING_WINDOW = 120


# ── Parsing ───────────────────────────────────────────────────────────────────

def load_csv(path: Path) -> tuple[pd.DataFrame, dict, list[dict]]:
    """Return (frame DataFrame, metadata dict, isolation events list)."""
    meta = {}
    isolation_events = []
    data_lines = []

    with open(path, encoding="utf-8-sig") as f:
        for line in f:
            line = line.rstrip("\n")
            m_iso = ISOLATION_RE.match(line)
            if m_iso:
                isolation_events.append({
                    "baseline_ms":  float(m_iso.group(1)),
                    "shaderOff_ms": float(m_iso.group(2)),
                    "cost_ms":      float(m_iso.group(3)),
                })
                continue
            m_meta = META_RE.match(line)
            if m_meta:
                meta[m_meta.group(1)] = m_meta.group(2).strip()
                continue
            if not line.startswith("#"):
                data_lines.append(line)

    df = pd.read_csv(StringIO("\n".join(data_lines)))
    df.columns = df.columns.str.strip()

    # Back-compat: CSVs without Configuration/BoidCount columns use filename stem
    if "Configuration" not in df.columns:
        df["Configuration"] = meta.get("Configuration", path.stem)
    if "BoidCount" not in df.columns:
        df["BoidCount"] = int(meta.get("BoidCount", 500))

    df["BoidCount"] = pd.to_numeric(df["BoidCount"], errors="coerce").fillna(500).astype(int)
    df["GPU_ms"]    = pd.to_numeric(df["GPU_ms"],    errors="coerce")

    # Drop warmup rows flagged as IsolationActive
    if "IsolationActive" in df.columns:
        df = df[df["IsolationActive"] == 0].copy()

    return df, meta, isolation_events


def gather(paths: list[Path]) -> tuple[pd.DataFrame, dict, list[dict]]:
    """Concatenate all CSVs; return combined df, hardware meta, isolation list."""
    frames, all_meta, all_iso = [], {}, []
    for p in paths:
        df, meta, iso = load_csv(p)
        frames.append(df)
        all_meta.update(meta)   # last file wins for hardware fields
        all_iso.extend(iso)
    return pd.concat(frames, ignore_index=True), all_meta, all_iso


def summary_table(df: pd.DataFrame) -> pd.DataFrame:
    """Compute mean, std, min, max, p5, p95 grouped by (Configuration, BoidCount)."""
    g = df.groupby(["Configuration", "BoidCount"])["GPU_ms"]
    tbl = g.agg(
        mean="mean",
        std="std",
        min="min",
        max="max",
        p5=lambda x: x.quantile(0.05),
        p95=lambda x: x.quantile(0.95),
        n="count",
    ).reset_index()
    tbl["std"] = tbl["std"].fillna(0)

    # Sort by canonical config order, then boid count
    order_map = {c: i for i, c in enumerate(CONFIG_ORDER)}
    tbl["_order"] = tbl["Configuration"].map(lambda c: order_map.get(c, 99))
    tbl = tbl.sort_values(["_order", "BoidCount"]).drop(columns="_order").reset_index(drop=True)
    return tbl


# ── Figure 1: Time series ─────────────────────────────────────────────────────

def fig_timeseries(df: pd.DataFrame, out: Path):
    configs = [c for c in CONFIG_ORDER if c in df["Configuration"].unique()]
    fig, ax = plt.subplots(figsize=(6.5, 3.5))

    for cfg in configs:
        st = CONFIG_STYLES.get(cfg, {})
        sub = df[df["Configuration"] == cfg].sort_values("Frame")
        gpu = sub["GPU_ms"]
        frames = sub["Frame"]
        rolled = gpu.rolling(ROLLING_WINDOW, min_periods=1).mean()

        ax.plot(frames, gpu,    alpha=0.18, linewidth=0.5, color=st["color"])
        ax.plot(frames, rolled, linewidth=1.6, color=st["color"],
                linestyle=st["ls"], label=cfg)

    ax.set_xlabel("Frame")
    ax.set_ylabel("GPU Frame Time (ms)")
    ax.set_title("GPU Frame Time by Rendering Configuration")
    ax.yaxis.set_minor_locator(ticker.AutoMinorLocator())
    ax.legend(loc="upper right")
    fig.tight_layout()
    fig.savefig(out)
    plt.close(fig)
    print(f"  Saved → {out.name}")


# ── Figure 2: Bar chart with error bars ───────────────────────────────────────

def fig_barchart(tbl: pd.DataFrame, out: Path):
    boid_counts = sorted(tbl["BoidCount"].unique())
    configs     = [c for c in CONFIG_ORDER if c in tbl["Configuration"].unique()]
    n_configs   = len(configs)
    n_groups    = len(boid_counts)

    fig, ax = plt.subplots(figsize=(6.5, 3.8))

    bar_width  = 0.7 / n_configs
    group_x    = np.arange(n_groups)

    for ci, cfg in enumerate(configs):
        st   = CONFIG_STYLES.get(cfg, {})
        sub  = tbl[tbl["Configuration"] == cfg].set_index("BoidCount")
        x    = group_x + (ci - (n_configs - 1) / 2.0) * bar_width
        means = [sub.loc[b, "mean"] if b in sub.index else 0 for b in boid_counts]
        stds  = [sub.loc[b, "std"]  if b in sub.index else 0 for b in boid_counts]

        ax.bar(x, means, width=bar_width * 0.9,
               color=st["color"], hatch=st["hatch"], alpha=0.85,
               label=cfg, edgecolor="black", linewidth=0.5)
        ax.errorbar(x, means, yerr=stds, fmt="none",
                    ecolor="black", elinewidth=1.0, capsize=3)

    ax.set_xticks(group_x)
    ax.set_xticklabels([str(b) for b in boid_counts])
    ax.set_xlabel("Boid Count")
    ax.set_ylabel("Mean GPU Frame Time (ms)")
    ax.set_title("Mean Frame Cost ± 1 s.d. by Configuration")
    ax.yaxis.set_minor_locator(ticker.AutoMinorLocator())
    ax.legend(loc="upper left")
    fig.tight_layout()
    fig.savefig(out)
    plt.close(fig)
    print(f"  Saved → {out.name}")


# ── Figure 3: Scaling plot ────────────────────────────────────────────────────

def fig_scaling(tbl: pd.DataFrame, out: Path):
    configs     = [c for c in CONFIG_ORDER if c in tbl["Configuration"].unique()]
    boid_counts = sorted(tbl["BoidCount"].unique())

    fig, ax = plt.subplots(figsize=(6.5, 3.5))

    for cfg in configs:
        st  = CONFIG_STYLES.get(cfg, {})
        sub = tbl[tbl["Configuration"] == cfg].sort_values("BoidCount")
        ax.plot(sub["BoidCount"], sub["mean"],
                color=st["color"], marker=st["marker"],
                linestyle=st["ls"], linewidth=1.5, markersize=6, label=cfg)
        ax.fill_between(sub["BoidCount"],
                        sub["mean"] - sub["std"],
                        sub["mean"] + sub["std"],
                        alpha=0.15, color=st["color"])

    ax.set_xlabel("Boid Count")
    ax.set_ylabel("Mean GPU Frame Time (ms)")
    ax.set_title("Frame Time Scaling with Boid Population")
    ax.yaxis.set_minor_locator(ticker.AutoMinorLocator())
    ax.legend(loc="upper left")
    fig.tight_layout()
    fig.savefig(out)
    plt.close(fig)
    print(f"  Saved → {out.name}")


# ── LaTeX table ───────────────────────────────────────────────────────────────

def write_latex_table(tbl: pd.DataFrame, meta: dict, isolation: list[dict], out: Path):
    gpu_name  = meta.get("GPU",   "GPU not recorded")
    cpu_name  = meta.get("CPU",   "CPU not recorded")
    ram_mb    = meta.get("RAM_MB","")
    ram_str   = f", {int(ram_mb)//1024}\\,GB RAM" if ram_mb.isdigit() else ""

    multi_boid = tbl["BoidCount"].nunique() > 1
    has_iso    = len(isolation) > 0

    lines = []
    lines.append(r"\begin{table}[htbp]")
    lines.append(r"  \centering")

    # Caption — hardware info goes here so the thesis body stays clean
    cap = (
        r"  \caption{Measured per-frame GPU cost by rendering configuration. "
        r"Values are mean~$\pm$~1\,s.d.\ over recorded frames. "
        f"Hardware: {gpu_name}, {cpu_name}{ram_str}.}}"
    )
    lines.append(cap)
    lines.append(r"  \label{tab:frame-cost}")

    col_spec = "llccccc" if multi_boid else "lccccc"
    lines.append(f"  \\begin{{tabular}}{{{col_spec}}}")
    lines.append(r"    \toprule")

    if multi_boid:
        lines.append(
            r"    \textbf{Configuration} & \textbf{Boids} & "
            r"\textbf{Mean (ms)} & \textbf{Std (ms)} & "
            r"\textbf{Min (ms)} & \textbf{Max (ms)} & \textbf{p95 (ms)} \\"
        )
    else:
        lines.append(
            r"    \textbf{Configuration} & "
            r"\textbf{Mean (ms)} & \textbf{Std (ms)} & "
            r"\textbf{Min (ms)} & \textbf{Max (ms)} & \textbf{p95 (ms)} \\"
        )
    lines.append(r"    \midrule")

    for _, row in tbl.iterrows():
        cfg    = row["Configuration"]
        mean_  = f"{row['mean']:.2f}"
        std_   = f"{row['std']:.2f}"
        min_   = f"{row['min']:.2f}"
        max_   = f"{row['max']:.2f}"
        p95_   = f"{row['p95']:.2f}"
        if multi_boid:
            lines.append(f"    {cfg} & {row['BoidCount']} & {mean_} & {std_} & {min_} & {max_} & {p95_} \\\\")
        else:
            lines.append(f"    {cfg} & {mean_} & {std_} & {min_} & {max_} & {p95_} \\\\")

    lines.append(r"    \bottomrule")
    lines.append(r"  \end{tabular}")

    # Isolation footnote
    if has_iso:
        iso_parts = []
        for ev in isolation:
            iso_parts.append(
                f"shader cost~$\\approx${ev['cost_ms']:.2f}\\,ms "
                f"(baseline {ev['baseline_ms']:.2f}\\,ms, "
                f"without shader {ev['shaderOff_ms']:.2f}\\,ms)"
            )
        lines.append(r"  \smallskip")
        lines.append(r"  \begin{minipage}{\linewidth}")
        lines.append(r"    \footnotesize Isolation measurement: " + "; ".join(iso_parts) + ".")
        lines.append(r"  \end{minipage}")

    lines.append(r"\end{table}")

    out.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"  Saved → {out.name}")


# ── Console summary ───────────────────────────────────────────────────────────

def print_summary(tbl: pd.DataFrame, meta: dict, isolation: list[dict]):
    print("\n── Hardware ────────────────────────────────────────────────")
    for k in ("GPU", "CPU", "RAM_MB", "UnityVersion"):
        if k in meta:
            print(f"  {k:14s}: {meta[k]}")

    print("\n── Per-configuration statistics (ms) ───────────────────────")
    col_w = max(len(c) for c in tbl["Configuration"]) + 2
    header = f"  {'Config':<{col_w}} {'Boids':>6}  {'Mean':>6}  {'Std':>5}  {'Min':>5}  {'Max':>5}  {'p95':>5}  {'n':>7}"
    print(header)
    print("  " + "-" * (len(header) - 2))
    for _, r in tbl.iterrows():
        print(f"  {r['Configuration']:<{col_w}} {r['BoidCount']:>6}  "
              f"{r['mean']:>6.2f}  {r['std']:>5.2f}  {r['min']:>5.2f}  "
              f"{r['max']:>5.2f}  {r['p95']:>5.2f}  {int(r['n']):>7,}")

    if isolation:
        print("\n── Isolation measurements ──────────────────────────────────")
        for ev in isolation:
            delta = ev["cost_ms"]
            sign  = "+" if delta >= 0 else ""
            print(f"  baseline {ev['baseline_ms']:.3f} ms  |  "
                  f"without shader {ev['shaderOff_ms']:.3f} ms  |  "
                  f"shader cost {sign}{delta:.3f} ms")
    print()


# ── Entry point ───────────────────────────────────────────────────────────────

def main(paths: list[Path]):
    print(f"Loading {len(paths)} CSV(s)...")
    df, meta, isolation = gather(paths)

    tbl     = summary_table(df)
    out_dir = paths[0].parent

    print_summary(tbl, meta, isolation)

    print("Writing figures and table...")
    fig_timeseries(df, out_dir / "profiler_timeseries.pdf")
    fig_barchart(tbl,  out_dir / "profiler_barchart.pdf")

    if tbl["BoidCount"].nunique() > 1:
        fig_scaling(tbl, out_dir / "profiler_scaling.pdf")
    else:
        print("  (profiler_scaling.pdf skipped — only one boid count detected)")

    write_latex_table(tbl, meta, isolation, out_dir / "profiler_table.tex")

    print("\nDone. Include figures in your thesis with e.g.:")
    print("  \\includegraphics[width=\\textwidth]{profiler_barchart.pdf}")
    print("  \\input{profiler_table.tex}")


if __name__ == "__main__":
    assets_dir = Path(__file__).parent

    if len(sys.argv) > 1:
        csv_paths = [Path(p) for p in sys.argv[1:]]
    else:
        csv_paths = sorted(assets_dir.glob("StainedGlassProfile_*.csv"))
        if not csv_paths:
            sys.exit(f"No StainedGlassProfile_*.csv files found in {assets_dir}")

    missing = [p for p in csv_paths if not p.exists()]
    if missing:
        sys.exit(f"File(s) not found: {missing}")

    main(csv_paths)
