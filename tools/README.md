# Data Tools

## Notes

See the `.env` file for environment variables that can be used to override directories mentioned here.

## 1. Copy SPT templates

Copy `items.json` from `SPT/SPT_Data/database/templates/` to `tools/spt/`. Alternatively set the `SPT_DB_TEMPLATES` environment variable or `__DEFAULT_SPT_DB_TEMPLATES` variable in `tools/env.py`.

## 2. Copy WTT Backport templates

Copy all json files from `SPT/user/mods/WTT-ContentBackport/db/CustomItems` to `tools/wtt/`. Alternatively set the `WTT_BACKPORT_DB` environment variable or `__DEFAULT_WTT_BACKPORT_DB` variable in `tools/env.py`.

## 3. Run scripts

Requires Python. Latest (3.14) should work, no external libraries needed. Can be run by double-clicking or on command line. If running on command line, add any argument to remove the `Press RETURN to exit.` and immediately exit.

- `tools/1-download_data.py`

  - Download live item and quest data from `json.tarkov.dev` into `tools/dev/`
  - `tarkov.dev`'s GraphQL endpoint has been down since 2026-07-21, so this reads the flat JSON API the site itself is built from
  - Downloads are chunked and resumable, since a single connection to the host is slow
  - The payload is reshaped by `tools/normalise_items.py` / `tools/normalise_tasks.py` into the same layout the old GraphQL queries returned, so the following steps are unchanged

- `tools/2-refactor_data.py`

  - Cleanup, simplify, and make some arbitrary changes to data for easier processing
  - Outputs to `tools/tmp/`

- `tools/3-compile_changes.py`

  - Check data from Tarkov.Dev against SPT and WTT backport templates
  - Outputs 3 files:

    - `tools/tmp/review_items.json` - new and old values
    - `Resources/db/items.json` - only new values

      - `ConflictingItems` is output as `[items_added, items_removed]` for mod compatibility.

    - `Resources/db/flea.json` - ids of items that trade on the flea market in live Tarkov

      - An item only has a 24h average price when players actually trade it, so the mod uses this list to lift SPT's flea blacklist. No price value is written anywhere.
