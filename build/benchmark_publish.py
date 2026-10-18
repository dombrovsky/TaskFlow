"""Append a validated measurement to benchmark-data. Run only in the trusted publisher."""

import argparse
import json
from pathlib import Path
import shutil
import subprocess

from benchmark_report import SCHEMA, digest, read_cases, write_json


def append(results, data):
    results, data = Path(results), Path(data)
    record = json.loads((results / "record.json").read_text())
    if record["schema"] != SCHEMA or read_cases(results / "raw") != record["cases"] or len(record["cases"]) != 16:
        raise ValueError("Only complete measured records can be published")
    p = record["provenance"]
    if p["run_id"] == "local" or not p["run_url"]:
        raise ValueError("History publication requires a workflow run identity")
    key = f"runs/{p['measured_sha']}/{int(p['run_id'])}-{int(p['attempt'])}"
    target = data / key
    # Retrying an identical publication is idempotent; no record may be replaced.
    if target.exists():
        if digest(json.loads((target / "record.json").read_text())) != digest(record):
            raise ValueError("Immutable record already exists with different content")
    else:
        target.mkdir(parents=True)
        for name in ["record.json", "provenance.json", "summary.md"]:
            shutil.copyfile(results / name, target / name)
        shutil.copytree(results / "raw", target / "raw")
    index_path = data / "index.json"
    index = json.loads(index_path.read_text()) if index_path.exists() else {"schema": SCHEMA, "records": []}
    entry = {"path": f"{key}/record.json", "sha256": digest(record), "series": record["series"],
             "label": p["label"], "sha": p["measured_sha"], "timestamp": p["timestamp"]}
    if entry not in index["records"]:
        index["records"].append(entry)
    index["records"].sort(key=lambda r: (r["timestamp"], r["path"]))
    write_json(index_path, index)
    (data / ".nojekyll").touch()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("results")
    parser.add_argument("data")
    args = parser.parse_args()
    append(args.results, args.data)


if __name__ == "__main__":
    main()