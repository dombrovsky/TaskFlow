"""Run the fixed release-compatible harness in an independent checkout; never publish."""

import argparse
import datetime
import fnmatch
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess

from benchmark_report import (EXPECTED, PRODUCTION, RELEASE, RELEASE_CHECKOUT, SCHEMA, SETTINGS,
                              digest, history_summary, read_cases, write_json)


ROOT = Path(__file__).resolve().parents[1]


def command(*args, cwd=ROOT, capture=False):
    result = subprocess.run(args, cwd=cwd, check=True, text=True,
                            stdout=subprocess.PIPE if capture else None)
    return result.stdout.strip() if capture else None


def git(*args):
    return command("git", *args, capture=True)


def tree(sha):
    return {path: (entry.split()[2] if (entry := git("ls-tree", sha, "--", path)) else None)
            for path in PRODUCTION}


def environment(cwd):
    files = ["/sys/fs/cgroup/cpu.max", "/sys/fs/cgroup/cpuset.cpus.effective", "/proc/cpuinfo"]
    info = {path: Path(path).read_text() for path in files if Path(path).exists()}
    return {"platform": platform.platform(), "machine": platform.machine(), "cpu_count": os.cpu_count(),
            "affinity": sorted(os.sched_getaffinity(0)) if hasattr(os, "sched_getaffinity") else None,
            "runner": {key: os.environ.get(key) for key in ["RUNNER_NAME", "RUNNER_OS", "RUNNER_ARCH", "ImageOS", "ImageVersion"]},
            "cpu": info, "dotnet_info": command("dotnet", "--info", cwd=cwd, capture=True),
            "dotnet_variables": {k: v for k, v in os.environ.items() if k.startswith(("DOTNET_", "COMPlus_"))}}


def measure(source, output, label, release=False, filters=None, omit=None, synthetic_delay=False, release_tag=None):
    output = Path(output).resolve()
    output.mkdir(parents=True, exist_ok=False)
    sha = git("rev-parse", "--verify", "--end-of-options", f"{source}^{{commit}}")
    if release and tree(sha) != tree(RELEASE):
        raise ValueError("Refusing 1.0.0 label: production tree does not match release")
    if release and synthetic_delay:
        raise ValueError("Synthetic validation cannot be a release measurement")
    write_json(output / "global.json", {"sdk": {"version": SETTINGS["sdk"], "rollForward": "disable"}})
    sdk = command("dotnet", "--version", cwd=output, capture=True)
    runtimes = command("dotnet", "--list-runtimes", capture=True)
    if sdk != SETTINGS["sdk"] or "Microsoft.NETCore.App 10.0.0 " not in runtimes:
        raise ValueError("Requires SDK 10.0.100 and runtime 10.0.0")
    os.environ["DOTNET_PROCESSOR_COUNT"] = "2"
    os.environ["DOTNET_ROLL_FORWARD"] = "Disable"
    os.environ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
    checkout = output / "checkout"
    command("git", "worktree", "add", "--detach", str(checkout), sha)
    try:
        # Overlay only this one immutable common harness, never the candidate's harness.
        harness_files = sorted(p for p in (ROOT / "TaskFlow.Benchmarks").iterdir()
                               if (p.suffix in {".cs", ".csproj", ".props"} or p.name == ".editorconfig")
                               and p.name not in (omit or []))
        harness = {p.name: p.read_text() for p in harness_files}
        shutil.rmtree(checkout / "TaskFlow.Benchmarks", ignore_errors=True)
        (checkout / "TaskFlow.Benchmarks").mkdir()
        for name, content in harness.items():
            (checkout / "TaskFlow.Benchmarks" / name).write_text(content)
        synthetic_patch = None
        if synthetic_delay:
            # Validation only: this isolated worktree is removed in finally, never committed.
            source_file = checkout / "TaskFlow/TaskFlow.cs"
            content = source_file.read_text()
            needle = "            Argument.NotNull(taskFunc);"
            if content.count(needle) != 1:
                raise ValueError("Synthetic validation requires the known release implementation")
            source_file.write_text(content.replace(needle, "            System.Threading.Thread.SpinWait(5000);\n" + needle))
            synthetic_patch = command("git", "diff", "--", "TaskFlow/TaskFlow.cs", cwd=checkout, capture=True)
        write_json(checkout / "global.json", {"sdk": {"version": SETTINGS["sdk"], "rollForward": "disable"}})
        # Runtime pin also applies to BenchmarkDotNet's generated executables through inherited env.
        raw = output / "raw"
        raw.mkdir()
        env = environment(checkout)
        provenance = {"label": label, "measured_sha": sha, "production_tree": tree(sha),
                      "release_source_sha": sha if release or release_tag else None,
                      "release_tag": "1.0.0" if release else release_tag,
                      "release_tree_verified": release, "harness_sha": git("rev-parse", "HEAD"),
                      "synthetic_validation_patch": synthetic_patch,
                      "harness_hash": digest(harness), "settings": SETTINGS, "environment": env,
                      "timestamp": datetime.datetime.now(datetime.timezone.utc).isoformat(),
                      "run_id": os.environ.get("GITHUB_RUN_ID", "local"),
                      "attempt": os.environ.get("GITHUB_RUN_ATTEMPT", "1"),
                      "run_url": (f"https://github.com/{os.environ['GITHUB_REPOSITORY']}/actions/runs/{os.environ['GITHUB_RUN_ID']}"
                                  if "GITHUB_RUN_ID" in os.environ else None),
                      "selection": source, "filters": filters or ["*"], "omitted_harness_files": omit or []}
        write_json(output / "provenance.json", provenance)
        for args in [("restore", "TaskFlow.Benchmarks/TaskFlow.Benchmarks.csproj"),
                     ("build", "TaskFlow.Benchmarks/TaskFlow.Benchmarks.csproj", "-c", "Release", "--no-restore"),
                     ("run", "--project", "TaskFlow.Benchmarks", "-c", "Release", "--no-build", "--", "--validate"),
                     ("run", "--project", "TaskFlow.Benchmarks", "-c", "Release", "--no-build", "--", "--filter", *(filters or ["*"]),
                      "--warmupCount", "3", "--iterationCount", "8", "--launchCount", "1", "--artifacts", str(raw))]:
            command("dotnet", *args, cwd=checkout)
        cases = read_cases(raw)
        expected = {key for key in EXPECTED if any(fnmatch.fnmatchcase(key, pattern) for pattern in (filters or ["*"]))
                    and not ("MiddlewareBenchmarks.cs" in (omit or []) and ".MiddlewareBenchmarks." in key)}
        if set(cases) != expected:
            raise ValueError(f"Incomplete case set; missing={expected - cases.keys()}, unexpected={cases.keys() - expected}")
        # Hash environment characteristics, not ephemeral runner name/run ID.
        cpu_model = sorted({line for line in env['cpu'].get('/proc/cpuinfo', '').splitlines()
                            if line.startswith(('model name', 'vendor_id', 'cpu family', 'model\t', 'stepping'))})
        series = digest({"harness": provenance["harness_hash"], "settings": SETTINGS,
                         "platform": env["platform"], "cpu": cpu_model, "affinity": env["affinity"],
                         "quota": env['cpu'].get('/sys/fs/cgroup/cpu.max'),
                         "image": env["runner"].get("ImageVersion"), "runtime": env["dotnet_info"].split("global.json file:")[0],
                         "variables": env["dotnet_variables"]})
        record = {"schema": SCHEMA, "series": series, "provenance": provenance, "cases": cases,
                  "raw_reports": [str(p.relative_to(raw)) for p in sorted(raw.glob("results/*-report-full.json"))]}
        write_json(output / "record.json", record)
        (output / "summary.md").write_text(history_summary(record))
        return record
    finally:
        command("git", "worktree", "remove", "--force", str(checkout))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", default=RELEASE_CHECKOUT)
    parser.add_argument("--release-1.0.0", dest="release_1_0_0", action="store_true")
    parser.add_argument("--label", default="TaskFlow main")
    parser.add_argument("--release-tag")
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    measure(args.source, args.output, "TaskFlow 1.0.0" if args.release_1_0_0 else args.label, args.release_1_0_0, release_tag=args.release_tag)


if __name__ == "__main__":
    main()