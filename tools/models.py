from typing import Any, TypedDict

MongoID = str

Dict = dict[str, Any]


class HasID(TypedDict):
    id: MongoID


class FixedItem(HasID):
    types: list[str]
    properties: Dict


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
