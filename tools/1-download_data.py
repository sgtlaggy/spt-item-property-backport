#!/usr/bin/env python3

import argparse
import http
import json
import ssl
import sys
from pathlib import Path
from typing import Any, TypedDict
from urllib import request

from env import HERE, TARKOV_DEV_FILES
from utils import hang, json_dump

QUERIES_DIR = HERE / "queries"

PARSER = argparse.ArgumentParser()
PARSER.add_argument("-Q", "--query", nargs="*")
ARGS = PARSER.parse_args()


class GQLResponse(TypedDict):
    data: dict[str, list] | None
    errors: dict[str, Any] | None


def main():
    if ARGS.query:
        for basename in ARGS.query:
            query_file = QUERIES_DIR / f"{basename}.gql"
            if not query_file.exists():
                print(f"Error: {query_file.name} not found.", file=sys.stderr)
                continue
            download_query(query_file)
    else:
        for query_file in QUERIES_DIR.glob("*.gql"):
            download_query(query_file)


def download_query(fp: Path):
    name = fp.stem
    out_file = TARKOV_DEV_FILES / f"{name}.json"

    try:
        download(fp.read_text(), out_file)
    except Exception as e:
        print(f"Error downloading {name}: {e}", file=sys.stderr)
    else:
        print(f"Successfully downloaded {name}.")


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
