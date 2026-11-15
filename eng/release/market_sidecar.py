"""Compatibility entry point for the independently built Market image."""
from sidecar_package import ROOT, NAME, INFO, RIDS, verify_pair, stage, verify_archives
import argparse
import json
import os
from pathlib import Path


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=['stage', 'verify', 'export'])
    parser.add_argument('directory', type=Path)
    parser.add_argument('--source', type=Path)
    parser.add_argument('--rid', choices=RIDS)
    parser.add_argument('--version')
    args = parser.parse_args()
    lock = json.loads((ROOT / 'eng/release/market-sidecar.lock.json').read_text())
    public = (ROOT / 'GPG-PUBLIC-KEY.asc').read_bytes()
    if args.mode == 'stage':
        stage(args.source, args.directory, args.rid, lock, public,
              os.environ.get('GPG_PRIVATE_KEY', ''), os.environ.get('GPG_PASSPHRASE', ''))
    else:
        verify_archives(args.directory, args.version, lock, public, export=args.mode == "export")
