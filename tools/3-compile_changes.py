#!/usr/bin/env python3

import sys
from copy import deepcopy
from itertools import zip_longest
from typing import Any, Self

from env import OUT_DIR, SPT_DB, SPT_DB_TEMPLATES, TMP_DIR, WTT_BACKPORT_DB
from models import (
    CloneItem,
    Dict,
    FixedQuest,
    MongoID,
    Pair,
    Prices,
    SpecialProperties,
    SptBuff,
    SptHandbook,
    SptItem,
    SptQuest,
)
from utils import hang, json_dump, json_load

IGNORE_CURES = {"RadExposure"}
IGNORE_BUFFS = {
    "Contusion",
    "Fracture",
    "FrostbiteBuff",
    "HeavyBleeding",
    "LightBleeding",
    "UnknownToxin",
    "ZombieInfection",
}


class SPTData:
    items: dict[MongoID, SptItem]
    quests: dict[MongoID, SptQuest]
    prices: dict[MongoID, Prices]
    buffs: dict[str, list[SptBuff]]

    def load(self) -> Self:
        self.items = json_load(SPT_DB_TEMPLATES / "items.json")
        self.quests = json_load(SPT_DB_TEMPLATES / "quests.json")

        spt_globals: Dict = json_load(SPT_DB / "globals.json")
        self.buffs = spt_globals["config"]["Health"]["Effects"]["Stimulator"]["Buffs"]

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

    def get_buffs(self, mongo: MongoID) -> list[SptBuff] | None:
        item = self.items.get(mongo)
        if item is None:
            msg = f"Item {mongo} not found."
            raise Exception(msg)

        buff_name = item.get("_props", {}).get("StimulatorBuffs")
        if not buff_name:
            return None

        return self.buffs.get(buff_name)


def compile_item_changes(spt: SPTData):  # noqa: C901
    live_items = json_load(TMP_DIR / "items.json")

    review: dict[MongoID, Any] = {}
    for mongo in live_items.keys():
        try:
            spt_props = spt.get_item_properties(mongo)
        except Exception as e:
            print(e, file=sys.stderr)
            continue

        review_props = {}

        properties = {}
        props = live_items[mongo]["properties"]
        for prop, live_value in props.items():
            spt_value = spt_props.get(prop)
            if (live_value != spt_value) and (live_value is not None):
                properties[prop] = (live_value, spt_value)
        if properties:
            review_props["properties"] = properties

        special_properties = check_special_properties(
            spt, mongo, live_items[mongo]["special_properties"]
        )
        if special_properties:
            review_props["special_properties"] = special_properties

        if review_props:
            review[mongo] = review_props

    changes: dict[MongoID, Any] = {}
    remove_from_review = []
    for mongo, data in review.items():
        changed_props: Dict = {}
        if "properties" in data:
            changed_props["properties"] = {}
            for k, v in data["properties"].items():
                changed_props["properties"][k] = v[0]

        if "special_properties" in data:
            changed_special_props = consolidate_special_properties(
                data["special_properties"]
            )
            if changed_special_props:
                changed_props["special_properties"] = changed_special_props

        if changed_props:
            changes[mongo] = changed_props
        else:
            remove_from_review.append(mongo)

    for mongo in remove_from_review:
        del review[mongo]

    json_dump(review, TMP_DIR / "review_items.json")
    json_dump(changes, OUT_DIR / "items.json")


def check_special_properties(  # noqa: C901
    spt: SPTData,
    mongo: MongoID,
    special_props: Dict,
):
    review_props: dict[str, Pair] = {}

    spt_props = spt.get_item_properties(mongo)
    if "ConflictingItems" in special_props:
        live_conflicting = sorted(special_props["ConflictingItems"])
        spt_conflicting = sorted(spt_props["ConflictingItems"])
        if live_conflicting != spt_conflicting:
            review_props["ConflictingItems"] = (
                live_conflicting,
                spt_conflicting,
            )

    if "Buffs" in special_props:
        live_buffs: list[SptBuff] = special_props["Buffs"].copy()
        spt_buffs = spt.get_buffs(mongo) or []
        live_diff: list[SptBuff | None] = [None] * len(spt_buffs)
        spt_diff: list[SptBuff | None] = live_diff.copy()
        for index, buff in enumerate(spt_buffs):
            similar_index, similarity = get_most_similar_buff(live_buffs, buff)
            if similarity == len(buff):
                del live_buffs[similar_index]
            elif similarity > 0:
                live_diff[index] = live_buffs.pop(similar_index)
                spt_diff[index] = buff
        live_diff.extend(live_buffs)
        if any(live_diff) or any(spt_diff):
            review_props["Buffs"] = (live_diff, spt_diff)

    if "EffectsDamage" in special_props:
        live_cures = special_props["EffectsDamage"]
        spt_cures = spt_props.get("effects_damage", {})
        if get_special_dict_diff(live_cures, spt_cures, IGNORE_CURES):
            review_props["EffectsDamage"] = (live_cures, spt_cures)

    if "EffectsHealth" in special_props:
        live_effects = special_props["EffectsHealth"]
        spt_effects = spt_props.get("effects_health", {})
        if get_special_dict_diff(live_effects, spt_effects):
            review_props["EffectsHealth"] = (live_effects, spt_effects)

    return review_props


def consolidate_special_properties(review: dict[str, Pair]) -> SpecialProperties:  # noqa: C901
    changed_props: SpecialProperties = {}  # ty:ignore[missing-typed-dict-key]

    if "ConflictingItems" in review:
        live_conflicting = set(review["ConflictingItems"][0])
        spt_conflicting = set(review["ConflictingItems"][1])
        added_items = sorted(live_conflicting - spt_conflicting)
        removed_items = sorted(spt_conflicting - live_conflicting)
        changed_props["ConflictingItems"] = (added_items, removed_items)

    if "Buffs" in review:
        diff: dict[int, SptBuff | None] = {}
        for index, (lbuff, sbuff) in enumerate(
            zip_longest(review["Buffs"][0], review["Buffs"][1])
        ):
            if lbuff is None and sbuff is None:
                continue  # no change
            elif sbuff and sbuff["BuffType"] in IGNORE_BUFFS:
                continue  # buffs not listed by tarkov.dev
            elif lbuff is None:
                diff[index] = None
            else:
                diff[index] = lbuff
        changed_props["Buffs"] = diff

    if "EffectsDamage" in review:
        changed_props["EffectsDamage"] = get_special_dict_diff(
            *review["EffectsDamage"], IGNORE_CURES
        )

    if "EffectsHealth" in review:
        live_effects, spt_effects = review["EffectsHealth"]
        diff = {}
        for effect, data in live_effects.items():
            if ((effect not in spt_effects) and (data["value"] != 0)) or (
                (effect in spt_effects)
                and (data["value"] != spt_effects[effect]["value"])
            ):
                if data["value"] == 0:
                    diff[effect] = None
                else:
                    diff[effect] = data
        if diff:
            changed_props["EffectsHealth"] = diff  # ty:ignore[invalid-assignment]

    return changed_props


def get_buff_similarity(a: SptBuff, b: SptBuff) -> int:
    if (a["BuffType"] != b["BuffType"]) or (a["SkillName"] != b["SkillName"]):
        return 0
    return sum(a[key] == b[key] for key in a)  # ty:ignore[invalid-key]


def get_most_similar_buff(buffs: list[SptBuff], buff: SptBuff) -> tuple[int, int]:
    best_index, best_similarity = -1, 0
    for i, b in enumerate(buffs):
        similarity = get_buff_similarity(buff, b)
        if similarity > best_similarity:
            best_index = i
            best_similarity = similarity
    return best_index, best_similarity


def check_dicts_match(partial: Dict, full: Dict) -> bool:
    """Check all values from PARTIAL match those in FULL."""
    for k, v in partial.items():
        if (k not in full) or (v != full[k]):
            return False
    return True


def get_special_dict_diff(
    live: Dict,
    spt: Dict,
    ignore_keys: set[str] | None = None,
) -> dict[str, Any | None]:
    ignore_keys = ignore_keys or set()
    changed = {}
    for key, live_value in live.items():
        spt_value = spt.get(key, {})
        if not check_dicts_match(live_value, spt_value):
            # changed/added items
            changed[key] = live_value
    for key in spt:
        if key not in live and key not in ignore_keys:
            # removed items
            changed[key] = None
    return changed


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
