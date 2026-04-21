# Tarkov.Dev GraphQL Queries

The scripts don't currently have a GraphQL client, but these can be run
manually on the [Tarkov.Dev API Playground](https://api.tarkov.dev/).

These should be saved as `.json` files in the `tools/dev/`
directory, named `ammo.json` and `items.json` respectively.


## Ammo
```conf
{
  ammo {
    item {
      id
    }
    penetrationChance
    penetrationPower
    penetrationPowerDeviation
    accuracyModifier
    recoilModifier
    initialSpeed
    stackMaxSize
    damage
    armorDamage
    lightBleedModifier
    heavyBleedModifier
    weight
  }
}
```

## Items
```conf
{
  items {
    id
    weight
    accuracyModifier
    recoilModifier
    ergonomicsModifier
    blocksHeadphones
    velocity
    loudness
    stackMaxSize
    conflictingItems {
      id
    }
  }
}
```
