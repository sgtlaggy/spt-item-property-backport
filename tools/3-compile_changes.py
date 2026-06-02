#!/usr/bin/env python3

import sys
from copy import deepcopy
from typing import Any, Self

from env import OUT_DIR, SPT_DB_TEMPLATES, TMP_DIR, WTT_BACKPORT_DB
from models import (
    CloneItem,
    Dict,
    FixedQuest,
    MongoID,
    Prices,
    SptHandbook,
    SptItem,
    SptQuest,
)
from utils import hang, json_dump, json_load


class SPTData:
    items: dict[MongoID, SptItem]
    quests: dict[MongoID, SptQuest]
    prices: dict[MongoID, Prices]

    def load(self) -> Self:
        self.items = json_load(SPT_DB_TEMPLATES / "items.json")
        self.quests = json_load(SPT_DB_TEMPLATES / "quests.json")

        self.prices = {}
        handbook: SptHandbook = json_load(SPT_DB_TEMPLATES / "handbook.json")
        prices: dict[MongoID, int] = json_load(SPT_DB_TEMPLATES / "prices.json")
        for hb_item in handbook["Items"]:
            mongo = hb_item["Id"]
            flea_price = prices.get(mongo)
            self.prices[mongo] = {"Handbook": hb_item["Price"], "Flea": flea_price}

        for fp in WTT_BACKPORT_DB.glob("*.json"):
            wtt_items: dict[MongoID, CloneItem] = json_load(fp)
            for mongo, clone in wtt_items.items():
                item: SptItem = deepcopy(self.items[clone["itemTplToClone"]])
                item["_props"].update(clone["overrideProperties"])
                self.items[mongo] = item

                self.prices[mongo] = {
                    "Handbook": clone.get("handbookPriceRoubles"),
                    "Flea": clone.get("fleaPriceRoubles"),
                }

        return self

    def get_item_properties(self, mongo: MongoID) -> Dict:
        item = self.items.get(mongo)
        if item is None:  # just in case, should never hit
            msg = f"Item {mongo} not found."
            raise Exception(msg)

        return item["_props"]

    def get_item_prices(self, mongo: MongoID) -> Prices:
        """Returns item price as (handbook, flea)."""
        prices = self.prices.get(mongo)
        if prices is None:
            msg = f"Prices for {mongo} not found."
            raise Exception(msg)

        return prices

    def get_quest_objectives(self, mongo: MongoID) -> list[Dict]:
        quest = self.quests.get(mongo)
        if quest is None:
            msg = f"Quest {mongo} not found."
            raise Exception(msg)

        return quest["conditions"]["AvailableForFinish"]


def compile_item_changes(spt: SPTData):
    live_items = json_load(TMP_DIR / "items.json")

    review: dict[MongoID, Any] = {}
    for mongo in live_items.keys():
        try:
            spt_props = spt.get_item_properties(mongo)
        except Exception as e:
            print(e, file=sys.stderr)
            continue

        review_props = {}
        props = live_items[mongo]["properties"]

        for prop, live_value in props.items():
            spt_value = spt_props.get(prop)
            if (live_value != spt_value) and (live_value is not None):
                review_props[prop] = (live_value, spt_value)

        if review_props:
            review[mongo] = review_props

    changes: dict[MongoID, Any] = {}
    for mongo, data in review.items():
        changed_props: Dict = {}
        for k, v in data.items():
            if k != "ConflictingItems":
                changed_props[k] = v[0]
                continue

            v[0].sort()
            v[1].sort()
            live_conflicting = set(v[0])
            spt_conflicting = set(v[1])
            added_items = sorted(live_conflicting - spt_conflicting)
            removed_items = sorted(spt_conflicting - live_conflicting)

            # Rename the property so we can extend SPT’s ‘TemplateItemProperties’
            changed_props["ConflictingItemsDiff"] = (added_items, removed_items)

        changes[mongo] = changed_props

    json_dump(review, TMP_DIR / "review_items.json")
    json_dump(changes, OUT_DIR / "items.json")


def compile_quest_changes(spt: SPTData):  # noqa: C901
    live_quests: dict[MongoID, FixedQuest] = json_load(TMP_DIR / "quests.json")

    review: dict[MongoID, dict[MongoID, Dict]] = {}
    changes: dict[MongoID, dict[MongoID, Dict]] = {}
    for quest in live_quests.values():
        spt_objectives = {
            obj["id"]: obj for obj in spt.get_quest_objectives(quest["id"])
        }

        for obj in quest["objectives"]:
            spt_obj = spt_objectives.get(obj["id"])
            if spt_obj is None:
                print(f"Objective {obj['id']} not found.", file=sys.stderr)
                continue
            elif spt_obj["conditionType"] != "WeaponAssembly":
                print(
                    f"{obj['id']} has incorrect conditionType.",
                    file=sys.stderr,
                )
                continue

            review_attrs: Dict = {}
            for attr, live_value in obj["attributes"].items():
                spt_value = spt_obj.get(attr)
                if spt_value is None:
                    print(f"{attr} not found in {obj['id']}.", file=sys.stderr)
                    continue

                if live_value != spt_value:
                    review_attrs[attr] = (live_value, spt_value)

            if review_attrs:
                review.setdefault(quest["id"], {})[obj["id"]] = review_attrs
                changes.setdefault(quest["id"], {})[obj["id"]] = {
                    k: v[0] for k, v in review_attrs.items()
                }

    # setup required values
    for quest in changes.values():
        for obj_id, obj in quest.items():
            obj["id"] = obj_id
            obj["conditionType"] = "WeaponAssembly"
            obj["dynamicLocale"] = False

    json_dump(review, TMP_DIR / "review_quests.json")
    json_dump(changes, OUT_DIR / "quests.json")


def compile_price_changes(spt: SPTData):
    live_prices: dict[MongoID, Prices] = json_load(TMP_DIR / "prices.json")

    review: dict[MongoID, tuple[Prices, Prices | None]] = {}

    for mongo, live_price in live_prices.items():
        try:
            spt_price = spt.get_item_prices(mongo)
        except Exception as e:
            print(e, file=sys.stderr)
            continue

        if spt_price == live_price:
            continue

        review[mongo] = (live_price, spt_price)

    changes = {mongo: prices[0] for mongo, prices in review.items()}

    json_dump(review, TMP_DIR / "review_prices.json")
    json_dump(changes, OUT_DIR / "prices.json")


def main():
    OUT_DIR.mkdir(parents=True, exist_ok=True)

    spt_data = SPTData().load()

    compile_item_changes(spt_data)
    compile_quest_changes(spt_data)
    compile_price_changes(spt_data)


if __name__ == "__main__":
    main()
    hang()
