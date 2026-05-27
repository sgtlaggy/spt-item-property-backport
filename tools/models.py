from typing import Any, TypedDict

MongoID = str

Dict = dict[str, Any]


class HasID(TypedDict):
    id: MongoID


class FixedItem(HasID):
    types: list[str]
    properties: dict[str, Any]


class SptItem(TypedDict):
    _id: MongoID
    _props: dict[str, Any]


class CloneItem(TypedDict):
    itemTplToClone: MongoID
    overrideProperties: dict[str, Any]


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
