"""Versioned measured-result contract shared by history and same-run comparisons."""

import hashlib
import json
import math
from pathlib import Path


SCHEMA = 1
RELEASE = "0800af84229d7beb3a6fa507d236d0ff533b0c92"
RELEASE_CHECKOUT = "7e3410545c4cdf38602f2cdd01b2e9e67d9bad51"
SETTINGS = {"warmup": 3, "iterations": 8, "launches": 1, "processor_count": 2,
            "sdk": "10.0.100", "runtime": "10.0.0", "bdn": "0.15.8"}
PRODUCTION = ["TaskFlow", "TaskFlow.Annotations", "Directory.Build.props", "Directory.Build.targets", "TaskFlow.snk"]
PREFIX = "TaskFlow.Benchmarks."
EXPECTED = {PREFIX + name for name in [
    "BurstBenchmarks.SubmitAndDrain", "EnqueueOnlyBenchmarks.SubmitOnly",
    *[f"MiddlewareBenchmarks.PipelineCompletion(Layers: {n})" for n in [0, 1, 2, 4, 8]],
    *[f"ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: {n})" for n in [1, 4, 16]],
    *[f"SequentialBenchmarks.{method}(Token: {token})" for token in ["None", "Live", "CanceledCooperative"]
      for method in ["SyncCompletion", "AsyncCompletion"]]]}


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True).encode()).hexdigest()


def case_id(benchmark):
    # FullName includes class, method and workload parameters; retain BDN's stable identity.
    return benchmark["FullName"]


def read_cases(raw):
    cases = {}
    for path in sorted(Path(raw).glob("results/*-report-full.json")):
        report = json.loads(path.read_text())
        for benchmark in report["Benchmarks"]:
            identity = case_id(benchmark)
            if identity in cases:
                raise ValueError(f"Duplicate case: {identity}")
            stats = benchmark.get("Statistics")
            samples = [m for m in benchmark.get("Measurements", [])
                       if m["IterationMode"] == "Workload" and m["IterationStage"] == "Actual"]
            warmup = [m for m in benchmark.get("Measurements", [])
                      if m["IterationMode"] == "Workload" and m["IterationStage"] == "Warmup"]
            job = benchmark["DisplayInfo"]
            if "Dry" in str(job) or not stats or len(samples) < 8 or len(warmup) < 3:
                raise ValueError(f"Insufficient measured evidence for {identity}")
            mean = stats["Mean"]
            if not math.isfinite(mean) or mean <= 0:
                raise ValueError(f"Invalid mean for {identity}")
            enqueue = ".EnqueueOnlyBenchmarks." in identity
            memory = benchmark.get("Memory") or {}
            if not enqueue and memory.get("BytesAllocatedPerOperation") is None:
                raise ValueError(f"Missing managed allocation measurement for {identity}")
            cases[identity] = {"mean_ns": mean, "stddev_ns": stats["StandardDeviation"],
                               "error_ns": stats["StandardError"], "n": stats["N"],
                               "ops_s": 1e9 / mean,
                               "allocated_bytes": None if enqueue else memory.get("BytesAllocatedPerOperation"),
                               "allocation_note": "unsupported: deferred execution and iteration setup" if enqueue else "managed bytes/op",
                               "actual_iterations": len(samples), "warmup_iterations": len(warmup)}
    if not cases:
        raise ValueError("No measured reports")
    return cases


def write_json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, allow_nan=False) + "\n")


def history_summary(record):
    p = record["provenance"]
    text = f"# {p['label']}\n\nMeasured checkout: `{p['measured_sha']}`; release source: `{p.get('release_source_sha')}`\n\n"
    text += f"Harness: `{p['harness_hash']}`; environment series: `{record['series']}`\n\n"
    text += f"[Workflow run]({p['run_url']}) · [Raw results](./raw/)\n\n"
    text += "Cross-run history is advisory. Inspect variability and runner configuration before comparing.\n\n"
    text += "| Case | Mean ns/op | StdDev | Ops/s | Bytes/op | Samples |\n|---|---:|---:|---:|---:|---:|\n"
    for key, value in record["cases"].items():
        allocated = value['allocated_bytes']
        text += f"| {key} | {value['mean_ns']:.2f} | {value['stddev_ns']:.2f} | {value['ops_s']:.2f} | {allocated if allocated is not None else 'unsupported'} | {value['n']} |\n"
    return text