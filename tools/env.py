import os
import re
from pathlib import Path

__FILE = Path(__file__)
HERE = __FILE.parent

env_file = HERE / ".env"
if env_file.exists():
    for line in env_file.read_text().splitlines():
        m = re.match(r"^export (?P<var>.+)=(?P<value>.*)", line)
        if m and (value := m.group("value").strip()):
            os.environ[m.group("var").strip()] = value

# These paths are relative to this file.
# Absolute paths can be used instead.
# If on Windows ensure there is no trailing backslash \ before the closing quote.
# e.g. r"C:\path\to\files"  not  r"C:\path\to\files\"
__DEFAULT_TARKOV_DEV_FILES = r"dev"
__DEFAULT_SPT_DB = r"spt"
__DEFAULT_SPT_DB_TEMPLATES = r"spt/templates"
__DEFAULT_WTT_BACKPORT_DB = r"wtt"
__DEFAULT_OUT_DIR = "../Resources/db"

TARKOV_DEV_FILES = Path(
    os.getenv("TARKOV_DEV_FILES") or (HERE / __DEFAULT_TARKOV_DEV_FILES)
)
SPT_DB = Path(os.getenv("SPT_DB") or (HERE / __DEFAULT_SPT_DB))
SPT_DB_TEMPLATES = Path(os.getenv("SPT_DB_TEMPLATES") or __DEFAULT_SPT_DB_TEMPLATES)
WTT_BACKPORT_DB = Path(
    os.getenv("WTT_BACKPORT_DB") or (HERE / __DEFAULT_WTT_BACKPORT_DB)
)
OUT_DIR = Path(os.getenv("OUT_DIR") or (HERE / __DEFAULT_OUT_DIR))

QUERIES_DIR = HERE / "queries"
TMP_DIR = HERE / "tmp"

__all__ = (
    "OUT_DIR",
    "QUERIES_DIR",
    "SPT_DB",
    "SPT_DB_TEMPLATES",
    "TARKOV_DEV_FILES",
    "TMP_DIR",
    "WTT_BACKPORT_DB",
)
