"""Advisory same-run comparison using the common measured-result contract."""

import argparse
import json
import math
import os
from pathlib import Path
import re
import subprocess
import traceback

from benchmark_report import RELEASE, write_json
from benchmark_run import ROOT, git, measure


REPRESENTATIVE = ["*BurstBenchmarks*", "*SequentialBenchmarks.SyncCompletion*"]


def delta(before, after):
    if before is None or after is None:
        return "unsupported / missing"
    absolute = after - before
    percent = f"{absolute / before * 100:+.1f}%" if before else "percentage unavailable (zero baseline)"
    return f"{absolute:+.2f} ({percent})"


def evidence(before, after):
    if min(before["n"], after["n"]) < 6:
        return "insufficient evidence: fewer than six retained samples"
    cv = max(before["stddev_ns"] / before["mean_ns"], after["stddev_ns"] / after["mean_ns"])
    # Conservative heuristic, not a hypothesis test or a regression gate.
    uncertainty = 3 * math.hypot(before["error_ns"], after["error_ns"])
    change = after["mean_ns"] - before["mean_ns"]
    if cv > 0.15 or abs(change) <= uncertainty:
        return "insufficient evidence: variability / overlapping uncertainty"
    if abs(change / before["mean_ns"]) >= 0.20:
        return "material slowdown (advisory)" if change > 0 else "material speedup (advisory)"
    return "below 20% reporting heuristic; advisory"


def render(report):
    text = "# Advisory same-run performance comparison\n\n"
    text += f"Baseline selection: `{report['selection']}`  \nBaseline: `{report['baseline_sha']}`  \nCandidate: `{report['candidate_sha']}`\n\n"
    text += f"Mode: **{report['mode']}**; scope: **{report['scope']}**; execution order: baseline then candidate.\n\n"
    text += "Three warmups, eight measurements, one launch; pinned SDK/runtime; independent build/raw directories; one runner. "
    text += "The 20% heuristic is advisory, not a statistical significance test or a required status gate. "
    text += "Shared-runner noise and baseline-first temporal drift remain limitations. Repeat before attribution.\n\n"
    incompatible = report.get("incompatible_configuration")
    if incompatible:
        text += f"**INCOMPARABLE: {incompatible}. No deltas are reported.**\n\n"
    for side in ["baseline", "candidate"]:
        value = report.get(side)
        if value:
            p = value["provenance"]
            text += f"{side}: harness `{p['harness_hash']}`, series `{value['series']}`, SDK {p['settings']['sdk']}, runtime {p['settings']['runtime']}.\n\n"
        if error := report.get(f"{side}_failure"):
            text += f"**{side} infrastructure failure: {error}. No valid comparison for failed cases.**\n\n"
    text += "| Case | Baseline ns/op ± SD | Candidate ns/op ± SD | Δ ns/op (%) | Baseline / candidate ops/s | Δ ops/s (%) | Baseline / candidate bytes/op | Δ bytes/op (%) | Evidence |\n"
    text += "|---|---:|---:|---:|---:|---:|---:|---:|---|\n"
    baseline = report.get("baseline", {}).get("cases", {})
    candidate = report.get("candidate", {}).get("cases", {})
    for key in sorted(baseline.keys() | candidate.keys()):
        a, b = baseline.get(key), candidate.get(key)
        if not a or not b or incompatible:
            text += f"| {key} | {'measured' if a else 'missing / unsupported'} | {'measured' if b else 'missing / unsupported'} | — | — | — | — | — | incomparable: added, removed, unsupported or failed |\n"
            continue
        text += (f"| {key} | {a['mean_ns']:.2f} ± {a['stddev_ns']:.2f} | {b['mean_ns']:.2f} ± {b['stddev_ns']:.2f} | "
                 f"{delta(a['mean_ns'], b['mean_ns'])} | {a['ops_s']:.2f} / {b['ops_s']:.2f} | {delta(a['ops_s'], b['ops_s'])} | "
                 f"{a['allocated_bytes']} / {b['allocated_bytes']} | {delta(a['allocated_bytes'], b['allocated_bytes'])} | {evidence(a, b)} |\n")
    text += "\n## Compatibility and exclusions\n\n"
    for exclusion in report["exclusions"]:
        text += f"- {exclusion}\n"
    text += "\nEnqueue-only allocations are unsupported. Unselected cases are excluded, never inferred. "
    text += "Download **benchmark-comparison** from this run's Artifacts for `comparison.json`, exact provenance, summaries and both raw BenchmarkDotNet JSON trees.\n"
    if run_url := os.environ.get("GITHUB_SERVER_URL"):
        text += f"\n[Run and downloadable artifacts]({run_url}/{os.environ['GITHUB_REPOSITORY']}/actions/runs/{os.environ['GITHUB_RUN_ID']})\n"
    return text


def supports_middleware(sha):
    files = git("ls-tree", "-r", "--name-only", sha, "TaskFlow/Middleware").splitlines()
    return any(path.endswith("ITaskSchedulerCompletionMiddleware.cs") for path in files)


def supports_enqueue(sha):
    source = git("show", f"{sha}:TaskFlow/ITaskScheduler.cs")
    return "object? state" in source and "Task<T> Enqueue<T>" in source


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mode", choices=["merge-base", "release"], default="release")
    parser.add_argument("--baseline", default="1.0.0")
    parser.add_argument("--target", default="origin/main")
    parser.add_argument("--candidate", default="HEAD")
    parser.add_argument("--scope", choices=["representative", "full"], default="representative")
    parser.add_argument("--output", required=True)
    parser.add_argument("--validate-slowdown", action="store_true", help="Validation only: inject temporary SpinWait into candidate worktree, never commit or publish")
    args = parser.parse_args()
    output = Path(args.output).resolve()
    output.mkdir(parents=True, exist_ok=False)
    candidate = git("rev-parse", "--verify", "--end-of-options", f"{args.candidate}^{{commit}}")
    if args.mode == "merge-base":
        target = git("rev-parse", "--verify", "--end-of-options", f"{args.target}^{{commit}}")
        baseline = git("merge-base", candidate, target)
        selection = f"merge-base({candidate}, {target})"
    else:
        ref = RELEASE if args.baseline == "1.0.0" else args.baseline
        if not re.fullmatch(r"[0-9a-f]{40}", ref):
            ref = f"refs/tags/{ref}"
        baseline = git("rev-parse", "--verify", f"{ref}^{{commit}}")
        selection = f"release {args.baseline} => {baseline}"
    exclusions = ["Keyed scheduling and bounded admission cases are outside this release-compatible harness; no 1.0.0 baseline exists for these newer APIs."]
    common_middleware = supports_middleware(baseline) and supports_middleware(candidate)
    omit = [] if common_middleware else ["MiddlewareBenchmarks.cs"]
    if omit:
        exclusions.append("All five MiddlewareBenchmarks.PipelineCompletion(Layers: 0/1/2/4/8) cases unsupported on at least one side; omitted from BOTH harness builds.")
    filters = REPRESENTATIVE if args.scope == "representative" else ["*"]
    if args.scope == "representative":
        exclusions.append("Representative scope selects Burst.SubmitAndDrain and all three SyncCompletion caller tokens; AsyncCompletion, producer, enqueue-only and middleware cases excluded. Choose full for all compatible cases.")
    report = {"mode": args.mode, "selection": selection, "baseline_sha": baseline,
              "candidate_sha": candidate, "scope": args.scope, "exclusions": exclusions}
    if args.validate_slowdown:
        report["exclusions"].append("SYNTHETIC VALIDATION ONLY: candidate Enqueue has an uncommitted SpinWait(5000), recorded as a patch in provenance; not a production performance claim.")
    failed = False
    try:
        for side, sha in [("baseline", baseline), ("candidate", candidate)]:
            if not supports_enqueue(sha):
                report["exclusions"].append(f"{side} {sha}: all common cases unsupported: missing the Task<T> Enqueue(delegate, state, token) API. No semantic adapter or timing comparison is invented.")
                continue
            try:
                synthetic = args.validate_slowdown and side == "candidate"
                report[side] = measure(sha, output / side, f"{side}: {sha}", sha == RELEASE and not synthetic,
                                       filters=filters, omit=omit, synthetic_delay=synthetic,
                                       release_tag=args.baseline if side == "baseline" and args.mode == "release" else None)
            except Exception as error:
                report[f"{side}_failure"] = f"{type(error).__name__}: {error}"
                traceback.print_exc()
                failed = True
        if report.get("baseline") and report.get("candidate"):
            if report["baseline"]["series"] != report["candidate"]["series"]:
                report["incompatible_configuration"] = "environment or harness changed between sequential measurements"
                raise ValueError("Environment or harness changed between sequential measurements")
    finally:
        write_json(output / "comparison.json", report)
        summary = render(report)
        (output / "summary.md").write_text(summary)
        if path := os.environ.get("GITHUB_STEP_SUMMARY"):
            with Path(path).open("a") as stream:
                stream.write(summary)
    if failed:
        raise SystemExit(1)  # Visible infrastructure failure; never fail on a performance delta.


if __name__ == "__main__":
    main()