import numpy as np
import pandas as pd


def haversine_dist(lat1, lon1, lat2, lon2):
    R = 6_371_000.0
    l1, l2 = np.deg2rad(lat1), np.deg2rad(lat2)
    dphi = l2 - l1
    dlambda = np.deg2rad(lon2 - lon1)
    a = (np.sin(dphi / 2) ** 2 +
         np.cos(l1) * np.cos(l2) * np.sin(dlambda / 2) ** 2)
    return 2 * R * np.arctan2(np.sqrt(a), np.sqrt(1 - a))


def make_windows(df, w):
    out = pd.DataFrame(index=df.index)

    out[f"acc_mean_{w}s"] = df["acceleration"].rolling(w, 1).mean().bfill().ffill()
    out[f"jerk_mean_{w}s"] = df["jerk"].rolling(w, 1).mean().bfill().ffill()
    out[f"dist_sum_{w}s"] = df["dist"].rolling(w, 1).sum().bfill().ffill()

    # "Straightness" = line‑of‑sight / path distance in window
    def straightness(x):
        if x.sum() == 0:
            return 0.0
        i0, i1 = x.index[0], x.index[-1]
        direct = haversine_dist(df.loc[i0, "lat"], df.loc[i0, "lon"],
                                df.loc[i1, "lat"], df.loc[i1, "lon"])
        return direct / x.sum()

    out[f"straightness_{w}s"] = (df["dist"]
                                 .rolling(w, 1)
                                 .apply(straightness, raw=False)
                                 .bfill().ffill())
    return out


def tag_events(df):
    ev = np.full(len(df), "Normal", dtype=object)

    # Noise grows slightly with speed  → harder to “learn the rule”.
    sigma_acc = 0.6 + 0.03 * df["speed"]
    sigma_jerk = 0.6 + 0.03 * df["speed"]

    th_brake = -3.47 + np.random.normal(0,  sigma_acc)
    th_accel = 3.47 + np.random.normal(0,  sigma_acc)
    th_jerk = 5.56 + np.random.normal(0,  sigma_jerk)
    overspeed_ratio = 1.2 + np.random.normal(0, 0.08, len(df))

    m = df["acceleration"] < th_brake
    ev[m] = "HardBrake"

    m = (df["acceleration"] > th_accel) & (ev == "Normal")
    ev[m] = "HardAccel"

    m = (df["jerk"].abs() > th_jerk) & (ev == "Normal")
    ev[m] = "HighJerk"

    m = ((df["speed"] /
          (df["speed_limit"] + 1e-3)) > overspeed_ratio) & (ev == "Normal")
    ev[m] = "Speeding"

    return ev


def main():
    np.random.seed(42)

    N = 120_000
    dt = 1.0

    speeds = np.clip(np.random.normal(15, 5, size=N), 0, 60)

    # Heading change - heavy vehicles wiggle more at low speed, less at high
    heading_sigma = np.clip(30 - 0.4 * speeds, 5, 25)
    heading_changes = np.random.normal(0, heading_sigma)
    headings = np.mod(np.cumsum(heading_changes), 360)

    # Integrate displacement on a sphere
    lats = np.zeros(N)
    lons = np.zeros(N)
    lats[0], lons[0] = 45.0, 9.0 # arbitrary

    R = 6_371_000
    for i in range(1, N):
        d = speeds[i] * dt          # meters in this second
        br = np.deg2rad(headings[i])
        dlat = d * np.cos(br) / R
        dlon = d * np.sin(br) / (R * np.cos(np.deg2rad(lats[i - 1])))
        lats[i] = lats[i - 1] + np.rad2deg(dlat)
        lons[i] = lons[i - 1] + np.rad2deg(dlon)

    times = pd.date_range("2025-01-01", periods=N, freq=f"{int(dt * 1000)}ms")
    df = pd.DataFrame({
        "timestamp": times,
        "lat": lats,
        "lon": lons,
        "heading": headings,
        "speed": speeds
    })

    df["acceleration"] = df["speed"].diff().fillna(0) / dt
    df["jerk"] = df["acceleration"].diff().fillna(0) / dt

    df["dist"] = haversine_dist(df["lat"], df["lon"],
                                df["lat"].shift(), df["lon"].shift()).fillna(0)

    SEG_LEN = 500 # meters per road "zone"
    zone_id = (df["dist"].cumsum() // SEG_LEN).astype(int)
    zone_limits = np.random.choice([10, 15, 20, 25],
                                   size=zone_id.nunique(),
                                   p=[.2, .3, .3, .2])
    df["speed_limit"] = zone_limits[zone_id.values]

    for col in ["acceleration", "jerk"]:
        df[col] += np.random.normal(0, 0.2, N)

    feats = []

    for w in [2, 4, 8]:
        W = make_windows(df, w=w)
        df = pd.concat([df, W], axis=1)
        feats.extend(W.columns)

    df["heading_change"] = heading_changes
    for w in [2, 4, 8]:
        mean_col = f"head_mean_{w}s"
        var_col = f"head_var_{w}s"
        df[mean_col] = df["heading_change"].rolling(w, 1).mean().bfill().ffill()
        df[var_col] = df["heading_change"].rolling(w, 1).var().bfill().ffill()
        feats += [mean_col, var_col]

    tod_sec = (df["timestamp"].dt.hour * 3600 +
               df["timestamp"].dt.minute * 60 +
               df["timestamp"].dt.second)
    df["tod_sin"] = np.sin(2 * np.pi * tod_sec / 86_400)
    df["tod_cos"] = np.cos(2 * np.pi * tod_sec / 86_400)
    feats += ["tod_sin", "tod_cos"]

    for col in feats:
        mask = np.random.rand(N) < 0.2
        df.loc[mask, col] += np.random.normal(0, 0.5, mask.sum())

    df["EventType"] = tag_events(df)

    counts = df["EventType"].value_counts()
    min_x = counts[["HardAccel", "HardBrake"]].min()
    target_counts = {k: min_x for k in
                     ["HardAccel", "HardBrake", "HighJerk", "Normal", "Speeding"]}

    balanced = []
    for lbl, tgt in target_counts.items():
        subset = df[df["EventType"] == lbl]
        balanced.append(subset.sample(tgt, replace=len(subset) < tgt,
                                      random_state=42))
    df = pd.concat(balanced).sample(frac=1, random_state=42).reset_index(drop=True)

    drop_cols = ["timestamp", "lat", "lon", "heading",
                 "speed", "speed_limit", "acceleration", "jerk", "dist"]
    df = df.drop(columns=drop_cols)

    df.to_csv("telemetry.csv", index=False)
    print(f"Wrote telemetry.csv  ({df.shape[0]} rows)")
    print("Class distribution:\n", df["EventType"].value_counts())


if __name__ == "__main__":
    main()
