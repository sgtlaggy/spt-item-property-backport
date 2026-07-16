#!/usr/bin/env python3

from dataclasses import dataclass, field
from queue import PriorityQueue
from typing import Callable, TypeVar

from env import TARKOV_DEV_FILES, TMP_DIR
from models import Dict, MongoID, Prices, SptBuff
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

    @fixer(0)
    def extract_prices(self):
        prices: dict[MongoID, Prices] = {}

        for mongo, item in self.data.items():
            prices[mongo] = {
                "Handbook": item.pop("HandbookPrice"),
                "Flea": item.pop("FleaPrice"),
            }

        json_dump(prices, TMP_DIR / "prices.json")

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

    @fixer(5)
    def fix_single_use_medical_uses(self):
        for item in self.data.values():
            if item["properties"].get("MaxHpResource") == 1:
                item["properties"]["MaxHpResource"] = 0

    @fixer(6)
    def translate_armor_zones(self):
        translations = {
            "Head, Top of the head": "ParietalHead",
            "Head, Face": "HeadCommon",
            "Head, Nape": "BackHead",
            "Head, Ears": "Ears",
            "Head, Eyes": "Eyes",
            "Head, Jaws": "Jaw",
            "Thorax": "RibcageUp",
            "Thorax, Neck": "NeckBack",
            "Thorax, Throat": "NeckFront",
            "Thorax, Upper back": "SpineTop",
            "Back plate": None,
            "Front plate": None,
            "Stomach": "RibcageLow",
            "Stomach, Buttocks": "PelvisBack",
            "Stomach, Groin": "Pelvis",
            "Stomach, Left Side": "LeftSideChestDown",
            "Stomach, Lower back": "SpineDown",
            "Stomach, Right Side": "RightSideChestDown",
            "Left arm, Shoulder": "LeftUpperArm",
            "Left plate": None,
            "Right arm, Shoulder": "RightUpperArm",
            "Right plate": None,
        }
        for item in self.data.values():
            colliders = (item["properties"] or {}).get("armorColliders")
            if not colliders:
                continue
            item["properties"]["armorColliders"] = sorted(
                filter(None, (translations[collider] for collider in colliders))
            )

    @fixer(6)
    def translate_buffs(self):
        buff_type_translations = {
            "Energy recovery": "EnergyRate",
            "Hydration recovery": "HydrationRate",
            "Stamina recovery": "StaminaRate",
            "Skill": "SkillRate",
            "Hands tremor": "HandsTremor",
            "Tunnel effect": "QuantumTunnelling",
            "Max stamina": "MaxStamina",
            "Stops and prevents bleedings": "RemoveAllBloodLosses",
            "Health regeneration": "HealthRate",
            "Weight limit": "WeightLimit",
            "Body temperature": "BodyTemperature",
            "Damage taken (except the head)": "DamageModifier",
            "Antidote": "Antidote",
            "Pain": "Pain",
            # no data for:
            #  Contusion
            #  Fracture
            #  FrostbiteBuff
            #  HeavyBleeding
            #  LightBleeding
            #  UnknownToxin
            #  ZombieInfection
        }
        for item in self.data.values():
            buffs: list[SptBuff] | None = item["properties"].get("Buffs")
            if buffs is None:
                continue
            for buff in buffs:
                buff["AbsoluteValue"] = not buff.pop("percent")  # ty:ignore[invalid-key]
                buff["BuffType"] = buff_type_translations[buff.pop("type")]  # ty:ignore[invalid-key]
                skill_name = buff.pop("skillName", "") or ""  # ty:ignore[invalid-key]
                buff["SkillName"] = (buff.pop("skill") or {}).get("id", skill_name)  # ty:ignore[invalid-key]

    @fixer(6)
    def repack_health_effects(self):
        for item in self.data.values():
            props = item["properties"]
            if "Energy" in props:
                props["EffectsHealth"] = {
                    "Energy": {"value": props.pop("Energy")},
                    "Hydration": {"value": props.pop("Hydration")},
                }

    @fixer(6)
    def translate_cures(self):
        for item in self.data.values():
            cures: list[str] = item["properties"].get("EffectsDamage")
            if not cures:
                continue
            try:
                index = cures.index("LostLimb")
            except ValueError:
                pass
            else:
                cures[index] = "DestroyedPart"

    @fixer(7)
    def fix_armor_properties(self):
        for item in self.data.values():
            props = item["properties"]

            if "ricochetX" in props:
                props["RicochetParams"] = {
                    c.lower(): props.pop(f"ricochet{c}") for c in "XYZ"
                }

            if "ArmorMaterial" in props:
                if props["ArmorMaterial"] is not None:
                    props["ArmorMaterial"] = props["ArmorMaterial"]["id"]

            if (
                ("glasses" in item["types"])
                and ("armorColliders" not in props)
                and props.get("Durability")
            ):
                # tarkov.dev doesn’t provide this for glasses
                props["armorColliders"] = ["Eyes"]

    @fixer(7)
    def convert_cures(self):
        for item in self.data.values():
            props = item["properties"]
            cures: list[str] | None = props.get("EffectsDamage")
            if cures is None:
                continue

            new_cures: dict[str, dict[str, int]] = {}
            for cure in cures:
                new_cures[cure] = {}
                if cure == "Pain":
                    duration = props.pop("PainDuration", 0)
                    if duration:
                        new_cures[cure]["duration"] = duration
                elif cure == "DestroyedPart":
                    new_cures[cure] = {
                        "healthPenaltyMax": int(props.pop("healthPenaltyMax") * 100),
                        "healthPenaltyMin": int(props.pop("healthPenaltyMin") * 100),
                    }
                elif cure.endswith("Bleeding"):
                    cost = props.pop(cure, 0)
                    if cost:
                        new_cures[cure]["cost"] = cost

            props["EffectsDamage"] = new_cures

    @fixer(8)
    def extract_special_properties(self):
        """Extract properties that require special-case handling."""

        # { category: [props] }
        to_extract = ["ConflictingItems", "Buffs", "EffectsDamage", "EffectsHealth"]

        for item in self.data.values():
            props = item["properties"]

            special_props: Dict = {}
            for prop in to_extract:
                if prop in props:
                    special_props[prop] = props.pop(prop)

            if special_props:
                item["special_properties"] = special_props


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
