import json
import sys
from pathlib import Path
from typing import Any


class SortedJsonEncoder(json.JSONEncoder):
    def __init__(self, *args, **kwargs):
        kwargs.pop("sort_keys")
        super().__init__(*args, **kwargs, sort_keys=True)

    def encode(self, o: Any) -> str:
        if o and isinstance(o, list) and isinstance(o[0], (str, float, int)):
            o.sort()

        return super().encode(o)


def json_load(fp: Path) -> Any:
    return json.loads(fp.read_text(encoding="utf-8-sig"))


def json_dump(obj: Any, fp: Path):
    fp.write_text(json.dumps(obj, indent="\t", cls=SortedJsonEncoder))


def hang():
    if not sys.argv[1:]:
        input("Press RETURN to exit.")
