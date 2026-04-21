from typing import TypedDict

MongoID = str


class HasID(TypedDict):
    id: MongoID


class FixedItem(HasID):
    weight: float
    accuracyModifier: float | None
    recoilModifier: float | None
    ergonomicsModifier: float | None
    blocksHeadphones: bool | None
    velocity: float | None
    loudness: int | None
    conflictingItems: list[HasID]
    penetrationChance: float | None
    penetrationPower: float | None
    penetrationPowerDeviation: float | None
    initialSpeed: float | None
    stackMaxSize: float | None
    damage: float | None
    armorDamage: float | None
    lightBleedModifier: float | None
    heavyBleedModifier: float | None


class Properties(TypedDict):
    Weight: float
    Ergonomics: float
    BlocksEarpiece: bool
    Velocity: float
    Loudness: int
    ConflictingItems: list[MongoID]


class SptItem(TypedDict):
    _id: MongoID
    _props: Properties


class CloneItem(TypedDict):
    itemTplToClone: MongoID
    overrideProperties: Properties
