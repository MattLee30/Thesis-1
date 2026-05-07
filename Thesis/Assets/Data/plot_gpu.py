"""
plot_gpu.py
Per-boid-count GPU timeseries figures from StainedGlassProfiler CSVs.

Produces one figure per detected boid count (e.g. gpu_timeseries_500.pdf),
each showing raw + rolling-average GPU frame time for all three configurations.

Usage:
  python plot_gpu.py                        # auto-discovers all *.csv here
  python plot_gpu.py a.csv b.csv c.csv      # explicit files
"""

import sys
import re
from io import StringIO
from pathlib import Path

import pandas as pd
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import matplotlib.ticker as ticker

# ── Publication style (mirrors graph_profiler.py) ────────────────────────────
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

CONFIG_ORDER  = ["Static", "Dynamic", "Hybrid"]
CONFIG_STYLES = {
    "Static":  {"color": "#1f77b4", "marker": "o", "ls": "-",  "hatch": ""},
    "Dynamic": {"color": "#d62728", "marker": "s", "ls": "--", "hatch": "///"},
    "Hybrid":  {"color": "#2ca02c", "marker": "^", "ls": "-.", "hatch": "..."},
}

ISOLATION_RE  = re.compile(
    r"^#\s*ISOLATION,baseline_ms=([0-9.]+),shaderOff_ms=([0-9.]+),cost_ms=([-.0-9]+)"
)
META_RE       = re.compile(r"^#\s*([A-Za-z_]+):\s*(.+)")
ROLLING_WINDOW = 120


# ── Parsing (mirrors graph_profiler.py) ──────────────────────────────────────

def load_csv(path: Path) -> pd.DataFrame:
    meta, data_lines = {}, []
    with open(path, encoding="utf-8-sig") as f:
        for line in f:
            line = line.rstrip("\n")
            if ISOLATION_RE.match(line):
                continue
            m = META_RE.match(line)
            if m:
                meta[m.group(1)] = m.group(2).strip()
                continue
            if not line.startswith("#"):
                data_lines.append(line)

    df = pd.read_csv(StringIO("\n".join(data_lines)))
    df.columns = df.columns.str.strip()

    if "ShaderMode" in df.columns and "Configuration" not in df.columns:
        df = df.rename(columns={"ShaderMode": "Configuration"})
    elif "Configuration" not in df.columns:
        df["Configuration"] = meta.get("Configuration", meta.get("ShaderMode", path.stem))

    if "BoidCount" not in df.columns:
        df["BoidCount"] = int(meta.get("BoidCount", 500))

    df["BoidCount"] = pd.to_numeric(df["BoidCount"], errors="coerce").fillna(500).astype(int)
    df["GPU_ms"]    = pd.to_numeric(df["GPU_ms"],    errors="coerce")

    if "IsolationActive" in df.columns:
        df = df[df["IsolationActive"] == 0].copy()

    return df


def gather(paths: list[Path]) -> pd.DataFrame:
    return pd.concat([load_csv(p) for p in paths], ignore_index=True)


# ── Figure: timeseries per boid count ────────────────────────────────────────

def fig_timeseries(df: pd.DataFrame, boid_count: int, out: Path):
    sub_all = df[df["BoidCount"] == boid_count]
    configs  = [c for c in CONFIG_ORDER if c in sub_all["Configuration"].unique()]

    fig, ax = plt.subplots(figsize=(6.5, 3.5))

    for cfg in configs:
        st  = CONFIG_STYLES.get(cfg, {})
        sub = sub_all[sub_all["Configuration"] == cfg].sort_values("Frame")
        gpu    = sub["GPU_ms"]
        frames = sub["Frame"]
        rolled = gpu.rolling(ROLLING_WINDOW, min_periods=1).mean()

        ax.plot(frames, gpu,    alpha=0.18, linewidth=0.5, color=st["color"])
        ax.plot(frames, rolled, linewidth=1.6, color=st["color"],
                linestyle=st["ls"], label=cfg)

    ax.set_xlabel("Frame")
    ax.set_ylabel("GPU Frame Time (ms)")
    ax.set_title(f"GPU Frame Time — {boid_count} Boids")
    ax.yaxis.set_minor_locator(ticker.AutoMinorLocator())
    ax.legend(loc="upper right")
    fig.tight_layout()
    fig.savefig(out)
    plt.close(fig)
    print(f"  Saved → {out.name}")


# ── Entry point ───────────────────────────────────────────────────────────────

def main(paths: list[Path]):
    print(f"Loading {len(paths)} CSV(s)...")
    df = gather(paths)

    out_dir     = paths[0].parent
    boid_counts = sorted(df["BoidCount"].unique())

    print("Writing figures...")
    for bc in boid_counts:
        fig_timeseries(df, bc, out_dir / f"gpu_timeseries_{bc}.pdf")

    print("\nDone. Include in your thesis with e.g.:")
    for bc in boid_counts:
        print(f"  \\includegraphics[width=\\textwidth]{{gpu_timeseries_{bc}.pdf}}")


if __name__ == "__main__":
    assets_dir = Path(__file__).parent

    if len(sys.argv) > 1:
        csv_paths = [Path(p) for p in sys.argv[1:]]
    else:
        csv_paths = sorted(assets_dir.glob("*.csv"))
        if not csv_paths:
            sys.exit(f"No *.csv files found in {assets_dir}")

    missing = [p for p in csv_paths if not p.exists()]
    if missing:
        sys.exit(f"File(s) not found: {missing}")

    main(csv_paths)
