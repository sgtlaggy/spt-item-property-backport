# Item Property Backport — json.tarkov.dev fork

A fork of [sgtlaggy/spt-item-property-backport](https://github.com/sgtlaggy/spt-item-property-backport)
that keeps the data current.

## Why

The upstream mod reads `Resources/db/*.json`, which the author regenerates with
`tools/1-download_data.py` against `https://api.tarkov.dev/graphql`. That endpoint has
been returning

```
HTTP 422 {"errors":["GraphQL server unavailable. Try again later."]}
```

for every query since around 2026-07-21
([the-hideout/tarkov-api#474](https://github.com/the-hideout/tarkov-api/issues/474)),
so the bundled data has been frozen on the 2026-07-15 build.

`json.tarkov.dev` serves the same underlying data as flat JSON and is still updated —
it is what tarkov.dev's own frontend reads.

## What is different

- **Data source** — `DataService` downloads from `https://json.tarkov.dev/regular/items`
  and builds the change set in-process. The GraphQL alias layout is reproduced in
  `LiveData.cs`, so the existing property-application path is untouched.
- **Auto update** — results are cached back to `db/*.json`, so the slow path only runs
  when a file is missing. Delete `db/items.json` and restart the server to refresh.
- **Prices are no longer modified** — the old `prices.json` overwrote SPT's handbook and
  flea prices with live values. That is removed; `handbook.json` and `templates/prices.json`
  are never touched.
- **Flea blacklist** — `db/flea.json` lists the items that actually trade on the flea
  market in live Tarkov (they have a 24h average price). Those items get
  `CanSellOnRagfair = true` and are dropped from `ragfair.json`'s custom blacklist.
- **Tools** — `tools/1-download_data.py` now reads json.tarkov.dev (chunked, resumable),
  `tools/normalise_*.py` reshape the payload to the old GraphQL layout so the refactor
  and compile steps are unchanged, and `3-compile_changes.py` emits `flea.json` instead
  of `prices.json`.

## Config

```json
{
    "ExcludeProperties": [],
    "IncludeItems": [],
    "ExcludeItems": [],
    "RemoveAmmoboxFleaLimit": false,
    "UpdateGunsmith": true,
    "UnblacklistPricedItems": true,
    "AutoUpdate": true,
    "AutoUpdateDays": 0
}
```

- `AutoUpdate` — when a db file is missing, rebuild it from json.tarkov.dev instead of
  reading the bundled copy.
- `AutoUpdateDays` — additionally refresh when a file is older than this many days.
  `0` (default) only refreshes when a file is missing.
- `UnblacklistPricedItems` — lift the flea blacklist for priced items.
- `AllPrices` / `UnblacklistedPrices` were removed along with the price writing.

## Install

Copy `SPT_Runtime/user/mods/ItemPropertyBackport` into your SPT install. Requires
SPT 4.1 and .NET 10.
