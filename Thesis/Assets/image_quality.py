"""
image_quality.py
Computes RMSE, DSSIM, and LPIPS for Dynamic and Hybrid shader screenshots
relative to the Static shader as the perceptual reference.

Expects PNGs written by ScreenshotCapture.cs:
    screenshot_000_Static.png   screenshot_000_Dynamic.png   screenshot_000_Hybrid.png
    screenshot_001_Static.png   ...

Outputs (next to the PNGs):
    quality_barchart.pdf   — RMSE / DSSIM / LPIPS bar chart
    quality_table.tex      — drop-in LaTeX table

Usage:
    python image_quality.py                # auto-search Assets/ folder
    python image_quality.py path/to/dir/   # explicit directory

Dependencies:
    pip install scikit-image lpips torch torchvision pillow numpy matplotlib
"""

import sys
from pathlib import Path
from collections import defaultdict
import numpy as np
from PIL import Image

# ── Image loading ─────────────────────────────────────────────────────────────

def load_np(path: Path) -> np.ndarray:
    return np.array(Image.open(path).convert("RGB"), dtype=np.float32) / 255.0

# ── Metrics ───────────────────────────────────────────────────────────────────

def rmse(ref: np.ndarray, tgt: np.ndarray) -> float:
    return float(np.sqrt(np.mean((ref - tgt) ** 2)))

def dssim(ref: np.ndarray, tgt: np.ndarray) -> float:
    from skimage.metrics import structural_similarity as ssim
    return 1.0 - ssim(ref, tgt, channel_axis=2, data_range=1.0)

def lpips_score(ref_path: Path, tgt_path: Path, loss_fn) -> float:
    import lpips as lp
    import torch
    ref_t = lp.im2tensor(lp.load_image(str(ref_path)))
    tgt_t = lp.im2tensor(lp.load_image(str(tgt_path)))
    with torch.no_grad():
        return float(loss_fn(ref_t, tgt_t))

# ── File discovery ────────────────────────────────────────────────────────────

def find_sets(directory: Path) -> dict:
    """Return {index: {config: path}} from screenshot_NNN_Config.png files."""
    groups: dict[int, dict[str, Path]] = defaultdict(dict)
    for png in sorted(directory.glob("screenshot_*_*.png")):
        parts = png.stem.split("_")
        if len(parts) != 3 or not parts[1].isdigit():
            continue
        groups[int(parts[1])][parts[2]] = png
    return dict(sorted(groups.items()))

# ── Aggregation ───────────────────────────────────────────────────────────────

def aggregate(rows: list) -> dict:
    by_cfg: dict[str, list] = defaultdict(list)
    for r in rows:
        by_cfg[r["config"]].append(r)
    agg = {}
    for cfg, rs in by_cfg.items():
        lp_vals = [r["lpips"] for r in rs if r["lpips"] is not None]
        agg[cfg] = {
            "rmse":  np.mean([r["rmse"]  for r in rs]),
            "dssim": np.mean([r["dssim"] for r in rs]),
            "lpips": float(np.mean(lp_vals)) if lp_vals else None,
        }
    return agg

# ── Console output ────────────────────────────────────────────────────────────

def print_summary(agg: dict):
    print("\n── Mean across all sets ─────────────────────────────────────────────")
    print(f"  {'Config':<12} {'RMSE':>8} {'DSSIM':>8} {'LPIPS':>8}")
    print("  " + "-" * 42)
    for cfg in ("Dynamic", "Hybrid"):
        if cfg not in agg:
            continue
        a = agg[cfg]
        lp_str = f"{a['lpips']:.4f}" if a["lpips"] is not None else "   N/A"
        print(f"  {cfg:<12} {a['rmse']:>8.4f} {a['dssim']:>8.4f} {lp_str:>8}")
    print()

# ── LaTeX table ───────────────────────────────────────────────────────────────

def write_latex(agg: dict, has_lpips: bool, out: Path):
    cols   = "lcc" + ("c" if has_lpips else "")
    h_extra = r" & \textbf{LPIPS} $\downarrow$" if has_lpips else ""
    ref_extra = " & 0" if has_lpips else ""

    def data_row(cfg):
        a = agg[cfg]
        cells = f"{a['rmse']:.4f} & {a['dssim']:.4f}"
        if has_lpips:
            cells += f" & {a['lpips']:.4f}" if a["lpips"] is not None else " & ---"
        return f"    {cfg} & {cells} \\\\"

    lpips_cite = (
        r"LPIPS uses the AlexNet backbone~\cite{zhang2018unreasonable}. "
        if has_lpips else ""
    )

    lines = [
        r"\begin{table}[htbp]",
        r"  \centering",
        r"  \caption{Perceptual image quality relative to the Static shader (reference). "
        + r"RMSE and DSSIM are computed with \texttt{scikit-image}; "
        + lpips_cite
        + r"Lower values indicate greater similarity to the Static reference.}",
        r"  \label{tab:image-quality}",
        f"  \\begin{{tabular}}{{{cols}}}",
        r"    \toprule",
        r"    \textbf{Configuration} & \textbf{RMSE} $\downarrow$ & \textbf{DSSIM} $\downarrow$"
        + h_extra + r" \\",
        r"    \midrule",
        f"    Static (reference) & 0 & 0{ref_extra} \\\\",
    ]
    lines += [data_row(c) for c in ("Dynamic", "Hybrid") if c in agg]
    lines += [
        r"    \bottomrule",
        r"  \end{tabular}",
        r"\end{table}",
    ]

    out.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"  Saved → {out.name}")

# ── Bar chart ─────────────────────────────────────────────────────────────────

def plot_bars(agg: dict, has_lpips: bool, out: Path):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

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

    metric_keys   = ["rmse", "dssim"] + (["lpips"] if has_lpips else [])
    metric_labels = ["RMSE", "DSSIM"] + (["LPIPS"] if has_lpips else [])
    configs = [c for c in ("Dynamic", "Hybrid") if c in agg]
    colors  = {"Dynamic": "#d62728", "Hybrid": "#2ca02c"}
    hatches = {"Dynamic": "///",     "Hybrid": "..."}

    x     = np.arange(len(metric_keys))
    width = 0.7 / len(configs)

    fig, ax = plt.subplots(figsize=(6.5, 3.8))
    for ci, cfg in enumerate(configs):
        vals   = [agg[cfg][k] if agg[cfg][k] is not None else 0.0 for k in metric_keys]
        offset = (ci - (len(configs) - 1) / 2.0) * width
        ax.bar(x + offset, vals, width * 0.9,
               label=cfg, color=colors[cfg], hatch=hatches[cfg],
               alpha=0.85, edgecolor="black", linewidth=0.5)

    ax.set_xticks(x)
    ax.set_xticklabels(metric_labels)
    ax.set_ylabel("Score (lower = more similar to Static)")
    ax.set_title("Perceptual Image Quality vs. Static Reference")
    ax.legend(loc="upper right")
    fig.tight_layout()
    fig.savefig(out)
    plt.close(fig)
    print(f"  Saved → {out.name}")

# ── Entry point ───────────────────────────────────────────────────────────────

def main(directory: Path):
    sets = find_sets(directory)
    if not sets:
        sys.exit(f"No screenshot_NNN_Config.png files found in {directory}")

    print(f"Found {len(sets)} screenshot set(s).")

    try:
        import lpips as lp_module
        loss_fn   = lp_module.LPIPS(net="alex", verbose=False)
        has_lpips = True
    except ImportError:
        print("  lpips not installed — run: pip install lpips")
        print("  LPIPS will be skipped; RMSE and DSSIM will still be computed.")
        loss_fn   = None
        has_lpips = False

    rows = []
    for idx, paths in sets.items():
        if "Static" not in paths:
            print(f"  Set {idx:03d}: no Static reference, skipping.")
            continue
        ref_np   = load_np(paths["Static"])
        ref_path = paths["Static"]

        for cfg in ("Dynamic", "Hybrid"):
            if cfg not in paths:
                print(f"  Set {idx:03d}: no {cfg}, skipping.")
                continue

            tgt_path = paths[cfg]
            tgt_np   = load_np(tgt_path)

            r = rmse(ref_np, tgt_np)
            d = dssim(ref_np, tgt_np)
            l = lpips_score(ref_path, tgt_path, loss_fn) if has_lpips else None

            rows.append({"set": idx, "config": cfg, "rmse": r, "dssim": d, "lpips": l})
            lp_str = f"{l:.4f}" if l is not None else "   N/A"
            print(f"  Set {idx:03d} | {cfg:>7} vs Static | RMSE={r:.4f}  DSSIM={d:.4f}  LPIPS={lp_str}")

    if not rows:
        print("No results to aggregate.")
        return

    agg = aggregate(rows)
    print_summary(agg)

    print("Writing outputs...")
    write_latex(agg, has_lpips, directory / "quality_table.tex")
    plot_bars(agg, has_lpips, directory / "quality_barchart.pdf")

    print("\nDone. Include in your thesis with:")
    print("  \\input{quality_table.tex}")
    print("  \\includegraphics[width=\\textwidth]{quality_barchart.pdf}")


if __name__ == "__main__":
    directory = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).parent
    main(directory)
