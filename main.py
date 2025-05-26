import math
import datetime
from typing import List

import numpy as np
import pandas as pd
from joblib import load
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field
from scipy.special import softmax
from scipy.optimize import minimize
from functools import partial

# ─── 1) Pydantic models ──────────────────────────────────────────────
class GpsPayload(BaseModel):
    latitude: float
    longitude: float
    timestamp: datetime.datetime

class PredictionResponse(BaseModel):
    predicted_event: str
    aggressive_score: float = Field(..., ge=0.0, le=1.0)
    proba: list[float]

# ─── 2) Utility functions (from generateTelemetry.py) ───────────────
def haversine_dist(lat1, lon1, lat2, lon2):
    R = 6_371_000.0
    l1, l2 = np.deg2rad(lat1), np.deg2rad(lat2)
    dphi = l2 - l1
    dlambda = np.deg2rad(lon2 - lon1)
    a = (np.sin(dphi / 2) ** 2 +
         np.cos(l1) * np.cos(l2) * np.sin(dlambda / 2) ** 2)
    return 2 * R * np.arctan2(np.sqrt(a), np.sqrt(1 - a))

def make_windows(df: pd.DataFrame, w: int) -> pd.DataFrame:
    out = pd.DataFrame(index=df.index)
    out[f"acc_mean_{w}s"] = df["acceleration"].rolling(w, 1).mean().bfill().ffill()
    out[f"jerk_mean_{w}s"] = df["jerk"].rolling(w, 1).mean().bfill().ffill()
    out[f"dist_sum_{w}s"]  = df["dist"].rolling(w, 1).sum().bfill().ffill()

    def straightness(x):
        if x.sum() == 0:
            return 0.0
        i0, i1 = x.index[0], x.index[-1]
        direct = haversine_dist(
            df.loc[i0, "lat"], df.loc[i0, "lon"],
            df.loc[i1, "lat"], df.loc[i1, "lon"]
        )
        return direct / x.sum()

    out[f"straightness_{w}s"] = (
        df["dist"]
          .rolling(w, 1)
          .apply(straightness, raw=False)
          .bfill().ffill()
    )
    return out

# ─── 3) TempScaler (must match your train script) ────────────────────
def _nll(proba: np.ndarray, y_true: np.ndarray) -> float:
    eps = 1e-9
    row_idx = np.arange(len(y_true))
    return -np.log(proba[row_idx, y_true] + eps).mean()

class TempScaler:
    def __init__(self, models):
        self.models = models
        self.T = 1.0

    def _avg_proba(self, X):
        return np.mean([m.predict_proba(X) for m in self.models], axis=0)

    def _avg_logits(self, X):
        eps = 1e-9
        return np.log(self._avg_proba(X) + eps)

    def fit(self, X_val, y_val):
        logits = self._avg_logits(X_val)
        obj = partial(
            lambda t, lg, y: _nll(softmax(lg / t, axis=1), y),
            lg=logits,
            y=y_val,
        )
        result = minimize(obj, x0=[1.0], bounds=[(0.05, 10.0)])
        self.T = float(result.x[0])
        return self

    def predict_proba(self, X):
        logits = self._avg_logits(X) / self.T
        return softmax(logits, axis=1)

    def predict(self, X):
        return self.predict_proba(X).argmax(axis=1)

# ─── 4) Feature‐builder maintains last N points ───────────────────────
class FeatureBuilder:
    def __init__(self, max_window: int = 8):
        self.max_window = max_window
        self.records: List[dict] = []

    def add_point(self, lat: float, lon: float, ts: datetime.datetime) -> pd.DataFrame:
        rec = {"lat": lat, "lon": lon, "timestamp": ts}
        if not self.records:
            # initialize dynamic fields to zero on first point
            rec.update(speed=0.0, acceleration=0.0,
                       jerk=0.0, dist=0.0,
                       heading=0.0, heading_change=0.0)
        else:
            prev = self.records[-1]
            dt = (ts - prev["timestamp"]).total_seconds() or 1.0
            d = haversine_dist(prev["lat"], prev["lon"], lat, lon)
            speed = d / dt
            acc   = (speed - prev["speed"]) / dt
            j     = (acc - prev["acceleration"]) / dt

            # compute bearing / heading
            y = math.sin(math.radians(lon - prev["lon"])) * math.cos(math.radians(lat))
            x = (math.cos(math.radians(prev["lat"])) * math.sin(math.radians(lat))
                 - math.sin(math.radians(prev["lat"])) * math.cos(math.radians(lat))
                   * math.cos(math.radians(lon - prev["lon"])))
            heading = (math.degrees(math.atan2(y, x)) + 360) % 360
            raw_delta = heading - prev["heading"]
            heading_change = (raw_delta + 180) % 360 - 180

            rec.update(speed=speed, acceleration=acc, jerk=j,
                       dist=d, heading=heading,
                       heading_change=heading_change)

        self.records.append(rec)
        if len(self.records) > self.max_window:
            self.records.pop(0)
        return pd.DataFrame(self.records)

# ─── 5) FastAPI app and startup hook ─────────────────────────────────
app = FastAPI(title="Aggressive‐Driver Predictor")

@app.on_event("startup")
def _load_artifacts():
    global builder, calibrator, label_encoder
    builder       = FeatureBuilder(max_window=8)
    calibrator    = load("temp_scal.pkl")
    label_encoder = load("label_encoder.pkl")

@app.post("/predict", response_model=PredictionResponse)
def predict(payload: GpsPayload):
    df = builder.add_point(payload.latitude, payload.longitude, payload.timestamp)

    if len(df) < 2:
        raise HTTPException(400, "need at least 2 GPS points to predict")

    # build all the same rolling and time-of-day features
    feats = df.copy()
    for w in (2, 4, 8):
        W = make_windows(df, w=w)
        for col in W.columns:
            feats[col] = W[col]
    for w in (2, 4, 8):
        feats[f"head_mean_{w}s"] = (
            df["heading_change"].rolling(w, 1).mean().bfill().ffill()
        )
        feats[f"head_var_{w}s"]  = (
            df["heading_change"].rolling(w, 1).var().bfill().ffill()
        )
    tod = (df["timestamp"].dt.hour * 3600 +
           df["timestamp"].dt.minute * 60 +
           df["timestamp"].dt.second)
    feats["tod_sin"] = np.sin(2 * np.pi * tod / 86_400)
    feats["tod_cos"] = np.cos(2 * np.pi * tod / 86_400)

    # only the newest row goes to the model
    X = feats.iloc[[-1]].drop(
        columns = [
            "lat", "lon", "timestamp",
            "speed", "acceleration", "jerk", "dist", "heading"
        ],
        errors = "ignore"
    )

    proba = calibrator.predict_proba(X.values)[0]
    idx_n = list(label_encoder.classes_).index("Normal")
    score = float(1.0 - proba[idx_n])
    lbl   = label_encoder.inverse_transform([proba.argmax()])[0]

    print(f"[predict] lbl={lbl!r}, score={score:.3f}, proba={proba.tolist()}")

    return PredictionResponse(predicted_event=lbl, aggressive_score=score, proba=proba.tolist())

# ─── 6) Optional “python main.py” entry point ───────────────────────
if __name__ == "__main__":
    import uvicorn
    uvicorn.run("main:app", host="0.0.0.0", port=8000, reload=True)
