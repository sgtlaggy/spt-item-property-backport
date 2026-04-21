# Data Tools

## Notes
See the `.envrc` file for environment variables that can be used to override directories mentioned here.

## 1. Save live data
See [queries.md](queries.md) for Tarkov.Dev API queries.

Place query results in `tools/dev/`.

## 2. Copy SPT templates
Copy `items.json` from `SPT/SPT_Data/database/templates/` to `tools/spt/`.

## 3. Copy WTT Backport templates
Copy all json files from `SPT/user/mods/WTT-ContentBackport/db/CustomItems` to `tools/wtt/`.

## 4. Run scripts
Requires Python. Latest (3.14) should work, no external libraries needed.

- `tools/1-fix_tdev_format.py`
  - Merge ammo and items into one file and rename properties to match SPT database.
  - Outputs to `tools/tmp/`
- `tools/2-compare_items.py`
  - Check data from Tarkov.Dev against SPT and WTT backport templates
  - Outputs 2 files:
    - `tools/tmp/review_items.json` - new and old values
    - `Resources/db/items.json` - only new values
      - `ConflictingItems` is output as `[items_added, items_removed]` for mod compatibility.
