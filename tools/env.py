import os
from pathlib import Path

__FILE = Path(__file__)
HERE = __FILE.parent

# These paths are relative to this file.
# Absolute paths can be used instead.
# If on Windows ensure there is no trailing backslash \ before the closing quote.
# e.g. r"C:\path\to\files"  not  r"C:\path\to\files\"
__DEFAULT_TARKOV_DEV_FILES = r"dev"
__DEFAULT_SPT_DB_TEMPLATES = r"spt"
__DEFAULT_WTT_BACKPORT_DB = r"wtt"
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

QUERIES_DIR = HERE / "queries"
TMP_DIR = HERE / "tmp"
