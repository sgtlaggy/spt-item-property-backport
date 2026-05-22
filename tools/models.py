from typing import TypedDict, Any

MongoID = str


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
