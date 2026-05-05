#!/usr/bin/env python3

from typing import TypedDict, Any
import sys
import http
import ssl
import json
from urllib import request
from pathlib import Path

from utils import json_dump, hang
from env import TARKOV_DEV_FILES

AMMO_FILE = TARKOV_DEV_FILES / "ammo.json"
AMMO_QUERY = """
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
"""

ITEMS_FILE = TARKOV_DEV_FILES / "items.json"
ITEM_QUERY = """
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
    conflictingItems {
      id
    }
  }
}
"""


class GQLResponse(TypedDict):
    data: dict[str, list] | None
    errors: dict[str, Any] | None


def main():
    try:
        download(AMMO_QUERY, AMMO_FILE)
        download(ITEM_QUERY, ITEMS_FILE)
    except Exception as e:
        print(e, file=sys.stderr)

def download(query: str, fp: Path):
    response = run_query(query)
    gql: GQLResponse = json.loads(response)
    data = unpack_gql(gql)
    json_dump(data, fp)


def unpack_gql(gql: GQLResponse) -> list:
    err = gql.get("errors")
    if err:
        msg = f"Failed query:\n{json.dumps(err, indent=2)}"
        raise ValueError(msg)

    data = gql.get("data")
    if not data:
        msg = f"Unexpected data:\n{json.dumps(err, indent=2)}."
        raise ValueError(msg)

    for val in data.values():
        return val
    else:
        msg = "Unexpected empty data."
        raise ValueError(msg)


def run_query(query: str) -> bytes:
    data = json.dumps({"query": query}).encode()
    req = request.Request(
        "https://api.tarkov.dev/graphql",
        headers={
            "Content-Type": "application/json",
            "User-Agent": "python-requests/2.33.1",  # 403 urllib is blocked
        },
        data=data,
    )
    with request.urlopen(req, context=ssl.create_default_context()) as resp:  # noqa: S310
        if resp.status != http.HTTPStatus.OK:
            msg = f"Unexpected response: {resp.status} {resp.msg}"
            raise ValueError(msg)

        return resp.read()


if __name__ == "__main__":
    main()
    hang()
