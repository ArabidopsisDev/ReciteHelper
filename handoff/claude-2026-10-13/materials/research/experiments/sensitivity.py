"""Research-only algebra checks; NOT an emotion/learning efficacy experiment.

No dependencies, participant data, model weights, network requests or C-drive output.
Run: python -B sensitivity.py
Outputs: sensitivity.csv and sensitivity-summary.json beside this script.
"""
import csv
import json
import math
from pathlib import Path


ROOT = Path(__file__).resolve().parent
if ROOT.drive.lower() != "d:":
    raise RuntimeError("Research outputs must remain on the D drive.")

# Parameter matches the inspected OpenRecite default. It is a fixed illustration,
# not a fitted parameter for a participant.
DECAY = 0.1542
FACTOR = math.pow(0.9, -1 / DECAY) - 1


def recall(days, stability):
    return math.pow(1 + FACTOR * days / stability, -DECAY)


def interval(stability, retention=0.9):
    return stability / FACTOR * (math.pow(retention, -1 / DECAY) - 1)


def adjusted_stability(stability, delta, available):
    # Experimental prior-event residual, held constant between real reviews.
    # Missing modality/model result MUST be exactly the unmodified baseline.
    return stability * math.exp(delta) if available else stability


def main():
    rows = []
    max_error = 0.0
    monotone = True
    for stability in (1.0, 10.0, 30.0):
        for delta in (-0.2, 0.0, 0.2):
            for available in (False, True):
                effective = adjusted_stability(stability, delta, available)
                days = interval(effective)
                error = abs(recall(days, effective) - 0.9)
                max_error = max(max_error, error)
                trace = [recall(t, effective) for t in (0, 1, 7, 30, 90)]
                monotone &= all(a >= b for a, b in zip(trace, trace[1:]))
                rows.append({
                    "baseline_stability_days": stability,
                    "hypothetical_delta": delta,
                    "signal_available": available,
                    "effective_stability_days": effective,
                    "interval_days": days,
                    "interval_ratio_to_baseline": days / interval(stability),
                    "recall_at_interval": recall(days, effective),
                })

    with (ROOT / "sensitivity.csv").open("w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)

    # Independent binary outcomes, normal approximation: planning lower bounds
    # before clustering, attrition, repeated looks and covariate adjustment.
    power = []
    for p0, p1 in ((0.80, 0.85), (0.80, 0.83), (0.90, 0.93)):
        pbar = (p0 + p1) / 2
        n = ((1.95996398454 * math.sqrt(2 * pbar * (1 - pbar))
              + 0.84162123357 * math.sqrt(p0 * (1 - p0) + p1 * (1 - p1)))
             / (p1 - p0)) ** 2
        power.append({"p0": p0, "p1": p1,
                      "approx_independent_observations_per_arm": math.ceil(n)})

    result = {
        "experiment_type": "deterministic sensitivity and algebra only",
        "uses_real_participants": False,
        "runs_emotion_llama": False,
        "predicts_actual_learning_effect": False,
        "default_fsrs_decay": DECAY,
        "delta_values_are_hypothetical_not_estimated": True,
        "missing_signal_exactly_recovers_baseline": all(
            row["effective_stability_days"] == row["baseline_stability_days"]
            for row in rows if not row["signal_available"]),
        "monotone_in_elapsed_time": bool(monotone),
        "max_inverse_error": max_error,
        "power_assumptions": "Two-sided alpha=.05, power=.80; independent binary outcomes; not participant counts; not final sample-size recommendation.",
        "planning_lower_bounds": power,
        "limitation": "Satisfying algebra does not establish emotional accuracy, causal effect, calibration or an appropriate delta bound. No memory-changing policy is authorized by these results.",
    }
    (ROOT / "sensitivity-summary.json").write_text(
        json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
