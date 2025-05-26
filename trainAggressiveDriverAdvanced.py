import io, sys, warnings, gc, datetime, json, pathlib, joblib, optuna
import numpy as np, pandas as pd, matplotlib.pyplot as plt, seaborn as sns
from optuna.pruners import HyperbandPruner
from scipy.special import softmax
from sklearn.preprocessing import LabelEncoder
from sklearn.utils.class_weight import compute_class_weight
from sklearn.model_selection import StratifiedKFold
from sklearn.metrics import f1_score, confusion_matrix, classification_report, accuracy_score
from xgboost import XGBClassifier
from scipy.optimize import minimize
from functools import partial


warnings.filterwarnings("ignore")
sns.set_style("whitegrid")


RANDOM_STATE = 42
CV_FOLDS     = 5
N_TRIALS     = 30
OPTUNA_JOBS  = 4


def load_data(path="telemetry.csv"):
    df = pd.read_csv(path)
    return df.drop(columns=["EventType"]), df["EventType"]


def plot_conf_matrix(y_true, y_pred, labels, title, cmap="Blues"):
    cm = confusion_matrix(y_true, y_pred, labels=range(len(labels)))

    ax  = sns.heatmap(cm, annot=True, fmt="d",
                      xticklabels=labels, yticklabels=labels,
                      cmap=cmap)

    ax.set(title=title, xlabel="Predicted", ylabel="Actual")
    ax.figure.tight_layout()
    ax.figure.savefig(f"{title.replace(' ', '_').lower()}.png")
    plt.close(ax.figure)


def plot_final_f1(train_oof, valid, test):
    fig, ax = plt.subplots(figsize=(5, 4))
    ax.bar(["Train (OOF)", "Valid", "Test"], [train_oof, valid, test])
    ax.set(ylim=(0, 1), ylabel="F1", title="Final F1 scores")
    fig.tight_layout()
    fig.savefig("final_f1.png")
    plt.close(fig)


def plot_separate_learning_curves(sizes, oof_curve, va_curve, te_curve):
    # Plotting Train OOF Learning Curve
    plt.figure(figsize=(8, 6))
    plt.plot(sizes, oof_curve, marker='o')
    plt.title("Learning Curve – Train OOF")
    plt.xlabel("Fraction of training data used")
    plt.ylabel("Weighted F1")
    plt.ylim(0, 1)
    plt.grid(True, linestyle='--', alpha=0.6)
    plt.tight_layout()
    plt.savefig("learning_curve_oof.png")
    plt.close()

    # Plotting Validation Learning Curve
    plt.figure(figsize=(8, 6))
    plt.plot(sizes, va_curve, marker='o')
    plt.title("Learning Curve – Validation")
    plt.xlabel("Fraction of training data used")
    plt.ylabel("Weighted F1")
    plt.ylim(0, 1)
    plt.grid(True, linestyle='--', alpha=0.6)
    plt.tight_layout()
    plt.savefig("learning_curve_validation.png")
    plt.close()

    # Plotting Test Learning Curve
    plt.figure(figsize=(8, 6))
    plt.plot(sizes, te_curve, marker='o')
    plt.title("Learning Curve – Test")
    plt.xlabel("Fraction of training data used")
    plt.ylabel("Weighted F1")
    plt.ylim(0, 1)
    plt.grid(True, linestyle='--', alpha=0.6)
    plt.tight_layout()
    plt.savefig("learning_curve_test.png")
    plt.close()


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


def make_objective(X, y, row_w):
    uniq_y = np.unique(y)
    kfold = StratifiedKFold(CV_FOLDS, shuffle=True, random_state=RANDOM_STATE)

    def obj(trial):
        # ─── hyper‑parameter space ──────────────────────────────────────
        params = {
            "tree_method"      : "hist",
            "n_estimators"     : trial.suggest_int   ("n_estimators",   900, 1600, step=100),
            "max_depth"        : trial.suggest_int   ("max_depth",        6,    9),
            "learning_rate"    : trial.suggest_float("lr",             0.06, 0.12, log=True),
            "subsample"        : trial.suggest_float("subsample",      0.70, 0.85),
            "colsample_bytree" : trial.suggest_float("col_bt",         0.75, 0.92),
            "min_child_weight" : trial.suggest_float("mcw",             1.0,  6.0, log=True),
            "gamma"            : trial.suggest_float("gamma",          0.05, 0.40),
            "reg_alpha"        : trial.suggest_float("alpha",           0.0,  1.5),
            "reg_lambda"       : trial.suggest_float("lambda",          1.0,  5.0),
            "grow_policy"      : trial.suggest_categorical("grow", ["depthwise", "lossguide"]),
            "objective"        : "multi:softprob",
            "num_class"        : len(uniq_y),
            "random_state"     : RANDOM_STATE,
            "n_jobs"           : -1,
            "eval_metric"      : "mlogloss",
            "early_stopping_rounds": 50,
        }

        # ─── cross‑validated weighted F1 ────────────────────────────────
        fold_scores = []
        for tr_idx, va_idx in kfold.split(X, y):
            model = XGBClassifier(**params)
            model.fit(
                X.iloc[tr_idx],
                y[tr_idx],
                sample_weight=row_w[y[tr_idx]],
                eval_set=[(X.iloc[va_idx], y[va_idx])],
                verbose=False,
            )

            fold_scores.append(
                f1_score(y[va_idx], model.predict(X.iloc[va_idx]), average="weighted")
            )

            # report to Optuna for pruning
            trial.report(np.mean(fold_scores), step=model.best_iteration)
            if trial.should_prune():
                raise optuna.TrialPruned()

        return np.mean(fold_scores)

    return obj

def main():
    # ── capture everything that gets printed ────────────────────────────
    capture_buf = io.StringIO()
    sys_stdout_orig = sys.stdout
    sys.stdout = capture_buf

    X,y_raw=load_data()
    le=LabelEncoder().fit(y_raw)
    y=le.transform(y_raw)
    N=len(X)
    t,v=int(.7*N), int(.85*N)
    X_tr,y_tr=X.iloc[:t],y[:t]
    X_va,y_va=X.iloc[t:v],y[t:v]
    X_te,y_te=X.iloc[v:],y[v:]
    cls_w = compute_class_weight('balanced',classes=np.unique(y),y=y)

    study = optuna.create_study(
        direction='maximize',
        sampler=optuna.samplers.TPESampler(
            multivariate=True,
            seed=RANDOM_STATE
        ),
        pruner=HyperbandPruner(
            min_resource=200,
            max_resource=1600,
            reduction_factor=3
        )
    )
    study.optimize(
        make_objective(X_tr,y_tr,cls_w),
        n_trials=N_TRIALS,
        n_jobs=OPTUNA_JOBS,
        show_progress_bar=False
    )
    topK = 3
    best_trials = sorted(
        study.trials,
        key=lambda t: t.value,
        reverse=True
    )[:topK]

    print(f"Optuna best OOF‑CV F1: {study.best_value:.4f}  |  "f"building ensemble from top‑{topK} trials")

    # ── 5‑fold × top‑K parameter ensembles – accumulate probas ──
    cv = StratifiedKFold(n_splits=CV_FOLDS, shuffle=True, random_state=RANDOM_STATE)
    n_class = len(le.classes_)

    models = []
    oof_probas = np.zeros((len(y_tr), n_class), dtype=float)

    for trial in best_trials:
        bp = dict(trial.params,
                  objective='multi:softprob',
                  num_class=n_class,
                  n_jobs=-1,
                  random_state=RANDOM_STATE)

        for tr_idx, va_idx in cv.split(X_tr, y_tr):
            mdl = XGBClassifier(
                **bp,
                eval_metric='mlogloss',
                early_stopping_rounds=80
            )
            mdl.fit(
                X_tr.iloc[tr_idx], y_tr[tr_idx],
                sample_weight=cls_w[y_tr[tr_idx]],
                eval_set=[(X_tr.iloc[va_idx], y_tr[va_idx])],
                verbose=False
            )

            # accumulate *probabilities* (later averaged)
            oof_probas[va_idx] += mdl.predict_proba(X_tr.iloc[va_idx])
            models.append(mdl)

    oof_probas /= (topK)
    oof_preds = oof_probas.argmax(1)

    # ── Train‑OOF report & confusion matrix
    print("\n[Train (OOF)]")
    print(classification_report(y_tr,oof_preds,target_names=le.classes_,digits=4))
    plot_conf_matrix(y_tr,oof_preds,le.classes_,"Train (OOF) Confusion")
    f1_tr=f1_score(y_tr,oof_preds,average='weighted')

    # ── Calibration on validation slice
    calib=TempScaler(models).fit(X_va,y_va)

    def report(X_,y_,tag):
        yp=calib.predict(X_)
        print(f"\n[{tag}]")
        print(classification_report(y_,yp,target_names=le.classes_,digits=4))
        plot_conf_matrix(y_,yp,le.classes_,f"{tag} Confusion")
        return f1_score(y_,yp,average='weighted')

    f1_va=report(X_va,y_va,"Valid")
    f1_te=report(X_te,y_te,"Test")
    plot_final_f1(f1_tr,f1_va,f1_te)

    # ── Unified learning‑curve  (Train‑OOF, Valid, Test) ────────────────────
    sizes = np.linspace(0.1, 1.0, 10)

    oof_curve, va_curve, te_curve = [], [], []

    best_bp = dict(best_trials[0].params,
                   objective='multi:softprob',
                   num_class=n_class,
                   n_jobs=-1,
                   random_state=RANDOM_STATE)

    for frac in sizes:
        n = int(len(X_tr) * frac)

        cv = StratifiedKFold(n_splits=CV_FOLDS, shuffle=True,
                             random_state=RANDOM_STATE)
        oof = np.zeros((n, n_class))
        y_sub = y_tr[:n]
        X_sub = X_tr.iloc[:n]

        for tr_idx, va_idx in cv.split(X_sub, y_sub):
            m = XGBClassifier(**best_bp, early_stopping_rounds=None)
            m.fit(X_sub.iloc[tr_idx], y_sub[tr_idx],
                  sample_weight=cls_w[y_sub[tr_idx]],
                  verbose=False)

            oof[va_idx] = m.predict_proba(X_sub.iloc[va_idx])

        oof_pred = oof.argmax(1)
        oof_curve.append(f1_score(y_sub, oof_pred, average='weighted'))

        m_full = XGBClassifier(**best_bp, early_stopping_rounds=None)
        m_full.fit(X_sub, y_sub, sample_weight=cls_w[y_sub], verbose=False)

        va_curve.append(f1_score(y_va, m_full.predict(X_va), average='weighted'))
        te_curve.append(f1_score(y_te, m_full.predict(X_te), average='weighted'))

    plt.figure(figsize=(8, 6))
    plt.plot(sizes, oof_curve, marker='o', label='Train OOF F1')
    plt.plot(sizes, va_curve, marker='o', label='Validation F1')
    plt.plot(sizes, te_curve, marker='o', label='Test F1')

    plot_separate_learning_curves(sizes, oof_curve, va_curve, te_curve)

    acc_va = accuracy_score(y_va, calib.predict(X_va))
    acc_te = accuracy_score(y_te, calib.predict(X_te))

    print("\n================    FINAL ACCURACIES   ================")
    print(f"Validation : {acc_va:0.4f}")
    print(f"Test       : {acc_te:0.4f}")
    print("========================================================\n")

    sys.stdout = sys_stdout_orig

    log_dir = pathlib.Path("run_logs")
    log_dir.mkdir(exist_ok=True)

    run_file = log_dir / f"run_{datetime.datetime.now():%Y%m%d_%H%M%S}.txt"
    run_file.write_text(capture_buf.getvalue(), encoding="utf-8")

    summary_line = (
        f"{datetime.datetime.now():%Y-%m-%d %H:%M:%S}\t"
        f"OOF_F1={f1_tr:0.4f}\tValid={acc_va:0.4f}\tTest={acc_te:0.4f}\t"
        f"Best_F1={study.best_value:0.4f}\t"
        f"Best_params={json.dumps(study.best_params, separators=(',', ':'))}"
    )
    (log_dir / "run_summary.tsv").open("a", encoding="utf-8").write(summary_line + "\n")

    (log_dir / "best_params.json").write_text(
        json.dumps(study.best_params, indent=2), encoding="utf-8"
    )

    joblib.dump(models, "xgb_folds.pkl")
    joblib.dump(calib, "temp_scal.pkl")
    joblib.dump(le, "label_encoder.pkl")
    print("\n💾 models & scaler saved")
    gc.collect()

if __name__=="__main__":
    main()
