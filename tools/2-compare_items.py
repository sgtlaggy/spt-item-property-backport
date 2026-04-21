#!/usr/bin/env python3

import functools
import sys
from typing import Any

from env import OUT_DIR, SPT_DB_TEMPLATES, TMP_DIR, WTT_BACKPORT_DB
from item_types import CloneItem, FixedItem, MongoID, SptItem
from utils import json_dump, json_load, json_load2

UPDATED: dict[MongoID, FixedItem] = json_load(TMP_DIR / "items.json")
SPT: dict[MongoID, SptItem] = json_load(SPT_DB_TEMPLATES / "items.json")

BACKPORT: dict[MongoID, CloneItem] = {}
for fp in WTT_BACKPORT_DB.iterdir():
    BACKPORT.update(json_load2(fp))


def get_backport_or_spt(item: MongoID, prop: str) -> Any:
    clone = BACKPORT.get(item)
    if clone is None:
        return get_spt(item, prop)

    val = clone["overrideProperties"].get(prop)
    if val is None:
        return get_spt(clone["itemTplToClone"], prop)

    return val


def get_spt(item: MongoID, prop: str) -> Any:
    db = SPT.get(item)
    if db is None:
        print(f"{item} not found.", file=sys.stderr)
        return
    return db["_props"].get(prop)


def item_exists(item: MongoID) -> bool:
    return item in BACKPORT or item in SPT


def compare(
    prop: str,
    mongo: MongoID,
    out: dict[str, Any],
):
    new_val = UPDATED[mongo].get(prop)
    old_val = get_backport_or_spt(mongo, prop)
    if (new_val != old_val) and (new_val is not None):
        out[prop] = (new_val, old_val)


def set_from_list_or_none(val: list | None) -> set:
    if val:
        return set(val)
    return set()


def main():
    REVIEW: dict[MongoID, Any] = {}
    for mid, lprops in UPDATED.items():
        new: dict[str, Any] = {}
        cmp = functools.partial(compare, mongo=mid, out=new)

        if not item_exists(mid):
            print(f"{mid} not found.", file=sys.stderr)
            continue

        cmp("Weight")
        cmp("Ergonomics")
        cmp("BlocksEarpiece")
        cmp("Velocity")
        cmp("Loudness")
        cmp("PenetrationChanceObstacle")
        cmp("PenetrationPower")
        cmp("PenetrationPowerDiviation")
        cmp("InitialSpeed")
        cmp("Damage")
        cmp("ArmorDamage")
        cmp("LightBleedingDelta")
        cmp("HeavyBleedingDelta")
        cmp("ConflictingItems")

        # skip currency where (None, ...)
        new_stack = lprops.get("StackMaxSize")
        old_stack = get_backport_or_spt(mid, "StackMaxSize")
        if (new_stack is not None) and (new_stack != old_stack):
            new["StackMaxSize"] = (new_stack, old_stack)

        if new:
            REVIEW[mid] = new

    OUT: dict[MongoID, Any] = {}
    for mid, data in REVIEW.items():
        new: dict[str, Any] = {}
        for k, v in data.items():
            if k != "ConflictingItems":
                new[k] = v[0]
                continue

            new_conflicting = set_from_list_or_none(v[0])
            old_conflicting = set_from_list_or_none(v[1])
            added_items = list(new_conflicting - old_conflicting)
            removed_items = list(old_conflicting - new_conflicting)
            new["ConflictingItems"] = (added_items, removed_items)
        OUT[mid] = new

    OUT_DIR.mkdir(parents=True, exist_ok=True)

    json_dump(REVIEW, TMP_DIR / "review_items.json")
    json_dump(OUT, OUT_DIR / "items.json")


if __name__ == "__main__":
    main()
