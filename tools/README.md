# Data Tools

## Notes
See the `.env` file for environment variables that can be used to override directories mentioned here.

## 1. Copy SPT templates
Copy `items.json` from `SPT/SPT_Data/database/templates/` to `tools/spt/`.
Alternatively set the `SPT_DB_TEMPLATES` environment variable or `__DEFAULT_SPT_DB_TEMPLATES` variable in `tools/env.py`.

## 2. Copy WTT Backport templates
Copy all json files from `SPT/user/mods/WTT-ContentBackport/db/CustomItems` to `tools/wtt/`.
Alternatively set the `WTT_BACKPORT_DB` environment variable or `__DEFAULT_WTT_BACKPORT_DB` variable in `tools/env.py`.

## 3. Run scripts
Requires Python. Latest (3.14) should work, no external libraries needed.

- `tools/1-download_data.py`
  - Query tarkov.dev API for live ammo and item data
  - Outputs to `tools/dev/`
- `tools/2-fix_tdev_format.py`
  - Merge ammo and items into one file and rename properties to match SPT database.
  - Outputs to `tools/tmp/`
- `tools/3-compare_items.py`
  - Check data from Tarkov.Dev against SPT and WTT backport templates
  - Outputs 2 files:
    - `tools/tmp/review_items.json` - new and old values
    - `Resources/db/items.json` - only new values
      - `ConflictingItems` is output as `[items_added, items_removed]` for mod compatibility.
