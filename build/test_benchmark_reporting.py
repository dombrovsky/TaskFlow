"""Contract tests use synthetic fixtures only; these are never performance evidence."""

import copy
import json
from pathlib import Path
import tempfile
import unittest

from benchmark_report import EXPECTED, read_cases, write_json
from benchmark_compare import delta, evidence, render
from benchmark_publish import append


class ReportingTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        (self.root / "results").mkdir()
        self.benchmark = {"FullName": "TaskFlow.Benchmarks.BurstBenchmarks.SubmitAndDrain",
                          "DisplayInfo": "BurstBenchmarks: Job(IterationCount=8, WarmupCount=3)",
                          "Statistics": {"Mean": 100, "StandardDeviation": 2, "StandardError": 1, "N": 8},
                          "Memory": {"BytesAllocatedPerOperation": 50},
                          "Measurements": [{"IterationMode": "Workload", "IterationStage": stage}
                                           for stage in ["Warmup"] * 3 + ["Actual"] * 8]}

    def tearDown(self):
        self.temp.cleanup()

    def parse(self, benchmarks=None):
        (self.root / "results/case-report-full.json").write_text(json.dumps({"Benchmarks": benchmarks or [self.benchmark]}))
        return read_cases(self.root)

    def test_measured_metrics(self):
        case = next(iter(self.parse().values()))
        self.assertEqual(case["ops_s"], 10_000_000)
        self.assertEqual(case["allocated_bytes"], 50)

    def test_dry_is_rejected_even_with_samples(self):
        self.benchmark["DisplayInfo"] = "Dry"
        with self.assertRaisesRegex(ValueError, "Insufficient"):
            self.parse()

    def test_missing_and_incomplete_are_rejected(self):
        for field, value in [("Statistics", None), ("Measurements", []), ("Memory", None)]:
            with self.subTest(field=field):
                original = copy.deepcopy(self.benchmark)
                self.benchmark[field] = value
                with self.assertRaises(ValueError):
                    self.parse()
                self.benchmark = original

    def test_enqueue_only_never_claims_allocations(self):
        self.benchmark["FullName"] = "TaskFlow.Benchmarks.EnqueueOnlyBenchmarks.SubmitOnly"
        self.assertIsNone(next(iter(self.parse().values()))["allocated_bytes"])

    def test_duplicate_identifier_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "Duplicate"):
            self.parse([self.benchmark, self.benchmark])

    def test_zero_and_missing_allocation_changes(self):
        self.assertIn("zero baseline", delta(0, 10))
        self.assertEqual(delta(None, 10), "unsupported / missing")
        self.assertEqual(delta(100, 120), "+20.00 (+20.0%)")

    def test_variability_and_material_change(self):
        before = {"n": 8, "mean_ns": 100, "stddev_ns": 1, "error_ns": 0.4}
        after = {**before, "mean_ns": 130}
        self.assertIn("slowdown", evidence(before, after))
        self.assertIn("insufficient", evidence(before, before))
        self.assertIn("insufficient", evidence(before, {**after, "stddev_ns": 50}))
        self.assertIn("insufficient", evidence(before, {**after, "n": 3}))

    def test_missing_side_and_failure_remain_visible(self):
        report = {"selection": "test", "baseline_sha": "a", "candidate_sha": "b", "mode": "release",
                  "scope": "full", "baseline_failure": "build failed", "exclusions": ["middleware unsupported"]}
        text = render(report)
        self.assertIn("build failed", text)
        self.assertIn("middleware unsupported", text)

    def test_immutable_publication_and_rerun_preservation(self):
        results = self.root / "measurement"
        raw = results / "raw"
        (raw / "results").mkdir(parents=True)
        benchmarks = [{**copy.deepcopy(self.benchmark), "FullName": key} for key in EXPECTED]
        write_json(raw / "results/all-report-full.json", {"Benchmarks": benchmarks})
        record = {"schema": 1, "series": "series", "cases": read_cases(raw),
                  "provenance": {"measured_sha": "a" * 40, "run_id": "123", "attempt": "1",
                                 "run_url": "https://github.com/example/repo/actions/runs/123",
                                 "label": "test fixture", "timestamp": "2026-01-01"}}
        write_json(results / "record.json", record)
        write_json(results / "provenance.json", record["provenance"])
        (results / "summary.md").write_text("test fixture, never performance evidence")
        data = self.root / "data"
        append(results, data)
        original = (data / "index.json").read_bytes()
        append(results, data)
        self.assertEqual((data / "index.json").read_bytes(), original)
        record["provenance"]["attempt"] = "2"
        write_json(results / "record.json", record)
        append(results, data)
        self.assertEqual(len(json.loads((data / "index.json").read_text())["records"]), 2)
        record["provenance"]["label"] = "different content, same key"
        write_json(results / "record.json", record)
        with self.assertRaisesRegex(ValueError, "Immutable"):
            append(results, data)
        record["provenance"]["synthetic_validation_patch"] = "temporary patch"
        write_json(results / "record.json", record)
        with self.assertRaisesRegex(ValueError, "Synthetic"):
            append(results, data)


if __name__ == "__main__":
    unittest.main()