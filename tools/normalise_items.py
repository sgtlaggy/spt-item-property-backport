"""Normalise the raw json.tarkov.dev items payload into the shape
`queries/items.gql` used to return, so the refactor/compile steps stay unchanged.

Run:  python normalise_items.py
In:   dev/raw_items.json  (raw json.tarkov.dev payload)
Out:  dev/items.json      (list of items, aliased keys)
"""

import json
from pathlib import Path

HERE = Path(__file__).parent

# gql alias -> (json property name, transform)
GENERIC = {
    "Accuracy": "accuracyModifier",
    "Ergonomics": "ergonomicsModifier",
    "Loudness": "loudness",
    "Recoil": "recoilModifier",
    "Velocity": "velocity",
}

# SPT buff type -> the human readable name tarkov.dev used in `stimEffects.type`
BUFF_HUMAN = {
    "EnergyRate": "Energy recovery",
    "HydrationRate": "Hydration recovery",
    "StaminaRate": "Stamina recovery",
    "SkillRate": "Skill",
    "HandsTremor": "Hands tremor",
    "QuantumTunnelling": "Tunnel effect",
    "MaxStamina": "Max stamina",
    "RemoveAllBloodLosses": "Stops and prevents bleedings",
    "HealthRate": "Health regeneration",
    "WeightLimit": "Weight limit",
    "BodyTemperature": "Body temperature",
    "DamageModifier": "Damage taken (except the head)",
    "Antidote": "Antidote",
    "Pain": "Pain",
}

# raw names that differ in casing from the SPT enum
BUFF_ALIAS = {
    "Removeallbloodlosses": "RemoveAllBloodLosses",
}

# buffs tarkov.dev never exposed; skip them to match upstream behaviour
BUFF_SKIP = {
    "Contusion",
    "Fracture",
    "FrostbiteBuff",
    "HeavyBleeding",
    "LightBleeding",
    "UnknownToxin",
    "ZombieInfection",
}


def zones_to_colliders(zones):
    """'Collider Type ParietalHead' -> 'ParietalHead'; plate zones are dropped.

    Returns None when nothing is left so the property is omitted entirely,
    which keeps plate-only carriers (which SPT also leaves empty) untouched.
    """
    out = []
    for zone in zones or []:
        if not isinstance(zone, str):
            continue
        if zone.startswith("Collider Type "):
            out.append(zone[len("Collider Type ") :])
        # 'Armor Zone ...' == the plate zones tarkov.dev reported as *plate*
    return sorted(set(out)) or None


def buffs_from_stim_effects(effects):
    out = []
    for e in effects or []:
        raw = e.get("type")
        if not raw:
            continue
        raw = BUFF_ALIAS.get(raw, raw)
        if raw in BUFF_SKIP:
            continue
        human = BUFF_HUMAN.get(raw)
        if human is None:
            continue
        skill = e.get("skill")
        out.append(
            {
                "Chance": e.get("chance"),
                "Delay": e.get("delay"),
                "Duration": e.get("duration"),
                "percent": e.get("percent"),
                "skill": {"id": skill} if skill else None,
                "skillName": "",
                "type": human,
                "Value": e.get("value"),
            }
        )
    return out


def armor(props):
    return {
        "ArmorType": props.get("armorType"),
        "BluntThroughput": props.get("bluntThroughput"),
        "armorClass": props.get("class"),
        "Durability": props.get("durability"),
        "weaponErgonomicPenalty": props.get("ergoPenalty"),
        "ArmorMaterial": props.get("material"),
        "RepairCost": props.get("repairCost"),
        "speedPenaltyPercent": props.get("speedPenalty"),
        "mousePenalty": props.get("turnPenalty"),
        "armorColliders": zones_to_colliders(props.get("zones")),
    }


def armor_attachment(props):
    out = armor(props)
    out["BlindnessProtection"] = props.get("blindnessProtection")
    return out


def helmet(props):
    out = armor(props)
    out["BlindnessProtection"] = props.get("blindnessProtection")
    out["BlocksEarpiece"] = props.get("blocksHeadset")
    out["DeafStrength"] = props.get("deafening")
    out["ricochetX"] = props.get("ricochetX")
    out["ricochetY"] = props.get("ricochetY")
    out["ricochetZ"] = props.get("ricochetZ")
    return out


def chest_rig(props):
    out = armor(props)
    out.pop("armorColliders", None)  # gql did not request zones for chest rigs
    return out


def glasses(props):
    return {
        "BlindnessProtection": props.get("blindnessProtection"),
        "BluntThroughput": props.get("bluntThroughput"),
        "armorClass": props.get("class"),
        "Durability": props.get("durability"),
        "weaponErgonomicPenalty": props.get("ergoPenalty"),
        "ArmorMaterial": props.get("material"),
        "RepairCost": props.get("repairCost"),
    }


def ammo(props):
    acc = props.get("accuracyModifier")
    rec = props.get("recoilModifier")
    return {
        "ammoAccr": None if acc is None else round(acc * 100),
        "ArmorDamage": props.get("armorDamage"),
        "BallisticCoeficient": props.get("ballisticCoeficient"),
        "BulletMassGram": props.get("bulletMassGrams"),
        "Damage": props.get("damage"),
        "DurabilityBurnModificator": props.get("durabilityBurnFactor"),
        "MalfFeedChance": props.get("failureToFeedChance"),
        "FragmentationChance": props.get("fragmentationChance"),
        "HeatFactor": props.get("heatFactor"),
        "HeavyBleedingDelta": props.get("heavyBleedModifier"),
        "InitialSpeed": props.get("initialSpeed"),
        "LightBleedingDelta": props.get("lightBleedModifier"),
        "MalfMisfireChance": props.get("misfireChance"),
        "PenetrationChanceObstacle": props.get("penetrationChance"),
        "PenetrationPower": props.get("penetrationPower"),
        "PenetrationPowerDiviation": props.get("penetrationPowerDeviation"),
        "ammoRec": None if rec is None else round(rec * 100),
        "StackMaxSize": props.get("stackMaxSize"),
    }


def barrel(props):
    return {
        "CenterOfImpact": props.get("centerOfImpact"),
        "DeviationCurve": props.get("deviationCurve"),
        "DeviationMax": props.get("deviationMax"),
    }


def food(props):
    return {
        "Energy": props.get("energy"),
        "Hydration": props.get("hydration"),
        "MaxResource": props.get("units"),
        "Buffs": buffs_from_stim_effects(props.get("stimEffects")),
    }


def headphone(props):
    return {
        "AmbientVolume": props.get("ambientVolume"),
        "CompressorAttack": props.get("compressorAttack"),
        "CompressorGain": props.get("compressorGain"),
        "CompressorRelease": props.get("compressorRelease"),
        "CompressorThreshold": props.get("compressorThreshold"),
        "RolloffMultiplier": props.get("distanceModifier"),
        "Distortion": props.get("distortion"),
        "DryVolume": props.get("dryVolume"),
    }


def magazine(props):
    return {
        "CheckTimeModifier": props.get("ammoCheckModifier"),
        "LoadUnloadModifier": props.get("loadModifier"),
        "MalfunctionChance": props.get("malfunctionChance"),
    }


def medical(props):
    return {
        "EffectsDamage": props.get("cures"),
        "MaxHpResource": props.get("uses"),
        "medUseTime": props.get("useTime"),
    }


def medkit(props):
    return {
        "EffectsDamage": props.get("cures"),
        "MaxHpResource": props.get("hitpoints"),
        "HeavyBleeding": props.get("hpCostHeavyBleeding"),
        "LightBleeding": props.get("hpCostLightBleeding"),
        "hpResourceRate": props.get("maxHealPerUse"),
        "medUseTime": props.get("useTime"),
    }


def nightvision(props):
    return {
        "DiffuseIntensity": props.get("diffuseIntensity"),
        "Intensity": props.get("intensity"),
        "NoiseIntensity": props.get("noiseIntensity"),
        "NoiseScale": props.get("noiseScale"),
    }


def painkiller(props):
    return {
        "EffectsDamage": props.get("cures"),
        "Energy": props.get("energyImpact"),
        "Hydration": props.get("hydrationImpact"),
        "PainDuration": props.get("painkillerDuration"),
        "MaxHpResource": props.get("uses"),
        "medUseTime": props.get("useTime"),
    }


def stim(props):
    return {
        "EffectsDamage": props.get("cures"),
        "Buffs": buffs_from_stim_effects(props.get("stimEffects")),
        "medUseTime": props.get("useTime"),
    }


def surgical_kit(props):
    return {
        "EffectsDamage": props.get("cures"),
        "healthPenaltyMax": props.get("maxLimbHealth"),
        "healthPenaltyMin": props.get("minLimbHealth"),
        "MaxHpResource": props.get("uses"),
        "medUseTime": props.get("useTime"),
    }


def weapon(props):
    return {
        "CameraSnap": props.get("cameraSnap"),
        "CenterOfImpact": props.get("centerOfImpact"),
        "DeviationCurve": props.get("deviationCurve"),
        "DeviationMax": props.get("deviationMax"),
        "RecoilAngle": props.get("recoilAngle"),
        "RecolDispersion": props.get("recoilDispersion"),
        "RecoilForceBack": props.get("recoilHorizontal"),
        "RecoilForceUp": props.get("recoilVertical"),
    }


TYPE_MAP = {
    "ItemPropertiesAmmo": ammo,
    "ItemPropertiesArmor": armor,
    "ItemPropertiesArmorAttachment": armor_attachment,
    "ItemPropertiesBarrel": barrel,
    "ItemPropertiesChestRig": chest_rig,
    "ItemPropertiesFoodDrink": food,
    "ItemPropertiesGlasses": glasses,
    "ItemPropertiesHeadphone": headphone,
    "ItemPropertiesHelmet": helmet,
    "ItemPropertiesKey": lambda p: {"MaximumNumberOfUsage": p.get("uses")},
    "ItemPropertiesMagazine": magazine,
    "ItemPropertiesMedicalItem": medical,
    "ItemPropertiesMedKit": medkit,
    "ItemPropertiesNightVision": nightvision,
    "ItemPropertiesPainkiller": painkiller,
    "ItemPropertiesStim": stim,
    "ItemPropertiesSurgicalKit": surgical_kit,
    "ItemPropertiesWeapon": weapon,
}


def build_item(item):
    props = item.get("properties") or {}
    out = {
        "id": item["id"],
        "types": item.get("types") or [],
        "HandbookPrice": item.get("basePrice"),
        "FleaPrice": item.get("avg24hPrice"),
    }
    for alias, field in GENERIC.items():
        out[alias] = item.get(field)
    out["ConflictingItems"] = [
        {"id": i} for i in (item.get("conflictingItems") or [])
    ]

    ptype = props.get("propertiesType")
    handler = TYPE_MAP.get(ptype)
    if handler is None:
        out["properties"] = {}
    else:
        out["properties"] = {k: v for k, v in handler(props).items() if v is not None}

    return out


def main():
    raw = json.loads((HERE / "dev" / "raw_items.json").read_text(encoding="utf-8"))
    items = raw["data"]["items"]

    result = [build_item(v) for v in items.values()]

    out_dir = HERE / "dev"
    out_dir.mkdir(exist_ok=True)
    out_file = out_dir / "items.json"
    out_file.write_text(json.dumps(result, indent="\t", sort_keys=True), encoding="utf-8")

    print(f"items: {len(result)} -> {out_file}")


if __name__ == "__main__":
    main()
