"""Read-only confirmation of the text reported by each isolated QA participant."""
import argparse
import hashlib
import json
from pathlib import Path
import sqlite3

parser = argparse.ArgumentParser()
parser.add_argument("runs", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()
root = args.runs.resolve()
results = []
for report in sorted(root.glob("*/assessment.json")):
    assessment = json.loads(report.read_text(encoding="utf-8-sig"))
    entry = {"session": report.parent.name, "reportedSuccess": assessment["success"]}
    project_path = assessment.get("projectFile")
    if not project_path:
        entry["verified"] = False
        entry["reason"] = "No saved project reported"
        results.append(entry)
        continue
    project = Path(project_path).resolve()
    if not project.is_relative_to(root):
        raise ValueError(f"Project is outside the isolated runs: {project}")
    with sqlite3.connect(project.as_uri() + "?mode=ro", uri=True) as database:
        documents = [json.loads(row[0]) for row in database.execute("SELECT document_json FROM scene_document")]
        texts = ["\n".join("".join(span["text"] for span in block["spans"]) for block in doc["blocks"]) for doc in documents]
        entry.update({
            "integrity": database.execute("PRAGMA quick_check").fetchone()[0],
            "schema": database.execute("SELECT version FROM schema_info").fetchone()[0],
            "books": database.execute("SELECT count(*) FROM book").fetchone()[0],
            "chapters": database.execute("SELECT count(*) FROM chapter").fetchone()[0],
            "scenes": len(documents),
            "textMatchesExactly": assessment.get("firstText") in texts,
        })
    entry["sha256"] = hashlib.sha256(project.read_bytes()).hexdigest()
    entry["hasExpectedStructure"] = entry["books"] >= 1 and entry["chapters"] >= 1 and entry["scenes"] >= 1
    entry["verified"] = entry["integrity"] == "ok" and entry["schema"] == 6 and entry["textMatchesExactly"] and entry["hasExpectedStructure"]
    results.append(entry)
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(results, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps(results, ensure_ascii=False, indent=2))
if any(item["reportedSuccess"] and not item["verified"] for item in results):
    raise SystemExit(1)
