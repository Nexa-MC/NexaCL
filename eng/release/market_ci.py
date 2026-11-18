"""Build the pinned Market source and bind its output to this launcher CI run."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess

from manifest import release_channel
from sidecar_package import ROOT, RIDS

PACKAGE = 'Nexa.Cloud.Market.Sidecar'
REPOSITORY = 'Nexa-MC/Nexa-Cloud-Market-Sidecar'
SOURCE_LOCK = ROOT / 'eng/release/market-sidecar-source.lock.json'


def source_lock(path=SOURCE_LOCK):
    lock = json.loads(path.read_text(encoding='utf-8'))
    if lock['repository'] != REPOSITORY:
        raise ValueError('Invalid Market source repository')
    for key in ('commit', 'hostReference'):
        if not re.fullmatch(r'[0-9a-f]{40}', lock[key]):
            raise ValueError('Market requires a full reviewed commit')
    if not re.fullmatch(r'[0-9]+\.[0-9]+\.[0-9]+', lock['runtimeVersion']):
        raise ValueError('Invalid independent Market runtime version')
    release_channel(lock['minimumHostVersion'])
    return lock


def run_identity():
    run = os.environ.get('GITHUB_RUN_ID', '')
    host = os.environ.get('GITHUB_SHA', '')
    if not re.fullmatch(r'[1-9][0-9]*', run) or not re.fullmatch(r'[0-9a-f]{40}', host):
        raise ValueError('Market provenance requires a launcher CI run and commit')
    return {'buildRun': int(run), 'launcherCommit': host}


def image_identity(path):
    if not path.is_file() or path.is_symlink() or not 64 <= path.stat().st_size <= 512 * 1024**2:
        raise ValueError('Invalid Market image')
    with path.open('rb') as stream:
        return {'size': path.stat().st_size, 'sha256': hashlib.file_digest(stream, 'sha256').hexdigest()}


def provenance_path(directory, rid):
    return directory / f'market-build-{rid}.json'


def build(checkout, directory, provenance, rid):
    lock = source_lock()
    run = run_identity()
    project = Path(PACKAGE) / (PACKAGE + '.csproj')
    sha = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=checkout, text=True).strip()
    dirty = subprocess.check_output(['git', 'status', '--porcelain', '--untracked-files=no'], cwd=checkout, text=True)
    reference = json.loads((checkout / 'eng/references/nexacl.lock.json').read_text(encoding='utf-8'))
    if sha != lock['commit'] or dirty or reference['commit'] != lock['hostReference']:
        raise ValueError('Market source or Host reference differs from the reviewed lock')
    version = subprocess.check_output(['dotnet', 'msbuild', str(project), '-nologo',
                                       '-getProperty:SidecarRuntimeVersion'], cwd=checkout, text=True).strip()
    if version != lock['runtimeVersion']:
        raise ValueError('Market runtime version differs from the reviewed lock')
    subprocess.run(['pwsh', '-NoProfile', '-File', 'eng/verify.ps1'], cwd=checkout, check=True)
    publish = checkout / 'market-ci-publish'
    subprocess.run(['dotnet', 'publish', str(project), '-c', 'Release', '-r', rid, '--self-contained', 'true',
                    '-p:PublishAot=true', '-p:StripSymbols=true', '-m:1', '-nr:false', '-o', str(publish)],
                   cwd=checkout, check=True)
    binary = publish / (PACKAGE + ('.exe' if rid.startswith('win-') else ''))
    if rid.startswith('osx-'):
        subprocess.run(['codesign', '--force', '--sign', '-', str(binary)], check=True)
        subprocess.run(['codesign', '--verify', '--strict', str(binary)], check=True)
    subprocess.run([str(binary)], cwd=checkout, check=True)
    directory.mkdir(parents=True, exist_ok=True)
    image = directory / f'{PACKAGE}-{rid}.nsc'
    shutil.copyfile(binary, image)
    provenance.mkdir(parents=True, exist_ok=True)
    provenance_path(provenance, rid).write_text(json.dumps({**lock, **run, 'rid': rid,
                                                           'artifact': image_identity(image)}, indent=2) + '\n', encoding='utf-8')


def built_lock(provenance, rids, lock=None, run=None):
    lock = source_lock() if lock is None else lock
    run = run_identity() if run is None else run
    artifacts = {}
    for rid in rids:
        path = provenance_path(provenance, rid)
        if path.is_symlink() or not path.is_file() or not 1 <= path.stat().st_size <= 4096:
            raise ValueError(f'Missing or invalid Market build provenance for {rid}')
        evidence = json.loads(path.read_text(encoding='utf-8'))
        artifact = evidence.pop('artifact')
        if evidence != {**lock, **run, 'rid': rid}:
            raise ValueError('Market build provenance does not match this launcher run and source lock')
        if set(artifact) != {'size', 'sha256'} or type(artifact['size']) is not int or not 64 <= artifact['size'] <= 512 * 1024**2 or not re.fullmatch(r'[0-9a-f]{64}', artifact['sha256']):
            raise ValueError('Invalid Market build artifact identity')
        artifacts[rid] = artifact
    return {**lock, **run, 'artifacts': artifacts}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    commands.add_parser('source')
    compile_market = commands.add_parser('build')
    compile_market.add_argument('--checkout', required=True, type=Path)
    compile_market.add_argument('--output', required=True, type=Path)
    compile_market.add_argument('--provenance', required=True, type=Path)
    compile_market.add_argument('--rid', required=True, choices=RIDS)
    args = parser.parse_args()
    if args.command == 'source':
        lock = source_lock()
        print(f"repository={lock['repository']}\ncommit={lock['commit']}")
    else:
        build(args.checkout.resolve(), args.output.resolve(), args.provenance.resolve(), args.rid)


if __name__ == '__main__':
    main()
