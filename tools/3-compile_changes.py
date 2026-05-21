#!/usr/bin/env python3

import functools
import sys
from typing import Any

from env import OUT_DIR, SPT_DB_TEMPLATES, TMP_DIR, WTT_BACKPORT_DB
from item_types import CloneItem, FixedItem, MongoID, SptItem
from utils import hang, json_dump, json_load

LIVE: dict[MongoID, FixedItem] = json_load(TMP_DIR / "items.json")
SPT: dict[MongoID, SptItem] = json_load(SPT_DB_TEMPLATES / "items.json")

BACKPORT: dict[MongoID, CloneItem] = {}
for fp in WTT_BACKPORT_DB.glob("*.json"):
    BACKPORT.update(json_load(fp))


def get_backport_or_spt(item: MongoID, prop: str) -> Any:
    clone = BACKPORT.get(item)
    if clone is None:
        return get_spt(item, prop)

    if prop in clone["overrideProperties"]:
        return clone["overrideProperties"][prop]
    else:
        return get_spt(clone["itemTplToClone"], prop)


def get_spt(mongo: MongoID, prop: str) -> Any:
    item = SPT.get(mongo)
    if item is None:
        print(f"{mongo} not found.", file=sys.stderr)
        return

    # sanity check for query typos
    if prop not in item["_props"] and prop not in {
        "Accuracy",
        "Recoil",
        "Ergonomics",
        "Loudness",
        "Velocity",
    }:
        print(f"ERROR: {prop} not found on {mongo}")
        return None

    return item["_props"].get(prop)


def item_exists(item: MongoID) -> bool:
    return item in BACKPORT or item in SPT


def compare_properties(mongo: MongoID) -> dict[str, Any]:
    out: dict[str, Any] = {}
    props = LIVE[mongo]["properties"]

    for prop, value in props.items():
        old_value = get_backport_or_spt(mongo, prop)
        if (value != old_value) and (value is not None):
            out[prop] = (value, old_value)

    return out


def set_from_list_or_none(val: list | None) -> set:
    if val:
        return set(val)
    return set()


def main():
    review: dict[MongoID, Any] = {}
    for mongo in LIVE.keys():
        if not item_exists(mongo):
            print(f"{mongo} not found.", file=sys.stderr)
            continue

        new = compare_properties(mongo)

        if new:
            review[mongo] = new

    changes: dict[MongoID, Any] = {}
    for mongo, data in review.items():
        new: dict[str, Any] = {}
        for k, v in data.items():
            if k != "ConflictingItems":
                new[k] = v[0]
                continue

            new_conflicting = set_from_list_or_none(v[0])
            old_conflicting = set_from_list_or_none(v[1])
            added_items = list(new_conflicting - old_conflicting)
            removed_items = list(old_conflicting - new_conflicting)

            # Rename the property so we can extend SPT’s ‘TemplateItemProperties’
            new["ConflictingItemsDiff"] = (added_items, removed_items)

        changes[mongo] = new

    OUT_DIR.mkdir(parents=True, exist_ok=True)

    json_dump(review, TMP_DIR / "review_items.json")
    json_dump(changes, OUT_DIR / "items.json")


if __name__ == "__main__":
    main()
    hang()
