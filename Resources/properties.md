Here is a categorized list of all properties currently updated by the mod.

All are applied by default but they can be individually disabled by adding
them to the ‘ExcludeProperties’ list in ‘config.json’.
To do so, wrap each property name in double quotes and add a comma between properties.

## Examples

default
```
    "ExcludeProperties": [],
```

keep SPT ammo stack sizes
```
    "ExcludeProperties": ["StackMaxSize"],
```

keep SPT ammo stack sizes + key use count
```
    "ExcludeProperties": ["StackMaxSize", "MaximumNumberOfUsages"],
```


## Properties

### Generic
- CanSellOnRagfair    (BSG flea blacklist)
- ConflictingItems    (item incompatibility)

### Weapons, mods
- Accuracy
- Ergonomics
- Loudness
- Recoil
- Velocity
- CameraSnap
- CenterOfImpact
- DeviationCurve
- DeviationMax
- RecoilAngle
- RecolDispersion   (not misspelled)
- RecoilForceBack
- RecoilForceUp

### Magazines
- CheckTimeModifier
- LoadUnloadModifier
- MalfunctionChance

### Ammo
- AmmoAccr        (accuracy)
- AmmoRec         (recoil)
- ArmorDamage
- BallisticCoeficient
- BulletMassGram
- Damage
- DurabilityBurnModificator
- FragmentationChance
- HeatFactor
- HeavyBleedingDelta
- InitialSpeed
- LightBleedingDelta
- MalfFeedChance
- MalfMisfireChance
- PenetrationChanceObstacle
- PenetrationPower
- PenetrationPowerDiviation  (not misspelled)
- StackMaxSize

### Armor, rigs, helmets, glasses
- BlindnessProtection
- BluntThroughput
- MousePenalty           (turn speed)
- SpeedPenaltyPercent
- WeaponErgonmicPenalty

### Headphones
- AmbientVolume
- CompressorAttack
- CompressorGain
- CompressorRelease
- CompressorThreshold
- RolloffMultiplier
- Distortion
- DryVolume

### Nightvision
- DiffuseIntensity
- Intensity
- NoiseIntensity
- NoiseScale

### Keys
- MaximumNumberOfUsages
