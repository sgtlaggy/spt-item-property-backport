using System.Text.Json;
using System.Text.Json.Nodes;

namespace ItemPropertyBackport;

/// <summary>
/// Builds the mod's db files (items / prices / quests) directly from the
/// json.tarkov.dev payload, producing the exact same structure as the upstream
/// Resources/db files:
///   { "id": { "properties": {...}, "special_properties": {...} } }
/// Only values that differ from the current SPT templates are emitted, which is
/// what the mod expects.
/// </summary>
public static class LiveDataConverter
{
    private static readonly HashSet<string> IgnoreCures = new(StringComparer.Ordinal) { "RadExposure" };

    private static readonly HashSet<string> IgnoreBuffs = new(StringComparer.Ordinal)
    {
        "Contusion",
        "Fracture",
        "FrostbiteBuff",
        "HeavyBleeding",
        "LightBleeding",
        "UnknownToxin",
        "ZombieInfection",
    };

    /// <summary>Buff types tarkov.dev never exposes; dropping them mirrors upstream.</summary>
    private static readonly HashSet<string> SkipBuffTypes = new(StringComparer.Ordinal)
    {
        "Contusion",
        "Fracture",
        "FrostbiteBuff",
        "HeavyBleeding",
        "LightBleeding",
        "UnknownToxin",
        "ZombieInfection",
    };

    private static readonly Dictionary<string, string> BuffTypeAlias = new(StringComparer.Ordinal)
    {
        ["Skill"] = "SkillRate",
        ["Removeallbloodlosses"] = "RemoveAllBloodLosses",
    };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    // ---------------------------------------------------------------- helpers

    private static JsonNode? Get(JsonNode? node, string name)
    {
        return node is JsonObject obj && obj.TryGetPropertyValue(name, out var value) ? value : null;
    }

    private static double? Num(JsonNode? node)
    {
        if (node is not JsonValue value || value.GetValueKind() != JsonValueKind.Number)
        {
            return null;
        }

        if (value.TryGetValue<double>(out var d))
        {
            return d;
        }

        if (value.TryGetValue<int>(out var i))
        {
            return i;
        }

        if (value.TryGetValue<long>(out var l))
        {
            return l;
        }

        if (value.TryGetValue<decimal>(out var m))
        {
            return (double)m;
        }

        return null;
    }

    private static int? Int(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        var kind = value.GetValueKind();
        if (kind == JsonValueKind.Number)
        {
            var d = Num(node);
            // truncates like Python's int()
            return d is null ? null : (int)d.Value;
        }

        // some SPT templates store numbers as strings, e.g. armorClass "0"
        if (kind == JsonValueKind.String && int.TryParse(value.GetValue<string>(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string? Str(JsonNode? node)
    {
        if (node is JsonValue value && value.GetValueKind() == JsonValueKind.String)
        {
            return value.GetValue<string>();
        }

        return null;
    }

    private static bool Same(JsonNode? a, JsonNode? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        var ka = a.GetValueKind();
        var kb = b.GetValueKind();

        if (ka == JsonValueKind.Number && kb == JsonValueKind.Number)
        {
            var na = Num(a);
            var nb = Num(b);
            return na is not null && nb is not null && Math.Abs(na.Value - nb.Value) < 1e-9;
        }

        if (ka != kb)
        {
            return false;
        }

        switch (ka)
        {
            case JsonValueKind.String:
                return string.Equals(a.GetValue<string>(), b.GetValue<string>(), StringComparison.Ordinal);
            case JsonValueKind.True:
            case JsonValueKind.False:
                return a.GetValue<bool>() == b.GetValue<bool>();
            case JsonValueKind.Array:
            {
                var aa = a.AsArray();
                var ba = b.AsArray();
                if (aa.Count != ba.Count)
                {
                    return false;
                }

                for (var i = 0; i < aa.Count; i++)
                {
                    if (!Same(aa[i], ba[i]))
                    {
                        return false;
                    }
                }

                return true;
            }
            case JsonValueKind.Object:
            {
                var ao = a.AsObject();
                var bo = b.AsObject();
                if (ao.Count != bo.Count)
                {
                    return false;
                }

                foreach (var (key, value) in ao)
                {
                    if (!bo.TryGetPropertyValue(key, out var other) || !Same(value, other))
                    {
                        return false;
                    }
                }

                return true;
            }
            default:
                return true;
        }
    }

    private static bool SameAsSorted(JsonNode? live, JsonNode? spt)
    {
        var l = SortedStrings(live);
        var s = SortedStrings(spt);
        if (l is null || s is null)
        {
            return l is null && s is null;
        }

        return l.SequenceEqual(s, StringComparer.Ordinal);
    }

    private static List<string>? SortedStrings(JsonNode? node)
    {
        if (node is not JsonArray arr)
        {
            return null;
        }

        var list = new List<string>();
        foreach (var item in arr)
        {
            var s = Str(item);
            if (s is not null)
            {
                list.Add(s);
            }
        }

        list.Sort(StringComparer.Ordinal);
        return list;
    }

    private static JsonArray ToJsonArray(IEnumerable<string> values)
    {
        var arr = new JsonArray();
        foreach (var v in values)
        {
            arr.Add(JsonValue.Create(v));
        }

        return arr;
    }

    private static void Put(Dictionary<string, JsonNode?> target, string name, JsonNode? value)
    {
        if (value is not null)
        {
            target[name] = value.DeepClone();
        }
    }

    private static void Put(Dictionary<string, JsonNode?> target, string name, double? value)
    {
        if (value is not null)
        {
            target[name] = JsonValue.Create(value.Value);
        }
    }

    private static void Put(Dictionary<string, JsonNode?> target, string name, int? value)
    {
        if (value is not null)
        {
            target[name] = JsonValue.Create(value.Value);
        }
    }

    private static void Put(Dictionary<string, JsonNode?> target, string name, string? value)
    {
        if (value is not null)
        {
            target[name] = JsonValue.Create(value);
        }
    }

    // ------------------------------------------------------- live -> spt names

    /// <summary>
    /// Turns one json.tarkov.dev item into the SPT property names the mod applies,
    /// running the same cleanup the upstream refactor script does.
    /// </summary>
    private static Dictionary<string, JsonNode?> ConvertLiveProperties(
        JsonObject item,
        out Dictionary<string, JsonNode?> special
    )
    {
        var props = new Dictionary<string, JsonNode?>();
        special = new Dictionary<string, JsonNode?>();

        var p = Get(item, "properties") as JsonObject;
        var types = Get(item, "types") as JsonArray;
        var typeNames = new HashSet<string>(StringComparer.Ordinal);
        if (types is not null)
        {
            foreach (var t in types)
            {
                var s = Str(t);
                if (s is not null)
                {
                    typeNames.Add(s);
                }
            }
        }

        // generic (top level in tarkov.dev, SPT keeps them on _props)
        Put(props, "Accuracy", Get(item, "accuracyModifier"));
        Put(props, "Ergonomics", Get(item, "ergonomicsModifier"));
        Put(props, "Loudness", Get(item, "loudness"));
        Put(props, "Recoil", Get(item, "recoilModifier"));
        Put(props, "Velocity", Get(item, "velocity"));

        // flea blacklist
        props["CanSellOnRagfair"] = JsonValue.Create(!typeNames.Contains("noFlea"));

        // conflicting items are handled as a special property
        var conflicting = SortedStrings(Get(item, "conflictingItems"));
        if (conflicting is not null)
        {
            special["ConflictingItems"] = ToJsonArray(conflicting);
        }

        switch (Str(Get(p, "propertiesType")))
        {
            case "ItemPropertiesAmmo":
                Put(props, "ammoAccr", Scale100(Num(Get(p, "accuracyModifier"))));
                Put(props, "ammoRec", Scale100(Num(Get(p, "recoilModifier"))));
                Put(props, "ArmorDamage", Get(p, "armorDamage"));
                Put(props, "BallisticCoeficient", Get(p, "ballisticCoeficient"));
                Put(props, "BulletMassGram", Get(p, "bulletMassGrams"));
                Put(props, "Damage", Get(p, "damage"));
                Put(props, "DurabilityBurnModificator", Get(p, "durabilityBurnFactor"));
                Put(props, "MalfFeedChance", Get(p, "failureToFeedChance"));
                Put(props, "FragmentationChance", Get(p, "fragmentationChance"));
                Put(props, "HeatFactor", Get(p, "heatFactor"));
                Put(props, "HeavyBleedingDelta", Get(p, "heavyBleedModifier"));
                Put(props, "InitialSpeed", Get(p, "initialSpeed"));
                Put(props, "LightBleedingDelta", Get(p, "lightBleedModifier"));
                Put(props, "MalfMisfireChance", Get(p, "misfireChance"));
                Put(props, "PenetrationChanceObstacle", Get(p, "penetrationChance"));
                Put(props, "PenetrationPower", Get(p, "penetrationPower"));
                Put(props, "PenetrationPowerDiviation", Get(p, "penetrationPowerDeviation"));
                Put(props, "StackMaxSize", Get(p, "stackMaxSize"));
                break;

            case "ItemPropertiesArmor":
                PutArmor(props, p);
                break;

            case "ItemPropertiesArmorAttachment":
                PutArmor(props, p);
                Put(props, "BlindnessProtection", Get(p, "blindnessProtection"));
                break;

            case "ItemPropertiesBarrel":
                Put(props, "CenterOfImpact", Get(p, "centerOfImpact"));
                Put(props, "DeviationCurve", Get(p, "deviationCurve"));
                Put(props, "DeviationMax", Get(p, "deviationMax"));
                break;

            case "ItemPropertiesChestRig":
                PutArmor(props, p);
                props.Remove("armorColliders");
                break;

            case "ItemPropertiesFoodDrink":
                Put(props, "Energy", Get(p, "energy"));
                Put(props, "Hydration", Get(p, "hydration"));
                Put(props, "MaxResource", Get(p, "units"));
                PutBuffs(special, Get(p, "stimEffects"));
                break;

            case "ItemPropertiesGlasses":
                Put(props, "BlindnessProtection", Get(p, "blindnessProtection"));
                Put(props, "BluntThroughput", Get(p, "bluntThroughput"));
                Put(props, "armorClass", Get(p, "class"));
                Put(props, "Durability", Get(p, "durability"));
                Put(props, "weaponErgonomicPenalty", Get(p, "ergoPenalty"));
                Put(props, "ArmorMaterial", Get(p, "material"));
                Put(props, "RepairCost", Get(p, "repairCost"));
                break;

            case "ItemPropertiesHeadphone":
                Put(props, "AmbientVolume", Get(p, "ambientVolume"));
                Put(props, "CompressorAttack", Get(p, "compressorAttack"));
                Put(props, "CompressorGain", Get(p, "compressorGain"));
                Put(props, "CompressorRelease", Get(p, "compressorRelease"));
                Put(props, "CompressorThreshold", Get(p, "compressorThreshold"));
                Put(props, "RolloffMultiplier", Get(p, "distanceModifier"));
                Put(props, "Distortion", Get(p, "distortion"));
                Put(props, "DryVolume", Get(p, "dryVolume"));
                break;

            case "ItemPropertiesHelmet":
                PutArmor(props, p);
                Put(props, "BlindnessProtection", Get(p, "blindnessProtection"));
                Put(props, "BlocksEarpiece", Get(p, "blocksHeadset"));
                Put(props, "DeafStrength", Get(p, "deafening"));
                Put(props, "ricochetX", Get(p, "ricochetX"));
                Put(props, "ricochetY", Get(p, "ricochetY"));
                Put(props, "ricochetZ", Get(p, "ricochetZ"));
                break;

            case "ItemPropertiesKey":
                Put(props, "MaximumNumberOfUsage", Get(p, "uses"));
                break;

            case "ItemPropertiesMagazine":
                Put(props, "CheckTimeModifier", Get(p, "ammoCheckModifier"));
                Put(props, "LoadUnloadModifier", Get(p, "loadModifier"));
                Put(props, "MalfunctionChance", Get(p, "malfunctionChance"));
                break;

            case "ItemPropertiesMedicalItem":
                PutCures(props, Get(p, "cures"));
                Put(props, "MaxHpResource", Get(p, "uses"));
                Put(props, "medUseTime", Get(p, "useTime"));
                break;

            case "ItemPropertiesMedKit":
                PutCures(props, Get(p, "cures"));
                Put(props, "MaxHpResource", Get(p, "hitpoints"));
                Put(props, "HeavyBleeding", Get(p, "hpCostHeavyBleeding"));
                Put(props, "LightBleeding", Get(p, "hpCostLightBleeding"));
                Put(props, "hpResourceRate", Get(p, "maxHealPerUse"));
                Put(props, "medUseTime", Get(p, "useTime"));
                break;

            case "ItemPropertiesNightVision":
                Put(props, "DiffuseIntensity", Get(p, "diffuseIntensity"));
                Put(props, "Intensity", Get(p, "intensity"));
                Put(props, "NoiseIntensity", Get(p, "noiseIntensity"));
                Put(props, "NoiseScale", Get(p, "noiseScale"));
                break;

            case "ItemPropertiesPainkiller":
                PutCures(props, Get(p, "cures"));
                Put(props, "Energy", Get(p, "energyImpact"));
                Put(props, "Hydration", Get(p, "hydrationImpact"));
                Put(props, "PainDuration", Get(p, "painkillerDuration"));
                Put(props, "MaxHpResource", Get(p, "uses"));
                Put(props, "medUseTime", Get(p, "useTime"));
                break;

            case "ItemPropertiesStim":
                PutCures(props, Get(p, "cures"));
                PutBuffs(special, Get(p, "stimEffects"));
                Put(props, "medUseTime", Get(p, "useTime"));
                break;

            case "ItemPropertiesSurgicalKit":
                PutCures(props, Get(p, "cures"));
                Put(props, "healthPenaltyMax", Get(p, "maxLimbHealth"));
                Put(props, "healthPenaltyMin", Get(p, "minLimbHealth"));
                Put(props, "MaxHpResource", Get(p, "uses"));
                Put(props, "medUseTime", Get(p, "useTime"));
                break;

            case "ItemPropertiesWeapon":
                Put(props, "CameraSnap", Get(p, "cameraSnap"));
                Put(props, "CenterOfImpact", Get(p, "centerOfImpact"));
                Put(props, "DeviationCurve", Get(p, "deviationCurve"));
                Put(props, "DeviationMax", Get(p, "deviationMax"));
                Put(props, "RecoilAngle", Get(p, "recoilAngle"));
                Put(props, "RecolDispersion", Get(p, "recoilDispersion"));
                Put(props, "RecoilForceBack", Get(p, "recoilHorizontal"));
                Put(props, "RecoilForceUp", Get(p, "recoilVertical"));
                break;
        }

        // --- refactor pass -------------------------------------------------

        // percentages are stored as fractions by tarkov.dev
        foreach (var name in new[]
                 {
                     "weaponErgonomicPenalty", "speedPenaltyPercent", "mousePenalty",
                     "CheckTimeModifier", "LoadUnloadModifier",
                 })
        {
            if (props.TryGetValue(name, out var node) && Num(node) is { } value && value != 0)
            {
                props[name] = JsonValue.Create(Math.Round(value * 100, 2));
            }
        }

        // single use medical items report 1, SPT wants 0
        if (Int(props.GetValueOrDefault("MaxHpResource")) == 1)
        {
            props["MaxHpResource"] = JsonValue.Create(0);
        }

        // health effects are stored in their own dictionary
        if (props.ContainsKey("Energy"))
        {
            var effects = new JsonObject
            {
                ["Energy"] = new JsonObject { ["value"] = props["Energy"]?.DeepClone() },
                ["Hydration"] = new JsonObject { ["value"] = props.GetValueOrDefault("Hydration")?.DeepClone() },
            };
            props.Remove("Energy");
            props.Remove("Hydration");
            special["EffectsHealth"] = effects;
        }

        // ricochet parameters
        if (props.ContainsKey("ricochetX"))
        {
            props["RicochetParams"] = new JsonObject
            {
                ["x"] = props["ricochetX"]?.DeepClone(),
                ["y"] = props["ricochetY"]?.DeepClone(),
                ["z"] = props["ricochetZ"]?.DeepClone(),
            };
            props.Remove("ricochetX");
            props.Remove("ricochetY");
            props.Remove("ricochetZ");
        }

        // tarkov.dev does not report colliders for glasses
        if (
            typeNames.Contains("glasses")
            && !props.ContainsKey("armorColliders")
            && Num(props.GetValueOrDefault("Durability")) is { } durability
            && durability != 0
        )
        {
            props["armorColliders"] = ToJsonArray(new[] { "Eyes" });
        }

        // cures become a dictionary of effect -> properties
        if (props.TryGetValue("EffectsDamage", out var curesNode) && curesNode is JsonArray cures)
        {
            var converted = new JsonObject();
            foreach (var cureNode in cures)
            {
                var cure = Str(cureNode);
                if (cure is null)
                {
                    continue;
                }

                if (cure == "LostLimb")
                {
                    cure = "DestroyedPart";
                }

                var entry = new JsonObject();
                switch (cure)
                {
                    case "Pain":
                        if (Num(props.GetValueOrDefault("PainDuration")) is { } duration && duration != 0)
                        {
                            entry["duration"] = JsonValue.Create(duration);
                        }

                        break;
                    case "DestroyedPart":
                        entry["healthPenaltyMax"] = JsonValue.Create(
                            (int)((Num(props.GetValueOrDefault("healthPenaltyMax")) ?? 0) * 100)
                        );
                        entry["healthPenaltyMin"] = JsonValue.Create(
                            (int)((Num(props.GetValueOrDefault("healthPenaltyMin")) ?? 0) * 100)
                        );
                        break;
                    default:
                        if (cure.EndsWith("Bleeding", StringComparison.Ordinal))
                        {
                            if (Num(props.GetValueOrDefault(cure)) is { } cost && cost != 0)
                            {
                                entry["cost"] = JsonValue.Create(cost);
                            }
                        }

                        break;
                }

                converted[cure] = entry;
            }

            props.Remove("EffectsDamage");
            props.Remove("PainDuration");
            props.Remove("healthPenaltyMax");
            props.Remove("healthPenaltyMin");
            props.Remove("HeavyBleeding");
            props.Remove("LightBleeding");
            special["EffectsDamage"] = converted;
        }

        return props;
    }

    private static void PutArmor(Dictionary<string, JsonNode?> props, JsonObject? p)
    {
        Put(props, "ArmorType", Get(p, "armorType"));
        Put(props, "BluntThroughput", Get(p, "bluntThroughput"));
        Put(props, "armorClass", Get(p, "class"));
        Put(props, "Durability", Get(p, "durability"));
        Put(props, "weaponErgonomicPenalty", Get(p, "ergoPenalty"));
        Put(props, "ArmorMaterial", Get(p, "material"));
        Put(props, "RepairCost", Get(p, "repairCost"));
        Put(props, "speedPenaltyPercent", Get(p, "speedPenalty"));
        Put(props, "mousePenalty", Get(p, "turnPenalty"));

        var colliders = new SortedSet<string>(StringComparer.Ordinal);
        if (Get(p, "zones") is JsonArray zones)
        {
            foreach (var zone in zones)
            {
                var s = Str(zone);
                if (s is null)
                {
                    continue;
                }

                if (s.StartsWith("Collider Type ", StringComparison.Ordinal))
                {
                    colliders.Add(s["Collider Type ".Length..]);
                }
                // "Armor Zone ..." entries are the plate zones upstream drops
            }
        }

        if (colliders.Count > 0)
        {
            props["armorColliders"] = ToJsonArray(colliders);
        }
    }

    private static void PutBuffs(Dictionary<string, JsonNode?> special, JsonNode? stimEffects)
    {
        if (stimEffects is not JsonArray effects)
        {
            return;
        }

        var buffs = new JsonArray();
        foreach (var effect in effects)
        {
            if (effect is not JsonObject e)
            {
                continue;
            }

            var raw = Str(Get(e, "type"));
            if (raw is null)
            {
                continue;
            }

            raw = BuffTypeAlias.GetValueOrDefault(raw, raw);
            if (SkipBuffTypes.Contains(raw))
            {
                continue;
            }

            var percent = Get(e, "percent") is JsonValue pv && pv.GetValueKind() == JsonValueKind.True;
            buffs.Add(
                new JsonObject
                {
                    ["AbsoluteValue"] = JsonValue.Create(!percent),
                    ["BuffType"] = JsonValue.Create(raw),
                    ["Chance"] = Get(e, "chance")?.DeepClone(),
                    ["Delay"] = Get(e, "delay")?.DeepClone(),
                    ["Duration"] = Get(e, "duration")?.DeepClone(),
                    ["SkillName"] = JsonValue.Create(Str(Get(e, "skill")) ?? ""),
                    ["Value"] = Get(e, "value")?.DeepClone(),
                }
            );
        }

        if (buffs.Count > 0)
        {
            special["Buffs"] = buffs;
        }
    }

    private static void PutCures(Dictionary<string, JsonNode?> props, JsonNode? cures)
    {
        if (cures is JsonArray arr && arr.Count > 0)
        {
            props["EffectsDamage"] = arr.DeepClone();
        }
    }

    private static double? Scale100(double? value)
    {
        return value is null ? null : Math.Round(value.Value * 100);
    }

    // -------------------------------------------------------------- item diff

    private static JsonObject? DiffItem(
        Dictionary<string, JsonNode?> props,
        JsonObject sptProps,
        Dictionary<string, JsonNode?> special,
        JsonNode? stimulatorBuffs
    )
    {
        var hasSlots = Get(sptProps, "Slots") is JsonArray { Count: > 0 };
        var changed = new JsonObject();

        foreach (var (name, live) in props)
        {
            if (live is null)
            {
                continue;
            }

            var spt = Get(sptProps, name);

            switch (name)
            {
                case "armorClass":
                    if (hasSlots || Int(live) == Int(spt))
                    {
                        continue;
                    }

                    break;
                case "Durability":
                    if (hasSlots || Same(live, spt))
                    {
                        continue;
                    }

                    changed["MaxDurability"] = live.DeepClone();
                    break;
                case "armorColliders":
                    if (hasSlots || SameAsSorted(live, spt))
                    {
                        continue;
                    }

                    break;
                default:
                    if (Same(live, spt))
                    {
                        continue;
                    }

                    break;
            }

            changed[name] = live.DeepClone();
        }

        var specialOut = new JsonObject();

        // conflicting items are shipped as [added, removed]
        if (special.TryGetValue("ConflictingItems", out var liveConflicting))
        {
            var liveSet = new HashSet<string>(
                SortedStrings(liveConflicting) ?? new List<string>(),
                StringComparer.Ordinal
            );
            var sptSet = new HashSet<string>(
                SortedStrings(Get(sptProps, "ConflictingItems")) ?? new List<string>(),
                StringComparer.Ordinal
            );

            if (!liveSet.SetEquals(sptSet))
            {
                var added = liveSet.Except(sptSet).OrderBy(x => x, StringComparer.Ordinal);
                var removed = sptSet.Except(liveSet).OrderBy(x => x, StringComparer.Ordinal);
                specialOut["ConflictingItems"] = new JsonArray(ToJsonArray(added), ToJsonArray(removed));
            }
        }

        if (special.TryGetValue("Buffs", out var liveBuffsNode))
        {
            var diff = DiffBuffs(liveBuffsNode, sptProps, stimulatorBuffs);
            if (diff is not null)
            {
                specialOut["Buffs"] = diff;
            }
        }

        if (
            special.TryGetValue("EffectsDamage", out var liveCures)
            && liveCures is JsonObject liveCuresObj
        )
        {
            var diff = DiffDictionary(liveCuresObj, Get(sptProps, "effects_damage") as JsonObject, IgnoreCures);
            if (diff.Count > 0)
            {
                specialOut["EffectsDamage"] = diff;
            }
        }

        if (
            special.TryGetValue("EffectsHealth", out var liveEffects)
            && liveEffects is JsonObject liveEffectsObj
        )
        {
            var sptEffects = Get(sptProps, "effects_health") as JsonObject;
            var diff = new JsonObject();
            foreach (var (effect, data) in liveEffectsObj)
            {
                var liveValue = Num(Get(data as JsonObject, "value")) ?? 0;
                var sptValue = Num(Get(Get(sptEffects, effect) as JsonObject, "value"));

                if (sptValue is null)
                {
                    if (liveValue == 0)
                    {
                        continue;
                    }

                    diff[effect] = data?.DeepClone();
                }
                else if (Math.Abs(liveValue - sptValue.Value) > 1e-9)
                {
                    diff[effect] = liveValue == 0 ? null : data?.DeepClone();
                }
            }

            if (diff.Count > 0)
            {
                specialOut["EffectsHealth"] = diff;
            }
        }

        if (changed.Count == 0 && specialOut.Count == 0)
        {
            return null;
        }

        var entry = new JsonObject();
        if (changed.Count > 0)
        {
            entry["properties"] = changed;
        }

        if (specialOut.Count > 0)
        {
            entry["special_properties"] = specialOut;
        }

        return entry;
    }

    private static JsonObject? DiffBuffs(
        JsonNode? liveBuffsNode,
        JsonObject sptProps,
        JsonNode? stimulatorBuffs
    )
    {
        var live = new List<JsonObject>();
        if (liveBuffsNode is JsonArray arr)
        {
            foreach (var b in arr)
            {
                if (b is JsonObject bo)
                {
                    live.Add(bo);
                }
            }
        }

        var group = Str(Get(sptProps, "StimulatorBuffs"));
        var spt = new List<JsonObject>();
        if (group is not null && Get(stimulatorBuffs, group) is JsonArray sptArr)
        {
            foreach (var b in sptArr)
            {
                if (b is JsonObject bo)
                {
                    spt.Add(bo);
                }
            }
        }

        var liveDiff = new List<JsonObject?>();
        var sptDiff = new List<JsonObject?>();
        for (var i = 0; i < spt.Count; i++)
        {
            liveDiff.Add(null);
            sptDiff.Add(null);
        }

        foreach (var (index, buff) in spt.Select((b, i) => (i, b)))
        {
            var (similarIndex, similarity) = MostSimilar(live, buff);
            if (similarity == buff.Count && similarIndex >= 0)
            {
                live.RemoveAt(similarIndex);
            }
            else if (similarity > 0)
            {
                liveDiff[index] = live[similarIndex];
                live.RemoveAt(similarIndex);
                sptDiff[index] = buff;
            }
        }

        liveDiff.AddRange(live);

        var diff = new JsonObject();
        var count = Math.Max(liveDiff.Count, sptDiff.Count);
        for (var i = 0; i < count; i++)
        {
            var lbuff = i < liveDiff.Count ? liveDiff[i] : null;
            var sbuff = i < sptDiff.Count ? sptDiff[i] : null;

            if (lbuff is null && sbuff is null)
            {
                continue;
            }

            if (sbuff is not null && IgnoreBuffs.Contains(Str(Get(sbuff, "BuffType")) ?? ""))
            {
                continue;
            }

            diff[i.ToString()] = lbuff is null ? null : lbuff.DeepClone();
        }

        return diff.Count > 0 ? diff : null;
    }

    private static (int Index, int Similarity) MostSimilar(List<JsonObject> candidates, JsonObject buff)
    {
        var bestIndex = -1;
        var bestSimilarity = 0;

        for (var i = 0; i < candidates.Count; i++)
        {
            var similarity = BuffSimilarity(buff, candidates[i]);
            if (similarity > bestSimilarity)
            {
                bestIndex = i;
                bestSimilarity = similarity;
            }
        }

        return (bestIndex, bestSimilarity);
    }

    private static int BuffSimilarity(JsonObject a, JsonObject b)
    {
        var typeA = Str(Get(a, "BuffType"));
        var typeB = Str(Get(b, "BuffType"));
        if (!string.Equals(typeA, typeB, StringComparison.Ordinal))
        {
            return 0;
        }

        var skillA = Str(Get(a, "SkillName")) ?? "";
        var skillB = Str(Get(b, "SkillName")) ?? "";
        if (!string.Equals(skillA, skillB, StringComparison.Ordinal))
        {
            return 0;
        }

        var same = 0;
        foreach (var (key, value) in a)
        {
            if (Same(value, Get(b, key)))
            {
                same++;
            }
        }

        return same;
    }

    private static JsonObject DiffDictionary(
        JsonObject live,
        JsonObject? spt,
        HashSet<string> ignoreKeys
    )
    {
        var diff = new JsonObject();

        foreach (var (key, liveValue) in live)
        {
            var sptValue = Get(spt, key) as JsonObject;
            if (!PartialMatch(liveValue as JsonObject, sptValue))
            {
                diff[key] = liveValue?.DeepClone();
            }
        }

        if (spt is not null)
        {
            foreach (var (key, _) in spt)
            {
                if (!live.ContainsKey(key) && !ignoreKeys.Contains(key))
                {
                    diff[key] = null;
                }
            }
        }

        return diff;
    }

    private static bool PartialMatch(JsonObject? partial, JsonObject? full)
    {
        if (partial is null)
        {
            return full is null;
        }

        foreach (var (key, value) in partial)
        {
            if (!Same(value, Get(full, key)))
            {
                return false;
            }
        }

        return true;
    }

    // ------------------------------------------------------------- public API

    public static string BuildItems(
        JsonNode liveRoot,
        Func<string, JsonNode?> sptProperties,
        JsonNode stimulatorBuffs
    )
    {
        var live = liveRoot["data"]?["items"] as JsonObject ?? new JsonObject();
        var result = new JsonObject();

        foreach (var (id, node) in live)
        {
            if (node is not JsonObject item)
            {
                continue;
            }

            // item does not exist in this SPT install
            if (sptProperties(id) is not JsonObject sptProps)
            {
                continue;
            }

            var props = ConvertLiveProperties(item, out var special);
            var entry = DiffItem(props, sptProps, special, stimulatorBuffs);
            if (entry is not null)
            {
                result[id] = entry;
            }
        }

        return result.ToJsonString(WriteOptions);
    }

    /// <summary>
    /// Ids of items that actually trade on the flea market in live Tarkov (they have a
    /// 24h average price). The mod only uses this to lift SPT's flea blacklist — it
    /// never writes any price value.
    /// </summary>
    public static string BuildFleaTradable(JsonNode liveRoot, Func<string, bool> existsInSpt)
    {
        var live = liveRoot["data"]?["items"] as JsonObject ?? new JsonObject();
        var ids = new JsonArray();

        foreach (var (id, node) in live)
        {
            if (node is not JsonObject item)
            {
                continue;
            }

            if (Num(Get(item, "avg24hPrice")) is null)
            {
                continue;
            }

            if (!existsInSpt(id))
            {
                continue;
            }

            ids.Add(JsonValue.Create(id));
        }

        return ids.ToJsonString(WriteOptions);
    }

    public static string BuildQuests(JsonNode liveRoot, JsonNode sptQuests)
    {
        var tasks = liveRoot["data"]?["tasks"] as JsonObject ?? new JsonObject();
        var quests = sptQuests as JsonObject ?? new JsonObject();
        var result = new JsonObject();

        foreach (var (questId, taskNode) in tasks)
        {
            if (taskNode is not JsonObject task)
            {
                continue;
            }

            if (Get(task, "objectives") is not JsonArray objectives)
            {
                continue;
            }

            var sptObjectives = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
            var finish = Get(Get(Get(quests, questId), "conditions"), "AvailableForFinish");
            if (finish is JsonArray finishArr)
            {
                foreach (var condition in finishArr)
                {
                    if (condition is JsonObject c)
                    {
                        var cid = Str(Get(c, "id"));
                        if (cid is not null)
                        {
                            sptObjectives[cid] = c;
                        }
                    }
                }
            }

            JsonObject? questChanges = null;

            foreach (var objNode in objectives)
            {
                if (objNode is not JsonObject objective)
                {
                    continue;
                }

                if (Str(Get(objective, "type")) != "buildWeapon")
                {
                    continue;
                }

                var objId = Str(Get(objective, "id"));
                if (objId is null || !sptObjectives.TryGetValue(objId, out var sptObjective))
                {
                    continue;
                }

                if (Str(Get(sptObjective, "conditionType")) != "WeaponAssembly")
                {
                    continue;
                }

                var attributes = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);

                if (Get(objective, "buildAttributes") is JsonObject buildAttributes)
                {
                    foreach (var (name, requirement) in buildAttributes)
                    {
                        attributes[name] = requirement;
                    }
                }

                var weaponId = Str(Get(objective, "item"));
                if (weaponId is not null)
                {
                    attributes["target"] = new JsonArray(JsonValue.Create(weaponId));
                }

                var containsItems = new JsonArray();
                if (Get(objective, "containsAll") is JsonArray containsAll)
                {
                    foreach (var entry in containsAll)
                    {
                        if (Str(entry) is { } itemId)
                        {
                            containsItems.Add(JsonValue.Create(itemId));
                        }
                    }
                }

                attributes["containsItems"] = containsItems;

                var categories = new JsonArray();
                if (Get(objective, "containsCategory") is JsonArray containsCategory)
                {
                    foreach (var entry in containsCategory)
                    {
                        if (Str(entry) is { } categoryId)
                        {
                            categories.Add(JsonValue.Create(categoryId));
                        }
                    }
                }

                attributes["hasItemFromCategory"] = categories;

                var changedAttributes = new JsonObject();
                foreach (var (name, liveValue) in attributes)
                {
                    var sptValue = Get(sptObjective, name);
                    if (sptValue is null)
                    {
                        continue;
                    }

                    if (!Same(liveValue, sptValue))
                    {
                        changedAttributes[name] = liveValue?.DeepClone();
                    }
                }

                if (changedAttributes.Count == 0)
                {
                    continue;
                }

                changedAttributes["id"] = JsonValue.Create(objId);
                changedAttributes["conditionType"] = JsonValue.Create("WeaponAssembly");
                changedAttributes["dynamicLocale"] = JsonValue.Create(false);

                questChanges ??= new JsonObject();
                questChanges[objId] = changedAttributes;
            }

            if (questChanges is not null)
            {
                result[questId] = questChanges;
            }
        }

        return result.ToJsonString(WriteOptions);
    }
}
