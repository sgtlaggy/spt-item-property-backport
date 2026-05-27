#!/usr/bin/env python3

from dataclasses import dataclass, field
from queue import PriorityQueue
from typing import Callable, TypeVar

from env import TARKOV_DEV_FILES, TMP_DIR
from models import Dict, MongoID
from utils import hang, json_dump, json_load

T = TypeVar("T")

Method = Callable[[T], None]


# dataclass with ‘order’ and ‘comapre’ for PriorityQueue sorting
@dataclass(order=True)
class FixerMethod:
    priority: int
    method: Method = field(compare=False)

    def __call__(self, *args, **kwargs):
        self.method(*args, **kwargs)


def fixer(priority: int = 0) -> Callable[[Method], FixerMethod]:
    def decorator(method: Method) -> FixerMethod:
        """Decorator that marks a method to be run by ."""
        return FixerMethod(priority, method)

    return decorator


class DataFixer:
    filename: str

    data: dict[MongoID, Dict]

    def load(self):
        lst: list[Dict] = json_load(TARKOV_DEV_FILES / self.filename)
        self.data = {thing["id"]: thing for thing in lst}

    def save(self):
        json_dump(self.data, TMP_DIR / self.filename)

    def fix(self):
        if not hasattr(self, "data"):
            self.load()

        fixers: PriorityQueue[FixerMethod] = PriorityQueue()
        for attr in type(self).__dict__.values():
            if isinstance(attr, FixerMethod):
                fixers.put(attr)
        while not fixers.empty():
            fixer = fixers.get()
            fixer(self)

        self.save()


class ItemFixer(DataFixer):
    filename = "items.json"

    @fixer(1)
    def move_generic_properties(self):
        ignore_keys = {"id", "types", "properties"}

        props_to_move: list[str] = []
        for item in self.data.values():
            for key in item.keys():
                if key not in ignore_keys:
                    props_to_move.append(key)

            break  # use only the first item to harvest names of properties to move

        for item in self.data.values():
            # This looks like a place for ‘dict.setdefault’ but
            # the key is always present, just null in some cases.
            props: Dict | None = item["properties"]
            if props is None:
                item["properties"] = props = {}

            for prop in props_to_move:
                props[prop] = item.pop(prop)

    @fixer(2)
    def fix_percentages(self):
        properties = [
            "weaponErgonomicPenalty",
            "speedPenaltyPercent",
            "mousePenalty",
            "CheckTimeModifier",
            "LoadUnloadModifier",
        ]
        for item in self.data.values():
            props: Dict = item["properties"]
            for prop in properties:
                val: float | None = props.get(prop)
                if val:  # not 0 or null
                    props[prop] = round(props[prop] * 100, 2)

    @fixer(3)
    def set_flea_blacklist(self):
        for item in self.data.values():
            blacklisted = "noFlea" in item["types"]
            item["properties"]["CanSellOnRagfair"] = not blacklisted

    @fixer(4)
    def simplify_conflicting_items(self):
        # [{id: XXX}] -> [XXX]
        for item in self.data.values():
            conflicting_items = item["properties"]["ConflictingItems"]
            conflicting_items[:] = [i["id"] for i in conflicting_items]


class QuestFixer(DataFixer):
    filename = "quests.json"

    @fixer(1)
    def filter_weapon_assembly_quests(self):
        to_remove: list[MongoID] = []
        for quest_id, quest in self.data.items():
            assembly_objectives = list(filter(None, quest["objectives"]))
            if not assembly_objectives:
                to_remove.append(quest_id)
                continue

            quest["objectives"] = assembly_objectives

        for quest_id in to_remove:
            self.data.pop(quest_id)

    @fixer(2)
    def attributes_list_to_dict(self):
        name_translation = {"accuracy": "baseAccuracy"}
        for quest in self.data.values():
            for objective in quest["objectives"]:
                new_attributes: Dict = {}
                attrs: list[Dict] = objective.pop("attributes")
                for attr in attrs:
                    name: str = name_translation.get(attr["name"], attr["name"])
                    new_attributes[name] = attr["requirement"]
                objective["attributes"] = new_attributes

    @fixer(3)
    def move_conditions(self):
        for quest in self.data.values():
            for objective in quest["objectives"]:
                attrs = objective["attributes"]

                weapon_id = objective.pop("item")["id"]
                attrs["target"] = [weapon_id]

                attrs["containsItems"] = has_all_items = []
                for item in objective.pop("containsAll"):
                    has_all_items.append(item["id"])

                attrs["hasItemFromCategory"] = has_category = []
                for category in objective.pop("containsCategory"):
                    has_category.append(category["id"])


def main():
    TMP_DIR.mkdir(parents=True, exist_ok=True)

    ItemFixer().fix()
    QuestFixer().fix()


if __name__ == "__main__":
    main()
    hang()
