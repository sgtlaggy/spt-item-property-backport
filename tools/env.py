import os
from pathlib import Path

__FILE = Path(__file__)
HERE = __FILE.parent

# These paths are relative to this file.
__DEFAULT_TARKOV_DEV_FILES = "dev"
__DEFAULT_SPT_DB_TEMPLATES = "spt"
__DEFAULT_WTT_BACKPORT_DB = "wtt"
__DEFAULT_OUT_DIR = "../Resources/db"

TARKOV_DEV_FILES = Path(
    os.getenv("TARKOV_DEV_FILES") or (HERE / __DEFAULT_TARKOV_DEV_FILES)
)
SPT_DB_TEMPLATES = Path(
    os.getenv("SPT_DB_TEMPLATES") or (HERE / __DEFAULT_SPT_DB_TEMPLATES)
)
WTT_BACKPORT_DB = Path(
    os.getenv("WTT_BACKPORT_DB") or (HERE / __DEFAULT_WTT_BACKPORT_DB)
)
OUT_DIR = Path(os.getenv("OUT_DIR") or (HERE / __DEFAULT_OUT_DIR))
TMP_DIR = HERE / "tmp"
