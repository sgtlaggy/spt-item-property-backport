#!/usr/bin/env python3

from typing import Any

from env import TARKOV_DEV_FILES, TMP_DIR
from utils import json_dump, json_load

AMMO: list[dict[str, Any]] = json_load(TARKOV_DEV_FILES / "ammo.json")
ITEMS: list[dict[str, Any]] = json_load(TARKOV_DEV_FILES / "items.json")

FIXED_ITEMS_F = TMP_DIR / "items.json"


def merge_ammo_items():
    items: dict[str, dict[str, Any]] = {i["id"]: i for i in ITEMS}
    for a in AMMO:
        ref_item: dict[str, str] = a.pop("item")
        item = items[ref_item["id"]]

        for prop, value in a.items():
            item[prop] = value

    json_dump(items, FIXED_ITEMS_F)


def simplify_conflicting_items():
    key = "ConflictingItems"
    for item in ITEMS:
        simplified = [i["id"] for i in item[key]]
        item[key] = simplified or []


def convert_keys():
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

    for ind, item in enumerate(ITEMS):
        new_item: dict[str, Any] = {}
        for k, v in item.items():
            new_key = names.get(k, k)
            new_item[new_key] = v
        ITEMS[ind] = new_item


def main():
    TMP_DIR.mkdir(parents=True, exist_ok=True)

    convert_keys()
    simplify_conflicting_items()
    merge_ammo_items()


if __name__ == "__main__":
    main()
