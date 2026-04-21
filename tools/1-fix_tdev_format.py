#!/usr/bin/env python3

from typing import Any

from env import TARKOV_DEV_FILES, TMP_DIR
from item_types import MongoID
from utils import json_dump, json_load

ItemProperties = dict[str, Any]

AMMO: list[ItemProperties] = json_load(TARKOV_DEV_FILES / "ammo.json")
ITEMS: list[ItemProperties] = json_load(TARKOV_DEV_FILES / "items.json")

FIXED_ITEMS_F = TMP_DIR / "items.json"

ItemsDict = dict[MongoID, ItemProperties]


def merge_ammo_items(
    old_items: list[ItemProperties], ammo: list[ItemProperties]
) -> ItemsDict:
    items: ItemsDict = {i["id"]: i for i in old_items}
    for a in ammo:
        ref_item: dict[str, str] = a.pop("item")
        item = items[ref_item["id"]]

        for prop, value in a.items():
            item[prop] = value

    return items


def convert_keys(items: ItemsDict):
    """Replace all keys with SPT DB equivalents."""
    names = {
        "weight": "Weight",
        "ergonomicsModifier": "Ergonomics",
        "blocksHeadphones": "BlocksEarpiece",
        "velocity": "Velocity",
        "loudness": "Loudness",
        "penetrationChance": "PenetrationChanceObstacle",
        "penetrationPower": "PenetrationPower",
        "penetrationPowerDeviation": "PenetrationPowerDiviation",
        "initialSpeed": "InitialSpeed",
        "damage": "Damage",
        "armorDamage": "ArmorDamage",
        "lightBleedModifier": "LightBleedingDelta",
        "heavyBleedModifier": "HeavyBleedingDelta",
        "conflictingItems": "ConflictingItems",
        "stackMaxSize": "StackMaxSize",
    }

    for mid, props in items.items():
        new_props: ItemProperties = {}
        for k, v in props.items():
            new_key = names.get(k, k)
            new_props[new_key] = v
        items[mid] = new_props


def simplify_conflicting_items(items: ItemsDict):
    key = "ConflictingItems"
    for item in items.values():
        simplified = [i["id"] for i in item[key]]
        item[key] = simplified or []


def main():
    TMP_DIR.mkdir(parents=True, exist_ok=True)

    items = merge_ammo_items(ITEMS, AMMO)
    convert_keys(items)
    simplify_conflicting_items(items)

    json_dump(items, FIXED_ITEMS_F)


if __name__ == "__main__":
    main()
