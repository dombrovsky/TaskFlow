"""Combine BenchmarkDotNet Markdown reports for GitHub Actions and local viewing."""

import argparse
import os
from pathlib import Path


MAX_SUMMARY_BYTES = 900_000  # Leave room below GitHub's 1 MiB per-step limit.


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--results", type=Path, default=Path("BenchmarkDotNet.Artifacts/results"))
    parser.add_argument("--output", type=Path, default=Path("BenchmarkDotNet.Artifacts/summary.md"))
    args = parser.parse_args()
    mode = os.environ.get("BENCHMARK_RUN_MODE", "measured")
    outcome = os.environ.get("BENCHMARK_OUTCOME", "unknown")
    # Values are supplied by Actions, rather than interpolated into shell source.
    commit = os.environ.get("BENCHMARK_COMMIT", os.environ.get("GITHUB_SHA", "local"))
    if mode not in {"smoke", "measured"} or outcome not in {"success", "failure", "skipped", "cancelled", "unknown"}:
        raise ValueError("Unexpected benchmark mode or outcome")
    if commit != "local" and (len(commit) != 40 or any(char not in "0123456789abcdef" for char in commit)):
        raise ValueError("Expected a full Git commit SHA")

    text = f"# Benchmark results\n\nTested commit: `{commit}`  \nBenchmark step outcome: **{outcome}**\n\n"
    if mode == "smoke":
        text += "> **Smoke run (Dry): these timings and allocations are not performance evidence.**\n\n"
    else:
        text += "> **Measured run:** shared-runner noise limits comparisons; this is not a release-baseline comparison.\n\n"
    text += "Times and allocations are per operation where reported; batch cases normalize 1,000 operations. "
    text += "See the benchmark README for workload boundaries. Download `benchmark-results` for full JSON, CSV, HTML, and logs.\n\n"
    reports = sorted(args.results.glob("*-report-github.md"))
    if not reports:
        text += "No benchmark reports were generated. Check the restore, build, validation, and benchmark step logs.\n"
    for report in reports:
        title = report.name.removesuffix("-report-github.md")
        section = f"## {title}\n\n{report.read_text(encoding='utf-8').strip()}\n\n"
        if len((text + section).encode("utf-8")) > MAX_SUMMARY_BYTES:
            text += "Additional tables were omitted to fit the job-summary limit; see the downloadable reports.\n"
            break
        text += section
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(text, encoding="utf-8")
    if summary_path := os.environ.get("GITHUB_STEP_SUMMARY"):
        with Path(summary_path).open("a", encoding="utf-8") as summary:
            summary.write(text)


if __name__ == "__main__":
    main()