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
