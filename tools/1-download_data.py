#!/usr/bin/env python3
"""Download live item/quest data and normalise it for the refactor step.

tarkov.dev's GraphQL endpoint (https://api.tarkov.dev/graphql) has been returning
"GraphQL server unavailable" since 2026-07-21, so this pulls from the flat JSON API
that tarkov.dev itself is built from instead. The payload is then reshaped to match
what `queries/items.gql` and `queries/quests.gql` used to return, so the following
steps do not need to change.

Outputs to `dev/`:
    raw_items.json   untouched /regular/items payload
    raw_tasks.json   untouched /regular/tasks payload
    items.json       normalised items
    quests.json      normalised quests
"""

import os
import ssl
import time
import urllib.request
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

from env import TARKOV_DEV_FILES
from utils import hang

BASE_URL = "https://json.tarkov.dev/regular"

SOURCES = {
    "raw_items.json": f"{BASE_URL}/items",
    "raw_tasks.json": f"{BASE_URL}/tasks",
}

WORKERS = 6
CHUNK = 400_000
USER_AGENT = "spt-item-property-backport/1.5.2"


def total_size(url: str) -> int:
    req = urllib.request.Request(
        url,
        headers={"Range": "bytes=0-0", "User-Agent": USER_AGENT},
    )
    with urllib.request.urlopen(req, timeout=60, context=ssl.create_default_context()) as resp:
        content_range = resp.headers.get("Content-Range", "")
        if "/" not in content_range:
            msg = f"server did not report a size: {content_range!r}"
            raise ValueError(msg)
        return int(content_range.rsplit("/", 1)[1])


def fetch_range(url: str, start: int, end: int, path: Path, tries: int = 6) -> None:
    if path.exists() and path.stat().st_size == end - start + 1:
        return

    last_error = None
    for attempt in range(tries):
        try:
            req = urllib.request.Request(
                url,
                headers={
                    "Range": f"bytes={start}-{end}",
                    "User-Agent": USER_AGENT,
                    "Accept-Encoding": "identity",
                },
            )
            with urllib.request.urlopen(
                req, timeout=45, context=ssl.create_default_context()
            ) as resp:
                data = resp.read()

            if len(data) == end - start + 1:
                path.write_bytes(data)
                return

            last_error = f"short read: {len(data)} of {end - start + 1} bytes"
        except Exception as e:  # noqa: BLE001
            last_error = repr(e)

        time.sleep(1.5 * (attempt + 1))

    msg = f"chunk {start}-{end} failed: {last_error}"
    raise RuntimeError(msg)


def download(url: str, out_file: Path) -> None:
    size = total_size(url)
    parts_dir = out_file.with_suffix(out_file.suffix + ".parts")
    parts_dir.mkdir(parents=True, exist_ok=True)

    ranges = []
    start = 0
    while start < size:
        end = min(start + CHUNK - 1, size - 1)
        ranges.append((start, end))
        start = end + 1

    def work(bounds: tuple[int, int]) -> None:
        s, e = bounds
        fetch_range(url, s, e, parts_dir / f"{s}-{e}")

    with ThreadPoolExecutor(max_workers=WORKERS) as pool:
        list(pool.map(work, ranges))

    with out_file.open("wb") as out:
        for s, e in ranges:
            out.write((parts_dir / f"{s}-{e}").read_bytes())

    for s, e in ranges:
        (parts_dir / f"{s}-{e}").unlink()
    parts_dir.rmdir()

    print(f"  {out_file.name}: {out_file.stat().st_size} bytes")


def main():
    TARKOV_DEV_FILES.mkdir(parents=True, exist_ok=True)

    for name, url in SOURCES.items():
        print(f"Downloading {url}")
        download(url, TARKOV_DEV_FILES / name)

    # reshaped to the old GraphQL response layout
    import normalise_items
    import normalise_tasks

    normalise_items.main()
    normalise_tasks.main()


if __name__ == "__main__":
    main()
    hang()
