"""Normalise the raw json.tarkov.dev tasks payload into the shape
`queries/quests.gql` used to return (only `buildWeapon` objectives matter).

Run:  python normalise_tasks.py
In:   dev/raw_tasks.json
Out:  dev/quests.json
"""

import json
from pathlib import Path

HERE = Path(__file__).parent


def build_objective(obj):
    return {
        "id": obj.get("id"),
        "item": {"id": obj["item"]} if obj.get("item") else None,
        "containsCategory": [{"id": c} for c in (obj.get("containsCategory") or [])],
        "containsAll": [{"id": c} for c in (obj.get("containsAll") or [])],
        "attributes": [
            {"name": name, "requirement": req}
            for name, req in (obj.get("buildAttributes") or {}).items()
        ],
    }


def main():
    raw = json.loads((HERE / "dev" / "raw_tasks.json").read_text(encoding="utf-8"))
    tasks = raw["data"]["tasks"]

    result = []
    for task in tasks.values():
        objectives = [
            build_objective(o)
            for o in (task.get("objectives") or [])
            if o.get("type") == "buildWeapon"
        ]
        if not objectives:
            continue
        result.append({"id": task["id"], "objectives": objectives})

    out_dir = HERE / "dev"
    out_dir.mkdir(exist_ok=True)
    out_file = out_dir / "quests.json"
    out_file.write_text(json.dumps(result, indent="\t", sort_keys=True), encoding="utf-8")

    print(f"tasks with buildWeapon objectives: {len(result)} -> {out_file}")


if __name__ == "__main__":
    main()
