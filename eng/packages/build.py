#!/usr/bin/env python3
"""Build a deterministic, local-only NuGet feed from this checkout. Never publishes."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[2]
SPEC = json.loads((ROOT / 'eng/packages/manifest.json').read_text())
NS = 'http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd'


def run(*args):
    subprocess.run(args, cwd=ROOT, check=True)


def package_metadata(name, dependencies, commit):
    package = ET.Element('package', xmlns=NS)
    metadata = ET.SubElement(package, 'metadata')
    for key, value in {'id': name, 'version': SPEC['version'], 'authors': 'Nexa-MC',
                       'description': 'Versioned Nexa XSR shared foundation.',
                       'readme': 'README.md', 'requireLicenseAcceptance': 'false'}.items():
        ET.SubElement(metadata, key).text = value
    ET.SubElement(metadata, 'license', type='file').text = 'LICENSE'
    ET.SubElement(metadata, 'repository', type='git', url='https://github.com/Nexa-MC/NexaCL', commit=commit)
    group = ET.SubElement(ET.SubElement(metadata, 'dependencies'), 'group', targetFramework='net10.0')
    for dep in dependencies:
        ET.SubElement(group, 'dependency', id=dep, version=f"[{SPEC['version']}]")
    if name == 'Nexa.Xsr.BuildTools':
        ET.SubElement(metadata, 'developmentDependency').text = 'true'
    return ET.tostring(package, encoding='utf-8', xml_declaration=True)


def write_package(output, name, entries):
    # Fixed ordering, ZIP metadata and stored bytes give identical package hashes
    # across paths/platforms when compiler outputs are identical. No signatures are stripped.
    path = output / f"{name}.{SPEC['version']}.nupkg"
    types = ET.Element('Types', xmlns='http://schemas.openxmlformats.org/package/2006/content-types')
    for extension in sorted(({Path(p).suffix.lstrip('.') for p in entries} - {''}) | {'rels'}):
        ET.SubElement(types, 'Default', Extension=extension,
                      ContentType='application/vnd.openxmlformats-package.relationships+xml' if extension == 'rels' else 'application/octet-stream')
    ET.SubElement(types, 'Override', PartName='/LICENSE', ContentType='text/plain')
    entries['[Content_Types].xml'] = ET.tostring(types, encoding='utf-8', xml_declaration=True)
    rels = ET.Element('Relationships', xmlns='http://schemas.openxmlformats.org/package/2006/relationships')
    ET.SubElement(rels, 'Relationship', Id='manifest', Type='http://schemas.microsoft.com/packaging/2010/07/manifest', Target=f'/{name}.nuspec')
    entries['_rels/.rels'] = ET.tostring(rels, encoding='utf-8', xml_declaration=True)
    with zipfile.ZipFile(path, 'w', compression=zipfile.ZIP_STORED) as archive:
        for member, data in sorted(entries.items()):
            info = zipfile.ZipInfo(member, (2000, 1, 1, 0, 0, 0))
            info.create_system = 3
            info.external_attr = 0o100644 << 16
            archive.writestr(info, data)
    return {'file': path.name, 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()}


def build(output):
    sdk = subprocess.check_output(['dotnet', '--version'], cwd=ROOT, text=True).strip()
    if sdk != SPEC['sdk']:
        raise SystemExit(f"Shared package build requires SDK {SPEC['sdk']}; got {sdk}")
    commit = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    run('dotnet', 'build', 'Nexa.Xsr.Runtime/Nexa.Xsr.Runtime.csproj', '-c', 'Release', '-t:Rebuild', '-m:2',
        '-p:DebugType=None', '-p:DebugSymbols=false', '-p:EnableSourceLink=false',
        '-p:PathMap=' + str(ROOT) + '=/_/nexacl', '-p:RestorePackagesWithLockFile=true', '-p:RestoreLockedMode=true')
    output.mkdir(parents=True, exist_ok=True)
    result = {'source_commit': commit, 'version': SPEC['version'], 'sdk': sdk, 'packages': []}
    for name, dependencies in [*SPEC['projects'].items(), ('Nexa.Xsr.BuildTools', [])]:
        entries = {f'{name}.nuspec': package_metadata(name, dependencies, commit),
                   'LICENSE': (ROOT / 'LICENSE').read_bytes(),
                   'README.md': (ROOT / 'docs/xsr/shared-packages.md').read_bytes()}
        if name == 'Nexa.Xsr.BuildTools':
            tool = ROOT / 'tools/Nexa.Xsr.Patch.Compiler/bin/Release/net10.0'
            for file in sorted(tool.rglob('*')):
                if file.is_file() and file.suffix in ('.dll', '.json'):
                    entries['tools/net10.0/' + file.relative_to(tool).as_posix()] = file.read_bytes()
            entries['buildTransitive/Nexa.Xsr.BuildTools.targets'] = (ROOT / 'eng/packages/Nexa.Xsr.BuildTools.targets').read_bytes()
            entries['tools/targets/Xsr.AutomaticFunctionPatches.targets'] = (ROOT / 'eng/xsr/Xsr.AutomaticFunctionPatches.targets').read_bytes()
            for file in (ROOT / 'eng/packages/licenses').glob('*.txt'):
                entries['licenses/' + file.name] = file.read_bytes()
            assets = json.loads((ROOT / 'tools/Nexa.Xsr.Patch.Compiler/obj/project.assets.json').read_text())
            folders = [Path(x) for x in assets['packageFolders']]
            for pkg in ('microsoft.codeanalysis.common/5.0.0', 'microsoft.codeanalysis.csharp/5.0.0'):
                notice = next((f / pkg / 'ThirdPartyNotices.rtf' for f in folders if (f / pkg / 'ThirdPartyNotices.rtf').exists()), None)
                if notice is None: raise RuntimeError(f'Missing third-party notice: {pkg}')
                entries['licenses/' + pkg.replace('/', '-') + '-ThirdPartyNotices.rtf'] = notice.read_bytes()
        else:
            built = ROOT / name / 'bin/Release/net10.0'
            entries[f'lib/net10.0/{name}.dll'] = (built / f'{name}.dll').read_bytes()
            entries[f'catalogs/{name}.nfp'] = (built / f'{name}.nfp').read_bytes()
            target = f'''<Project><ItemGroup>
<None Include="$(MSBuildThisFileDirectory)../catalogs/{name}.nfp" Link="{name}.nfp"
      CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" Visible="false" />
</ItemGroup></Project>\n'''
            entries[f'buildTransitive/{name}.targets'] = target.encode()
        result['packages'].append(write_package(output, name, entries))
    (output / 'feed-manifest.json').write_text(json.dumps(result, indent=2) + '\n')
    print(f"Built {len(result['packages'])} local packages in {output}")

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    build(args.output.resolve())
