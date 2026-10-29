#!/usr/bin/env python3
"""Verify package contents and a clean, package-only consumer (optional Linux AOT)."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile
ROOT = Path(__file__).resolve().parents[2]

def verify(feed, aot):
    manifest = json.loads((feed / 'feed-manifest.json').read_text())
    spec = json.loads((ROOT / 'eng/packages/manifest.json').read_text())
    names = set(spec['projects']) | {'Nexa.Xsr.BuildTools'}
    actual = set()
    for item in manifest['packages']:
        pkg = feed / item['file']
        if hashlib.sha256(pkg.read_bytes()).hexdigest() != item['sha256']: raise AssertionError('hash mismatch')
        with zipfile.ZipFile(pkg) as archive:
            paths = archive.namelist()
            metadata = ET.fromstring(archive.read(next(p for p in paths if p.endswith('.nuspec'))))
            name = next(e.text for e in metadata.iter() if e.tag.endswith('}id'))
            actual.add(name)
            assert 'LICENSE' in paths and 'README.md' in paths
            if name != 'Nexa.Xsr.BuildTools':
                assert f'catalogs/{name}.nfp' in paths
                assert f'buildTransitive/{name}.targets' in paths
                assert f'lib/net10.0/{name}.dll' in paths
                assert not any('CodeAnalysis' in p or 'Cecil' in p for p in paths)
            else:
                assert not any(p.startswith('lib/') for p in paths)
                assert 'tools/net10.0/Nexa.Xsr.Patch.Compiler.dll' in paths
                assert 'licenses/Mono.Cecil.txt' in paths and 'licenses/Roslyn.txt' in paths
    assert actual == names
    with tempfile.TemporaryDirectory(prefix='nexa-package-consumer-') as directory:
        root = Path(directory)
        for template in (ROOT / 'eng/packages/consumer').glob('*.in'):
            (root / template.name.removesuffix('.in')).write_text(template.read_text().replace('@VERSION@', manifest['version']))
        config = ET.Element('configuration'); sources = ET.SubElement(config, 'packageSources'); ET.SubElement(sources,'clear')
        ET.SubElement(sources,'add',key='feed',value=str(feed))
        ET.SubElement(sources,'add',key='nuget.org',value='https://api.nuget.org/v3/index.json')
        (root / 'NuGet.Config').write_bytes(ET.tostring(config))
        # No existing Nexa package in the machine cache may hide a packaging failure.
        environment = dict(os.environ, NUGET_PACKAGES=str(root / 'packages'))
        def run(*command): subprocess.run(command,cwd=root,env=environment,check=True)
        run('dotnet','run','--project','Consumer.csproj','-c','Release')
        check_output(root / 'bin/Release/net10.0', names)
        if aot:
            run('dotnet','publish','Consumer.csproj','-c','Release','-r','linux-x64','-p:PublishAot=true','-o',str(root / 'native'))
            check_output(root / 'native', names)
            run(str(root / 'native/Consumer'))
        # A second build must keep catalogs and must not double-instrument.
        run('dotnet','run','--project','Consumer.csproj','-c','Release')
    print('PASS: package closure, fresh restore, incremental build and runtime output isolation')

def check_output(directory, names):
    for name in names - {'Nexa.Xsr.BuildTools'}:
        assert (directory / f'{name}.nfp').exists(), name
    assert (directory / 'Consumer.nfp').exists()
    assert not any(p.name.startswith(('Mono.Cecil','Microsoft.CodeAnalysis','Nexa.Xsr.Patch.Compiler')) for p in directory.rglob('*'))

if __name__ == '__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--feed',required=True,type=Path);parser.add_argument('--aot',action='store_true')
    args=parser.parse_args(); verify(args.feed.resolve(),args.aot)
