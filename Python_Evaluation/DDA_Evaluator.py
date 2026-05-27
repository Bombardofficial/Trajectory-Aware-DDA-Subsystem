#!/usr/bin/env python3

from __future__ import annotations

import os
import re
import glob
import math
from dataclasses import dataclass
from typing import Dict, Optional, Tuple, List

import numpy as np
import pandas as pd
import matplotlib.pyplot as plt
import seaborn as sns
from scipy.stats import spearmanr, mannwhitneyu

# =========================
# CONFIG
# =========================

CONFIG = {
    "input_dir": ".",
    "output_dir": "thesis_output",
    "track_segments": 50,
    "plots_dpi": 300,
    "save_pdf": False,
}

sns.set_theme(style="whitegrid", context="paper", font_scale=1.2)

OUT_DIR = CONFIG["output_dir"]
PLOTS_DIR = os.path.join(OUT_DIR, "plots")


# =========================
# HELPERS
# =========================

def ensure_dirs() -> None:
    os.makedirs(OUT_DIR, exist_ok=True)
    os.makedirs(PLOTS_DIR, exist_ok=True)


def savefig_multi(path_no_ext: str) -> None:
    plt.tight_layout()
    plt.savefig(f"{path_no_ext}.png", dpi=CONFIG["plots_dpi"], bbox_inches="tight")
    if CONFIG["save_pdf"]:
        plt.savefig(f"{path_no_ext}.pdf", bbox_inches="tight")
    plt.close()


def parse_meta_lines(file_path: str) -> Dict[str, str]:
    meta: Dict[str, str] = {}
    try:
        with open(file_path, "r", encoding="utf-8") as f:
            for line in f:
                line = line.lstrip("\ufeff").strip()
                if not line.startswith("#"):
                    break
                line = line[1:].strip()
                if "=" in line:
                    k, v = line.split("=", 1)
                    meta[k.strip()] = v.strip()
    except Exception:
        pass
    return meta


def extract_session_id_from_filename(path: str) -> Optional[str]:
    name = os.path.basename(path)
    m = re.search(r"session_([a-fA-F0-9]{32})_", name)
    if m:
        return m.group(1)
    return None


def normalized_progress_from_time(df: pd.DataFrame, time_col: str) -> pd.Series:
    if time_col not in df.columns or df.empty:
        return pd.Series(dtype=float)
    t_min = df[time_col].min()
    t_max = df[time_col].max()
    if pd.isna(t_min) or pd.isna(t_max) or math.isclose(t_min, t_max):
        return pd.Series(np.zeros(len(df)), index=df.index)
    return (df[time_col] - t_min) / (t_max - t_min)


def to_numeric_inplace(df: pd.DataFrame, cols: List[str]) -> None:
    for c in cols:
        if c in df.columns:
            df[c] = pd.to_numeric(df[c], errors="coerce")

def replace_negative_sentinels_with_nan(df: pd.DataFrame, cols: List[str], sentinel: float = -1.0) -> None:
    for c in cols:
        if c in df.columns:
            df[c] = df[c].replace(sentinel, np.nan)

def compute_spearman(df: pd.DataFrame, x: str, y: str) -> Tuple[float, float, int]:
    if x not in df.columns or y not in df.columns:
        return np.nan, np.nan, 0
    clean = df[[x, y]].dropna()
    if len(clean) < 3:
        return np.nan, np.nan, len(clean)
    r, p = spearmanr(clean[x], clean[y])
    return float(r), float(p), int(len(clean))


# =========================
# DATA MODEL
# =========================

@dataclass
class SessionBundle:
    session_id: str
    condition: str = "Unknown"
    player_count: float = np.nan
    skill_path: Optional[str] = None
    trajectory_path: Optional[str] = None
    event_path: Optional[str] = None
    skill_df: Optional[pd.DataFrame] = None
    trajectory_df: Optional[pd.DataFrame] = None
    event_df: Optional[pd.DataFrame] = None


# =========================
# DISCOVERY
# =========================

def discover_logs(input_dir: str) -> Dict[str, SessionBundle]:
    skill_files = glob.glob(os.path.join(input_dir, "**", "session_*_skill.csv"), recursive=True)
    traj_files = glob.glob(os.path.join(input_dir, "**", "session_*_trajectory.csv"), recursive=True)
    event_files = glob.glob(os.path.join(input_dir, "**", "session_*_events.csv"), recursive=True)

    sessions: Dict[str, SessionBundle] = {}

    for path in skill_files + traj_files + event_files:
        sid = extract_session_id_from_filename(path)
        if sid is None:
            continue

        if sid not in sessions:
            meta = parse_meta_lines(path)

            # condition check
            path_lower = path.lower()
            if "baseline" in path_lower:
                cond = "Baseline"
            elif "adaptive" in path_lower:
                cond = "Adaptive"
            else:
                cond = "Unknown"

            sessions[sid] = SessionBundle(
                session_id=sid,
                condition=cond,
                player_count=float(meta["playerCount"]) if "playerCount" in meta else np.nan
            )

        if path.endswith("_skill.csv"):
            sessions[sid].skill_path = path
        elif path.endswith("_trajectory.csv"):
            sessions[sid].trajectory_path = path
        elif path.endswith("_events.csv"):
            sessions[sid].event_path = path

    return sessions


# =========================
# LOADING
# =========================

def load_skill_csv(path: str) -> pd.DataFrame:
    df = pd.read_csv(path, comment="#", encoding="utf-8-sig")
    numeric_cols = [
        "time", "currentDriverPlayerNum",
        "p1Points", "p2Points", "p3Points", "p4Points",
        "globalSkill", "globalConf", "globalOverall", "globalSpawn",
        "globalPrecision", "globalPunishment", "globalRewardBias",
        "driverSkill", "driverConf", "driverOverall", "driverSpawn",
        "driverPrecision", "driverPunishment", "driverRewardBias",
        "driverCollectSuccess", "driverCollectMiss", "driverObstacleHit",
        "driverMgPass", "driverMgPerfect", "driverMgFail",
        "tenureSeconds", "tenure01", "advantage01"
    ]
    to_numeric_inplace(df, numeric_cols)

    if "time" in df.columns:
        df["normalized_time"] = normalized_progress_from_time(df, "time")
    if "driverSkill" in df.columns:
        df["driverSkillDeltaAbs"] = df["driverSkill"].diff().abs()

    return df


def load_trajectory_csv(path: str) -> pd.DataFrame:
    df = pd.read_csv(path, comment="#", encoding="utf-8-sig")
    numeric_cols = [
        "t", "trackT", "lane", "speed", "speedLimit", "overshootRatio",
        "isDrifting", "isGrounded", "cornerAngle", "curvature",
        "spawnPressure", "precision", "punishment", "rewardBias",
        "cornerSmooth01", "laneJitter01", "overspeedControl01"
    ]
    to_numeric_inplace(df, numeric_cols)

    # IMPORTANT:
    # -1 means "ignore / no valid sample" in the Unity trajectory export
    replace_negative_sentinels_with_nan(
        df,
        ["cornerSmooth01", "laneJitter01", "overspeedControl01"],
        sentinel=-1.0
    )

    if "t" in df.columns:
        df["normalized_time"] = normalized_progress_from_time(df, "t")
    if "trackT" in df.columns:
        segs = CONFIG["track_segments"]
        df["track_segment"] = np.floor(df["trackT"].clip(0, 0.999999) * segs).astype("Int64")

    return df


def load_event_csv(path: str) -> pd.DataFrame:
    df = pd.read_csv(path, comment="#")
    numeric_cols = ["t", "trackT", "lane", "speed", "v1", "v2", "v3"]
    to_numeric_inplace(df, numeric_cols)

    if "t" in df.columns:
        df["normalized_time"] = normalized_progress_from_time(df, "t")
    if "trackT" in df.columns:
        segs = CONFIG["track_segments"]
        df["track_segment"] = np.floor(df["trackT"].clip(0, 0.999999) * segs).astype("Int64")
    if "eventType" in df.columns:
        df["eventType"] = df["eventType"].astype(str)
        df["is_crash_event"] = df["eventType"].str.contains("crash", case=False, na=False)

    return df


def load_all_sessions(sessions: Dict[str, SessionBundle]) -> Dict[str, SessionBundle]:
    for sid, bundle in sessions.items():
        if bundle.skill_path:
            bundle.skill_df = load_skill_csv(bundle.skill_path)
        if bundle.trajectory_path:
            bundle.trajectory_df = load_trajectory_csv(bundle.trajectory_path)
        if bundle.event_path:
            bundle.event_df = load_event_csv(bundle.event_path)
    return sessions


# =========================
# FAIRNESS METRICS
# =========================

def compute_score_spread_series(skill_df: pd.DataFrame) -> pd.DataFrame:
    if skill_df is None or skill_df.empty:
        return pd.DataFrame()

    needed = {"time", "p1Points", "p2Points", "p3Points", "p4Points"}
    if not needed.issubset(skill_df.columns):
        return pd.DataFrame()

    work = skill_df.copy()

    # Only those columns are counted in, where the score at the end of the match is > 0
    # (This way missing players do not distort the minimum and the spread)
    point_cols = ["p1Points", "p2Points", "p3Points", "p4Points"]
    active_cols = [c for c in point_cols if work[c].max() > 0]

    if len(active_cols) < 2:
        active_cols = point_cols[:2]  # Fallback, if everyone is on 0 score

    work["score_max"] = work[active_cols].max(axis=1)
    work["score_min"] = work[active_cols].min(axis=1)
    work["score_spread"] = work["score_max"] - work["score_min"]

    sorted_scores = np.sort(work[active_cols].fillna(0).to_numpy(), axis=1)
    work["leader_margin"] = sorted_scores[:, -1] - sorted_scores[:, -2]

    if "normalized_time" not in work.columns:
        work["normalized_time"] = normalized_progress_from_time(work, "time")

    return work


def compute_lead_changes(skill_df: pd.DataFrame) -> float:
    if skill_df is None or skill_df.empty:
        return np.nan

    point_cols = [c for c in ["p1Points", "p2Points", "p3Points", "p4Points"] if c in skill_df.columns]
    if len(point_cols) < 2:
        return np.nan

    arr = skill_df[point_cols].fillna(0).to_numpy()
    leaders = np.argmax(arr, axis=1)
    if len(leaders) < 2:
        return 0.0

    changes = np.sum(leaders[1:] != leaders[:-1])
    return float(changes)


def compute_driver_swap_count(event_df: pd.DataFrame) -> float:
    if event_df is None or event_df.empty or "eventType" not in event_df.columns:
        return np.nan
    return float((event_df["eventType"] == "driver_swap").sum())


def compute_driver_streaks(skill_df: pd.DataFrame) -> Tuple[float, float]:
    if skill_df is None or skill_df.empty:
        return np.nan, np.nan
    if not {"time", "currentDriverPlayerNum"}.issubset(skill_df.columns):
        return np.nan, np.nan

    df = skill_df[["time", "currentDriverPlayerNum"]].dropna().copy()
    if df.empty:
        return np.nan, np.nan

    df["streak_change"] = df["currentDriverPlayerNum"].ne(df["currentDriverPlayerNum"].shift()).cumsum()
    streaks = df.groupby("streak_change").agg(
        start_time=("time", "min"),
        end_time=("time", "max"),
        driver=("currentDriverPlayerNum", "first")
    )
    streaks["duration"] = streaks["end_time"] - streaks["start_time"]

    if streaks.empty:
        return np.nan, np.nan

    return float(streaks["duration"].mean()), float(streaks["duration"].max())


def summarize_session(bundle: SessionBundle) -> Dict[str, float]:
    row = {
        "session_id": bundle.session_id,
        "condition": bundle.condition,
        "player_count": bundle.player_count,
    }

    skill_df = bundle.skill_df
    traj_df = bundle.trajectory_df
    event_df = bundle.event_df

    if skill_df is not None and not skill_df.empty:
        row["mean_driver_skill"] = skill_df["driverSkill"].mean() if "driverSkill" in skill_df.columns else np.nan
        row["mean_driver_conf"] = skill_df["driverConf"].mean() if "driverConf" in skill_df.columns else np.nan
        row["skill_volatility"] = skill_df["driverSkillDeltaAbs"].mean() if "driverSkillDeltaAbs" in skill_df.columns else np.nan
        row["mean_tenure01"] = skill_df["tenure01"].mean() if "tenure01" in skill_df.columns else np.nan
        row["max_tenure01"] = skill_df["tenure01"].max() if "tenure01" in skill_df.columns else np.nan

        spread_df = compute_score_spread_series(skill_df)
        if not spread_df.empty:
            row["final_score_spread"] = float(spread_df["score_spread"].iloc[-1])
            row["final_leader_margin"] = float(spread_df["leader_margin"].iloc[-1])

            early = spread_df[spread_df["normalized_time"] < 0.25]
            late = spread_df[spread_df["normalized_time"] > 0.75]

            row["early_score_spread_mean"] = float(early["score_spread"].mean()) if not early.empty else np.nan
            row["late_score_spread_mean"] = float(late["score_spread"].mean()) if not late.empty else np.nan
            row["lead_change_count"] = compute_lead_changes(skill_df)
        else:
            row["final_score_spread"] = np.nan
            row["final_leader_margin"] = np.nan
            row["early_score_spread_mean"] = np.nan
            row["late_score_spread_mean"] = np.nan
            row["lead_change_count"] = np.nan

        mean_streak, max_streak = compute_driver_streaks(skill_df)
        row["mean_driver_streak_duration"] = mean_streak
        row["max_driver_streak_duration"] = max_streak

        responsiveness_pairs = [
            ("driverSkill", "driverPrecision", "skill_precision"),
            ("driverSkill", "driverSpawn", "skill_spawn"),
            ("driverSkill", "driverPunishment", "skill_punishment"),
            ("driverSkill", "driverRewardBias", "skill_rewardBias"),
        ]
        for x, y, prefix in responsiveness_pairs:
            r, p, n = compute_spearman(skill_df, x, y)
            row[f"{prefix}_r"] = r
            row[f"{prefix}_p"] = p
            row[f"{prefix}_n"] = n

    if traj_df is not None and not traj_df.empty:
        row["mean_overshoot_ratio"] = traj_df["overshootRatio"].mean() if "overshootRatio" in traj_df.columns else np.nan
        row["mean_corner_smoothness"] = traj_df["cornerSmooth01"].mean() if "cornerSmooth01" in traj_df.columns else np.nan
        row["mean_lane_jitter"] = traj_df["laneJitter01"].mean() if "laneJitter01" in traj_df.columns else np.nan
        row["valid_corner_smooth_samples"] = traj_df[
            "cornerSmooth01"].notna().sum() if "cornerSmooth01" in traj_df.columns else np.nan
        row["valid_lane_jitter_samples"] = traj_df[
            "laneJitter01"].notna().sum() if "laneJitter01" in traj_df.columns else np.nan
        row["valid_overspeed_control_samples"] = traj_df[
            "overspeedControl01"].notna().sum() if "overspeedControl01" in traj_df.columns else np.nan
    row["driver_swap_count"] = compute_driver_swap_count(event_df)

    return row


# =========================
# PLOTS
# =========================

def plot_skill_time_series(bundle: SessionBundle) -> None:
    df = bundle.skill_df
    if df is None or df.empty:
        return
    required = {"time", "driverSkill", "driverPrecision", "driverSpawn"}
    if not required.issubset(df.columns):
        return

    plt.figure(figsize=(12, 6))
    sns.lineplot(data=df, x="time", y="driverSkill", label="Estimated Skill", linewidth=2, color="black")
    sns.lineplot(data=df, x="time", y="driverPrecision", label="Precision", linestyle="--")
    sns.lineplot(data=df, x="time", y="driverSpawn", label="Spawn Pressure", linestyle="-.")
    plt.xlabel("Session Time (s)")
    plt.ylabel("Normalized Value")
    plt.title(f"Skill Estimation and Difficulty Adaptation Over Time\nSession {bundle.session_id}")
    savefig_multi(os.path.join(PLOTS_DIR, f"{bundle.session_id}_skill_time_series"))


def plot_skill_confidence(bundle: SessionBundle) -> None:
    df = bundle.skill_df
    if df is None or df.empty:
        return
    required = {"time", "driverSkill", "driverConf"}
    if not required.issubset(df.columns):
        return

    plt.figure(figsize=(12, 6))
    sns.lineplot(data=df, x="time", y="driverSkill", label="Estimated Skill", linewidth=2, color="black")
    sns.lineplot(data=df, x="time", y="driverConf", label="Confidence", linestyle="--")
    plt.xlabel("Session Time (s)")
    plt.ylabel("Normalized Value")
    plt.title(f"Skill and Confidence Over Time\nSession {bundle.session_id}")
    savefig_multi(os.path.join(PLOTS_DIR, f"{bundle.session_id}_skill_confidence"))


def plot_overspeed_heatmap(bundle: SessionBundle) -> None:
    df = bundle.trajectory_df
    if df is None or df.empty:
        return
    required = {"lane", "track_segment", "overshootRatio"}
    if not required.issubset(df.columns):
        return

    heatmap_data = df.groupby(["lane", "track_segment"])["overshootRatio"].mean().unstack()

    plt.figure(figsize=(12, 5))
    sns.heatmap(heatmap_data, cmap="rocket_r", cbar_kws={"label": "Mean Overshoot Ratio"})
    plt.title(f"Overspeed Hotspots Across Track Segments and Lanes\nSession {bundle.session_id}")
    plt.xlabel("Track Segment")
    plt.ylabel("Lane")
    savefig_multi(os.path.join(PLOTS_DIR, f"{bundle.session_id}_overspeed_heatmap"))


def plot_crash_density(bundle: SessionBundle) -> None:
    df = bundle.event_df
    if df is None or df.empty:
        return
    required = {"trackT", "is_crash_event"}
    if not required.issubset(df.columns):
        return

    crashes = df[df["is_crash_event"] == True]
    if crashes.empty:
        return

    plt.figure(figsize=(10, 4))
    sns.histplot(data=crashes, x="trackT", bins=30, kde=True)
    plt.title(f"Crash Event Density Along Track\nSession {bundle.session_id}")
    plt.xlabel("Normalized Track Position")
    plt.ylabel("Crash Count")
    savefig_multi(os.path.join(PLOTS_DIR, f"{bundle.session_id}_crash_density"))


def plot_fairness_metric_boxplot(summary_df: pd.DataFrame, metric_col: str, title: str, ylabel: str,
                                 filename: str) -> None:
    if summary_df.empty or "condition" not in summary_df.columns or metric_col not in summary_df.columns:
        return

    df = summary_df.dropna(subset=["condition", metric_col])
    # Comparing only known conditions
    df = df[df["condition"].isin(["Baseline", "Adaptive"])]

    if df.empty or df["condition"].nunique() < 2:
        print(f"[WARN] Not enough condition data to run A/B plot for {metric_col}.")
        return

    baseline_data = df[df["condition"] == "Baseline"][metric_col]
    adaptive_data = df[df["condition"] == "Adaptive"][metric_col]

    # Mann-Whitney U test (Wilcoxon ranksum)
    stat, p_val = mannwhitneyu(baseline_data, adaptive_data, alternative='two-sided')

    plt.figure(figsize=(8, 6))

    # Boxplot és Swarmplot
    sns.boxplot(data=df, x="condition", y=metric_col, width=0.5, boxprops={'alpha': 0.8})
    sns.swarmplot(data=df, x="condition", y=metric_col, color="black", size=6)

    # Mean lines
    means = df.groupby("condition")[metric_col].mean()
    plt.plot([-0.25, 0.25], [means["Adaptive"], means["Adaptive"]], color='black', linestyle='--',
             linewidth=1.5) if "Adaptive" in means else None
    plt.plot([0.75, 1.25], [means["Baseline"], means["Baseline"]], color='black', linestyle='--',
             linewidth=1.5) if "Baseline" in means else None

    # Title with p value
    plt.title(f"{title}\np-value = {p_val:.4f} (Mann-Whitney U test)")
    plt.xlabel("Game Condition")
    plt.ylabel(ylabel)


    plt.ylim(bottom=0)

    savefig_multi(os.path.join(PLOTS_DIR, filename))


def plot_aggregated_crash_density(sessions: Dict[str, SessionBundle]) -> None:
    dfs = []
    for bundle in sessions.values():
        if bundle.event_df is not None and not bundle.event_df.empty and bundle.condition in ["Baseline", "Adaptive"]:
            crashes = bundle.event_df[bundle.event_df["is_crash_event"] == True].copy()
            crashes["condition"] = bundle.condition
            dfs.append(crashes)

    if not dfs: return
    all_crashes = pd.concat(dfs, ignore_index=True)

    plt.figure(figsize=(10, 5))
    sns.histplot(data=all_crashes, x="trackT", hue="condition", element="poly", stat="density", common_norm=False,
                 alpha=0.3, linewidth=2)
    plt.title("Aggregated Crash Density Along Track\n(Baseline vs Adaptive)")
    plt.xlabel("Normalized Track Position")
    plt.ylabel("Crash Density")
    plt.xlim(0, 1)
    savefig_multi(os.path.join(PLOTS_DIR, "ab_aggregated_crash_density"))


def plot_aggregated_overspeed_heatmap(sessions: Dict[str, SessionBundle]) -> None:
    dfs = []
    for bundle in sessions.values():
        if bundle.trajectory_df is not None and not bundle.trajectory_df.empty and bundle.condition in ["Baseline",
                                                                                                        "Adaptive"]:
            df = bundle.trajectory_df[["lane", "track_segment", "overshootRatio"]].copy()
            df["condition"] = bundle.condition
            dfs.append(df)

    if not dfs: return
    all_traj = pd.concat(dfs, ignore_index=True)

    for cond in ["Baseline", "Adaptive"]:
        cond_data = all_traj[all_traj["condition"] == cond]
        if cond_data.empty: continue
        heatmap_data = cond_data.groupby(["lane", "track_segment"])["overshootRatio"].mean().unstack()

        plt.figure(figsize=(12, 4))
        sns.heatmap(heatmap_data, cmap="rocket_r", cbar_kws={"label": "Mean Overshoot Ratio"})
        plt.title(f"Aggregated Overspeed Hotspots - {cond} Condition")
        plt.xlabel("Track Segment")
        plt.ylabel("Lane")
        savefig_multi(os.path.join(PLOTS_DIR, f"ab_aggregated_overspeed_heatmap_{cond.lower()}"))

# =========================
# TXT SUMMARY
# =========================

def write_summary_txt(summary_df: pd.DataFrame) -> None:
    out_path = os.path.join(OUT_DIR, "statistical_summary.txt")
    with open(out_path, "w", encoding="utf-8") as f:
        f.write("MASTER THESIS TELEMETRY SUMMARY\n")
        f.write("================================\n\n")

        f.write(f"Total sessions analyzed: {len(summary_df)}\n\n")

        if "player_count" in summary_df.columns:
            f.write("Sessions by player count:\n")
            counts = summary_df["player_count"].value_counts(dropna=False).sort_index()
            for pc, cnt in counts.items():
                f.write(f"  - {pc}: {cnt}\n")
            f.write("\n")

        f.write("--- CONTROLLER RESPONSIVENESS ---\n")
        for label, r_col, p_col in [
            ("Skill vs Precision", "skill_precision_r", "skill_precision_p"),
            ("Skill vs Spawn", "skill_spawn_r", "skill_spawn_p"),
            ("Skill vs Punishment", "skill_punishment_r", "skill_punishment_p"),
            ("Skill vs RewardBias", "skill_rewardBias_r", "skill_rewardBias_p"),
        ]:
            if r_col in summary_df.columns:
                mean_r = summary_df[r_col].mean()
                median_r = summary_df[r_col].median()
                mean_p = summary_df[p_col].mean() if p_col in summary_df.columns else np.nan
                f.write(f"{label}:\n")
                f.write(f"  Mean Spearman r:   {mean_r:.4f}\n" if not pd.isna(mean_r) else "  Mean Spearman r:   NaN\n")
                f.write(f"  Median Spearman r: {median_r:.4f}\n" if not pd.isna(median_r) else "  Median Spearman r: NaN\n")
                f.write(f"  Mean p-value:      {mean_p:.4e}\n\n" if not pd.isna(mean_p) else "  Mean p-value:      NaN\n\n")

        f.write("--- ESTIMATOR SUMMARY ---\n")
        for col in ["mean_driver_skill", "mean_driver_conf", "skill_volatility", "mean_tenure01", "max_tenure01"]:
            if col in summary_df.columns:
                f.write(f"{col}: mean={summary_df[col].mean():.4f}, median={summary_df[col].median():.4f}\n")
        f.write("\n")

        f.write("--- SPATIAL SUMMARY ---\n")
        for col in ["mean_overshoot_ratio", "mean_corner_smoothness", "mean_lane_jitter"]:
            if col in summary_df.columns:
                f.write(f"{col}: mean={summary_df[col].mean():.4f}, median={summary_df[col].median():.4f}\n")
        f.write("\n")

        f.write("--- FAIRNESS SUMMARY ---\n")
        fairness_cols = [
            "final_score_spread",
            "final_leader_margin",
            "early_score_spread_mean",
            "late_score_spread_mean",
            "lead_change_count",
            "mean_driver_streak_duration",
            "max_driver_streak_duration",
            "driver_swap_count",
        ]
        for col in fairness_cols:
            if col in summary_df.columns:
                valid = summary_df[col].dropna()
                if len(valid) > 0:
                    f.write(f"{col}: mean={valid.mean():.4f}, median={valid.median():.4f}\n")

        f.write("\n--- CONDITION COMPARISON (A/B TESTING) ---\n")
        if "condition" in summary_df.columns:
            ab_cols = ["final_score_spread", "lead_change_count", "driver_swap_count"]
            grouped = summary_df.groupby("condition")

            for cond, group in grouped:
                if cond == "Unknown": continue
                f.write(f"[{cond}]\n")
                f.write(f"  Sessions: {len(group)}\n")
                for col in ab_cols:
                    if col in group.columns:
                        f.write(f"  {col}: mean={group[col].mean():.2f}, std={group[col].std():.2f}\n")
                f.write("\n")
        f.write("\n")
        f.write("Note:\n")
        f.write("This summary is intentionally simplified for thesis use.\n")
        f.write("It generates only plots and this text report, without intermediate CSV exports.\n")


# =========================
# MAIN
# =========================

def main() -> None:
    print("=" * 60)
    print("MASTER THESIS TELEMETRY ANALYZER - AGGREGATED")
    print("=" * 60)

    ensure_dirs()

    sessions = discover_logs(CONFIG["input_dir"])
    sessions = load_all_sessions(sessions)

    print(f"[INFO] Sessions discovered: {len(sessions)}")

    summary_rows = []

    # 1. Collecting data per session
    for sid, bundle in sessions.items():
        summary_rows.append(summarize_session(bundle))

    summary_df = pd.DataFrame(summary_rows)

    # 2. Summarized A/B test plots generation
    print("[INFO] Generating aggregated plots...")

    plot_fairness_metric_boxplot(
        summary_df,
        metric_col="final_score_spread",
        title="Comparison of Final Score Spread",
        ylabel="Final Score Spread",
        filename="ab_final_score_spread"
    )

    plot_fairness_metric_boxplot(
        summary_df,
        metric_col="driver_swap_count",
        title="Comparison of Driver Swap Frequency",
        ylabel="Total Driver Swaps",
        filename="ab_driver_swaps"
    )

    plot_aggregated_crash_density(sessions)
    plot_aggregated_overspeed_heatmap(sessions)

    # 3. Plotting one representative session (to show how the controller works)
    adaptive_sessions = [b for b in sessions.values() if b.condition == "Adaptive"]
    if adaptive_sessions:
        representative = adaptive_sessions[0] # holds the first adaptive session
        plot_skill_time_series(representative)
        plot_skill_confidence(representative)
        print(f"[INFO] Representative session plots generated for: {representative.session_id}")

    # 4. text report
    write_summary_txt(summary_df)

    print("=" * 60)
    print(f"[DONE] Results saved to: {OUT_DIR}")
    print(f"[DONE] Plots folder:      {PLOTS_DIR}")
    print("=" * 60)

if __name__ == "__main__":
    main()