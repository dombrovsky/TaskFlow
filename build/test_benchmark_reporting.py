"""Contract tests use synthetic fixtures only; these are never performance evidence."""

import copy
import json
from pathlib import Path
import tempfile
import unittest

from benchmark_report import read_cases


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


if __name__ == "__main__":
    unittest.main()