#!/usr/bin/env python3

from typing import Any

from env import TARKOV_DEV_FILES, TMP_DIR
from models import MongoID
from utils import hang, json_dump, json_load

ItemProperties = dict[str, Any]

ITEMS: list[ItemProperties] = json_load(TARKOV_DEV_FILES / "items.json")

FIXED_ITEMS_F = TMP_DIR / "items.json"

ItemsDict = dict[MongoID, ItemProperties]


def move_generic_properties(items: ItemsDict):
    ignore_keys = {"id", "types", "properties"}

    props_to_move: list[str] = []
    for item in items.values():
        for key in item.keys():
            if key not in ignore_keys:
                props_to_move.append(key)

        break  # use only the first item to harvest names of properties to move

    for item in items.values():
        props: dict[str, Any] | None = item["properties"]
        if props is None:
            item["properties"] = props = {}

        for prop in props_to_move:
            props[prop] = item.pop(prop)


def fix_percentages(items: ItemsDict):
    properties = [
        "weaponErgonomicPenalty",
        "speedPenaltyPercent",
        "mousePenalty",
        "CheckTimeModifier",
        "LoadUnloadModifier",
    ]
    for item in items.values():
        props: dict[str, Any] = item["properties"]
        for prop in properties:
            val: float | None = props.get(prop)
            if val:  # not 0 or null
                props[prop] = round(props[prop] * 100, 2)


def set_flea_blacklist(items: ItemsDict):
    for item in items.values():
        blacklisted = "noFlea" in item["types"]
        item["properties"]["CanSellOnRagfair"] = not blacklisted


def simplify_conflicting_items(items: ItemsDict):
    # [{id: XXX}] -> [XXX]
    for item in items.values():
        conflicting_items = item["properties"]["ConflictingItems"]
        conflicting_items[:] = [i["id"] for i in conflicting_items]


def main():
    items = {item["id"]: item for item in ITEMS}
    move_generic_properties(items)
    fix_percentages(items)
    set_flea_blacklist(items)
    simplify_conflicting_items(items)

    TMP_DIR.mkdir(parents=True, exist_ok=True)
    json_dump(items, FIXED_ITEMS_F)


if __name__ == "__main__":
    main()
    hang()
