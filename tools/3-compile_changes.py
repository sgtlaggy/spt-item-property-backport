#!/usr/bin/env python3

import sys
from typing import Any, Self

from env import OUT_DIR, SPT_DB_TEMPLATES, TMP_DIR, WTT_BACKPORT_DB
from models import CloneItem, Dict, MongoID, SptItem
from utils import hang, json_dump, json_load


class SPTData:
    spt_items: dict[MongoID, SptItem]
    wtt_items: dict[MongoID, CloneItem]

    def load(self) -> Self:
        self.spt_items = json_load(SPT_DB_TEMPLATES / "items.json")

        self.wtt_items = {}
        for fp in WTT_BACKPORT_DB.glob("*.json"):
            self.wtt_items.update(json_load(fp))

        return self

    def get_item_properties(self, mongo: MongoID) -> Dict:
        clone = self.wtt_items.get(mongo)
        clone_props = clone["overrideProperties"] if clone else {}

        base_item_id = clone["itemTplToClone"] if clone else mongo
        item = self.spt_items.get(base_item_id)
        if item is None:  # just in case, should never hit
            msg = f"{base_item_id} not found."
            raise Exception(msg)

        props = item["_props"].copy()
        props.update(clone_props)
        return props


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

            # sort lists to reduce noise in diffs
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


def main():
    OUT_DIR.mkdir(parents=True, exist_ok=True)

    spt_data = SPTData().load()

    compile_item_changes(spt_data)


if __name__ == "__main__":
    main()
    hang()
