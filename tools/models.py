from typing import Any, Literal, TypedDict

MongoID = str

Dict = dict[str, Any]
Pair = tuple[Any, Any]


class HasID(TypedDict):
    id: MongoID


class SptBuff(TypedDict):
    AbsoluteValue: bool
    BuffType: str
    Chance: int
    Delay: int
    Duration: int
    SkillName: str
    Value: int


class SpecialDiff[T](TypedDict):
    Changed: T
    Removed: list[str]


class SpecialProperties(TypedDict):
    ConflictingItems: tuple[list[str], list[str]]
    Buffs: dict[int, SptBuff | None]
    EffectsDamage: dict[str, dict[str, dict[str, int]] | None]
    EffectsHealth: dict[str, dict[Literal["value"], int]]


class FixedItem(HasID):
    types: list[str]
    properties: Dict
    special_properties: SpecialProperties


class SptItem(TypedDict):
    _id: MongoID
    _props: Dict


class CloneItem(TypedDict):
    itemTplToClone: MongoID
    overrideProperties: Dict
    fleaPriceRoubles: int
    handbookPriceRoubles: int


class WeaponAssemblyCondition(TypedDict):
    compareMethod: str
    value: float


class FixedQuestObjective(HasID):
    attributes: dict[str, WeaponAssemblyCondition]


class FixedQuest(HasID):
    objectives: list[FixedQuestObjective]


class SptQuestConditions(TypedDict):
    AvailableForFinish: list[Dict]


class SptQuest(TypedDict):
    _id: MongoID
    conditions: SptQuestConditions


class SptHandbookItem(TypedDict):
    Id: MongoID
    ParentId: MongoID
    Price: int


class SptHandbook(TypedDict):
    Categories: list[Dict]
    Items: list[SptHandbookItem]


class Prices(TypedDict):
    Handbook: int
    Flea: int | None
