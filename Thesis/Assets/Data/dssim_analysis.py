"""
dssim_analysis.py
Computes DSSIM (Dissimilarity Structural Similarity Index) for stained-glass
shader screenshots against a reference image.

Reads *_screenshots.csv index files written by DSSIMCapture.cs.  For each
captured PNG it computes SSIM / DSSIM vs. the reference and summarises results
across configurations in the same style as graph_profiler.py.

Requirements:
  pip install scikit-image pillow numpy pandas matplotlib

Outputs (written next to the first screenshots CSV):
  dssim_timeseries.jpg  — DSSIM per frame, one series per configuration
  dssim_barchart.jpg    — mean ± 1σ DSSIM grouped by boid count
  dssim_table.tex       — drop-in LaTeX table

Usage:
  python dssim_analysis.py reference.png
  python dssim_analysis.py reference.png a_screenshots.csv b_screenshots.csv
  python dssim_analysis.py reference.png --size 256
"""

import sys
import re
import argparse
from pathlib import Path

import numpy as np
import pandas as pd
from PIL import Image
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import matplotlib.ticker as ticker

try:
    from skimage.metrics import structural_similarity as ssim
except ImportError:
    sys.exit("scikit-image is required:  pip install scikit-image")

# ── Publication style (mirrors graph_profiler.py) ────────────────────────────
plt.rcParams.update({
    "font.family":    "serif",
    "font.size":      10,
    "axes.labelsize": 10,
    "axes.titlesize": 11,
    "legend.fontsize": 9,
    "xtick.labelsize": 9,
    "ytick.labelsize": 9,
    "axes.grid":      True,
    "grid.alpha":     0.3,
    "grid.linestyle": "--",
    "figure.dpi":     150,
    "savefig.dpi":    300,
    "savefig.bbox":   "tight",
})

CONFIG_ORDER  = ["Static", "Dynamic", "Hybrid"]
CONFIG_STYLES = {
    "Static":  {"color": "#1f77b4", "marker": "o", "ls": "-",  "hatch": ""},
    "Dynamic": {"color": "#d62728", "marker": "s", "ls": "--", "hatch": "///"},
    "Hybrid":  {"color": "#2ca02c", "marker": "^", "ls": "-.", "hatch": "..."},
}

# Matches e.g. "Dynamic500_20260507_155922_screenshots"
SESSION_RE = re.compile(r"^(Static|Dynamic|Hybrid)(\d+)_")


def config_from_stem(stem: str) -> tuple[str, int]:
    """Parse 'Dynamic500_20260507_155922' → ('Dynamic', 500)."""
    m = SESSION_RE.match(stem)
    if m:
        return m.group(1), int(m.group(2))
    return stem, 0


# ── Image helpers ─────────────────────────────────────────────────────────────

def load_image(path: Path, size: tuple[int, int]) -> np.ndarray:
    return np.array(Image.open(path).convert("RGB").resize(size, Image.LANCZOS))


def dssim(img_a: np.ndarray, img_b: np.ndarray) -> float:
    """DSSIM = (1 − SSIM) / 2.  Range [0, 1]; 0 = pixel-identical."""
    s = ssim(img_a, img_b, channel_axis=2, data_range=255)
    return (1.0 - float(s)) / 2.0


# ── Parsing ───────────────────────────────────────────────────────────────────

def load_session(index_csv: Path, reference: np.ndarray,
                 ref_size: tuple[int, int]) -> pd.DataFrame:
    """Read one *_screenshots.csv and compute DSSIM for every listed PNG."""
    stem          = index_csv.stem.removesuffix("_screenshots")
    config, boids = config_from_stem(Path(stem).name)
    img_dir       = index_csv.parent

    rows = []
    with open(index_csv, encoding="utf-8-sig") as f:
        for line in f:
            line = line.strip()
            if not line or line.startswith("#") or line.startswith("Frame"):
                continue
            parts = line.split(",", 2)
            if len(parts) < 3:
                continue
            frame_no  = int(parts[0])
            label     = parts[1].strip()
            filename  = parts[2].strip()
            png_path  = img_dir / filename

            if not png_path.exists():
                print(f"  WARNING: missing {png_path.name}")
                continue

            shot      = load_image(png_path, ref_size)
            d         = dssim(reference, shot)
            rows.append({
                "Frame":         frame_no,
                "Label":         label,
                "DSSIM":         d,
                "SSIM":          1.0 - 2.0 * d,
                "Configuration": config,
                "BoidCount":     boids,
            })

    return pd.DataFrame(rows)


def gather_sessions(index_csvs: list[Path], reference: np.ndarray,
                    ref_size: tuple[int, int]) -> pd.DataFrame:
    frames = [load_session(p, reference, ref_size) for p in index_csvs]
    return pd.concat(frames, ignore_index=True) if frames else pd.DataFrame()


def summary_table(df: pd.DataFrame) -> pd.DataFrame:
    g   = df.groupby(["Configuration", "BoidCount"])["DSSIM"]
    tbl = g.agg(
        mean="mean", std="std", min="min", max="max",
        p5=lambda x: x.quantile(0.05),
        p95=lambda x: x.quantile(0.95),
        n="count",
    ).reset_index()
    tbl["std"]    = tbl["std"].fillna(0)
    order_map     = {c: i for i, c in enumerate(CONFIG_ORDER)}
    tbl["_order"] = tbl["Configuration"].map(lambda c: order_map.get(c, 99))
    return (tbl.sort_values(["_order", "BoidCount"])
               .drop(columns="_order")
               .reset_index(drop=True))


# ── Figure 1: DSSIM time series ───────────────────────────────────────────────

def fig_timeseries(df: pd.DataFrame, out: Path):
    configs = [c for c in CONFIG_ORDER if c in df["Configuration"].unique()]
    fig, ax = plt.subplots(figsize=(6.5, 3.5))

    for cfg in configs:
        st  = CONFIG_STYLES.get(cfg, {})
        sub = df[df["Configuration"] == cfg].sort_values("Frame")
        ax.plot(sub["Frame"], sub["DSSIM"],
                color=st["color"], marker=st["marker"], markersize=5,
                linestyle=st["ls"], linewidth=1.5, label=cfg)

    ax.set_xlabel("Frame")
    ax.set_ylabel("DSSIM  (lower = more faithful)")
    ax.set_title("Visual Fidelity vs. Reference Image over Time")
    ax.yaxis.set_minor_locator(ticker.AutoMinorLocator())
    ax.legend(loc="upper right")
    fig.tight_layout()
    fig.savefig(out)
    plt.close(fig)
    print(f"  Saved → {out.name}")


# ── Figure 2: DSSIM bar chart ─────────────────────────────────────────────────

def fig_barchart(tbl: pd.DataFrame, out: Path):
    boid_counts = sorted(tbl["BoidCount"].unique())
    configs     = [c for c in CONFIG_ORDER if c in tbl["Configuration"].unique()]
    n_configs   = len(configs)
    n_groups    = len(boid_counts)

    fig, ax   = plt.subplots(figsize=(6.5, 3.8))
    bar_width = 0.7 / n_configs
    group_x   = np.arange(n_groups)

    for ci, cfg in enumerate(configs):
        st    = CONFIG_STYLES.get(cfg, {})
        sub   = tbl[tbl["Configuration"] == cfg].set_index("BoidCount")
        x     = group_x + (ci - (n_configs - 1) / 2.0) * bar_width
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
    ax.set_ylabel("Mean DSSIM  (lower = more faithful)")
    ax.set_title("Mean DSSIM ± 1 s.d. vs. Reference by Configuration")
    ax.yaxis.set_minor_locator(ticker.AutoMinorLocator())
    ax.legend(loc="upper left")
    fig.tight_layout()
    fig.savefig(out)
    plt.close(fig)
    print(f"  Saved → {out.name}")


# ── LaTeX table ───────────────────────────────────────────────────────────────

def write_latex_table(tbl: pd.DataFrame, out: Path):
    multi_boid = tbl["BoidCount"].nunique() > 1
    col_spec   = "llccccc" if multi_boid else "lccccc"

    lines = [
        r"\begin{table}[htbp]",
        r"  \centering",
        (r"  \caption{Visual fidelity of each shader configuration measured by DSSIM "
         r"relative to the source reference image. Values are mean~$\pm$~1\,s.d.\ "
         r"over captured frames; DSSIM~$= 0$ is pixel-perfect, DSSIM~$= 1$ is "
         r"maximally dissimilar.}"),
        r"  \label{tab:dssim}",
        f"  \\begin{{tabular}}{{{col_spec}}}",
        r"    \toprule",
    ]

    header_cols = (
        r"\textbf{Configuration} & \textbf{Boids} & "
        if multi_boid else r"\textbf{Configuration} & "
    )
    lines.append(
        f"    {header_cols}"
        r"\textbf{Mean} & \textbf{Std} & \textbf{Min} & \textbf{Max} & \textbf{p95} \\"
    )
    lines.append(r"    \midrule")

    for _, row in tbl.iterrows():
        vals = " & ".join(f"{row[c]:.4f}" for c in ("mean", "std", "min", "max", "p95"))
        if multi_boid:
            lines.append(f"    {row['Configuration']} & {row['BoidCount']} & {vals} \\\\")
        else:
            lines.append(f"    {row['Configuration']} & {vals} \\\\")

    lines += [r"    \bottomrule", r"  \end{tabular}", r"\end{table}"]
    out.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"  Saved → {out.name}")


# ── Console summary ───────────────────────────────────────────────────────────

def print_summary(tbl: pd.DataFrame):
    print("\n── DSSIM summary  (lower = more faithful to reference) ─────────────────")
    col_w  = max(len(c) for c in tbl["Configuration"]) + 2
    header = (f"  {'Config':<{col_w}} {'Boids':>6}  "
              f"{'Mean':>7}  {'Std':>6}  {'Min':>6}  {'Max':>6}  {'p95':>6}  {'n':>7}")
    print(header)
    print("  " + "-" * (len(header) - 2))
    for _, r in tbl.iterrows():
        print(f"  {r['Configuration']:<{col_w}} {r['BoidCount']:>6}  "
              f"{r['mean']:>7.4f}  {r['std']:>6.4f}  {r['min']:>6.4f}  "
              f"{r['max']:>6.4f}  {r['p95']:>6.4f}  {int(r['n']):>7,}")
    print()


# ── Entry point ───────────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(
        description="Compute DSSIM for stained-glass shader screenshots vs. a reference image."
    )
    parser.add_argument("reference", type=Path,
                        help="Source/reference image (PNG or JPG)")
    parser.add_argument("index_csvs", nargs="*", type=Path,
                        help="*_screenshots.csv files from DSSIMCapture.cs "
                             "(auto-discovered in reference image's directory if omitted)")
    parser.add_argument("--size", type=int, default=512, metavar="PX",
                        help="Resize all images to PX×PX before comparison (default: 512)")
    args = parser.parse_args()

    if not args.reference.exists():
        sys.exit(f"Reference image not found: {args.reference}")

    ref_size  = (args.size, args.size)
    reference = load_image(args.reference, ref_size)

    index_csvs = list(args.index_csvs)
    if not index_csvs:
        search_dir = args.reference.parent
        index_csvs = sorted(search_dir.glob("*_screenshots.csv"))
        if not index_csvs:
            sys.exit(f"No *_screenshots.csv files found in {search_dir}\n"
                     "Run a profiling session with DSSIMCapture attached first.")

    missing = [p for p in index_csvs if not p.exists()]
    if missing:
        sys.exit(f"File(s) not found: {missing}")

    print(f"Reference : {args.reference.name}  (resized to {args.size}×{args.size})")
    print(f"Sessions  : {len(index_csvs)}")
    print("Computing DSSIM...")

    df = gather_sessions(index_csvs, reference, ref_size)
    if df.empty:
        sys.exit("No screenshots could be loaded — confirm PNG files exist "
                 "next to the index CSVs.")

    tbl     = summary_table(df)
    out_dir = index_csvs[0].parent

    print_summary(tbl)

    print("Writing figures and table...")
    fig_timeseries(df,  out_dir / "dssim_timeseries.jpg")
    fig_barchart(tbl,   out_dir / "dssim_barchart.jpg")
    write_latex_table(tbl, out_dir / "dssim_table.tex")

    print("\nDone.  Include in your thesis with e.g.:")
    print("  \\includegraphics[width=\\textwidth]{dssim_barchart.jpg}")
    print("  \\input{dssim_table.tex}")


if __name__ == "__main__":
    main()
