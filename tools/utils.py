import json
from pathlib import Path
from typing import Any


def json_load(fp: Path) -> Any:
    return json.loads(fp.read_text())


def json_load2(fp: Path) -> Any:
    return json.loads(fp.read_text(encoding="utf-8-sig"))


def json_dump(obj: Any, fp: Path):
    fp.write_text(json.dumps(obj, indent='\t'))
