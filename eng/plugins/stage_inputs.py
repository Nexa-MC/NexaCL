"""Stage exact SDK source and packages for public repository-scoped builds."""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tarfile
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET
import zipfile


def main():
    commit = os.environ['SDK_COMMIT']
    version = os.environ['SDK_VERSION']
    expected = os.environ['SDK_SOURCE_SHA256']
    url = os.environ['SDK_SOURCE_URL']
    print('::add-mask::' + url, flush=True)
    parsed = urllib.parse.urlsplit(url)
    if (not re.fullmatch('[0-9a-f]{40}', commit)
            or not re.fullmatch(r'0\.2\.0-alpha\.[1-9][0-9]*', version)
            or not re.fullmatch('[0-9a-f]{64}', expected)
            or parsed.scheme != 'https' or parsed.hostname != 'codeload.github.com'
            or parsed.username or parsed.password or parsed.port or parsed.fragment
            or parsed.path != '/Nexa-MC/PluginSidecar/legacy.tar.gz/' + commit):
        raise ValueError('Unexpected SDK source identity')
    output = Path('plugin-inputs')
    output.mkdir(exist_ok=False)
    archive = output / f'PluginSidecar-source-{commit[:12]}.tar.gz'
    with urllib.request.urlopen(url, timeout=60) as response:
        payload = response.read(32 * 1024 * 1024 + 1)
    if len(payload) > 32 * 1024 * 1024 or hashlib.sha256(payload).hexdigest() != expected:
        raise ValueError('SDK source digest mismatch')
    archive.write_bytes(payload)
    source = Path('plugin-source')
    source.mkdir(exist_ok=False)
    with tarfile.open(archive) as tar:
        members = tar.getmembers()
        if (len(members) > 10000 or sum(m.size for m in members) > 128 * 1024 * 1024
                or any(not m.isfile() and not m.isdir() for m in members)):
            raise ValueError('Unexpected SDK archive entries')
        tar.extractall(source, filter='data')
    roots = list(source.iterdir())
    if len(roots) != 1 or not roots[0].is_dir():
        raise ValueError('Unexpected SDK archive root')
    root = roots[0].resolve()
    names = ('Nexa.Plugin.Bootstrap.V1', 'Nexa.Plugin.Sdk.Abi100', 'Nexa.Plugin.Hosting',
             'Nexa.Plugin.MarketDelivery', 'PluginSidecar.WasmRuntime', 'PluginSidecar.Packages')
    packages = Path('plugin-packages').resolve()
    for name in names:
        project = root / 'src' / name / (name + '.csproj')
        actual = ET.parse(project).findtext('.//Version')
        if actual != ('0.1.0-alpha.1' if name == 'Nexa.Plugin.MarketDelivery' else version):
            raise ValueError('SDK package version mismatch')
        subprocess.run(['dotnet', 'pack', str(project), '-c', 'Release', '-m:1',
                        '-o', str(packages), '-p:RepositoryCommit=' + commit,
                        '-p:RestoreSources=https://api.nuget.org/v3/index.json'], cwd=root, check=True)
    contents = {}
    for name in names:
        suffix = '0.1.0-alpha.1' if name == 'Nexa.Plugin.MarketDelivery' else version
        path = packages / f'{name}.{suffix}.nupkg'
        contents[path.name] = path.read_bytes()
    manifest = {'commit': commit, 'packages': [
        {'file': name, 'sha256': hashlib.sha256(data).hexdigest()}
        for name, data in sorted(contents.items())]}
    contents['feed-manifest.json'] = (json.dumps(manifest, indent=2) + '\n').encode()
    bundle = output / f'Nexa.Plugin.HostSdk.{commit[:12]}.zip'
    with zipfile.ZipFile(bundle, 'w', zipfile.ZIP_DEFLATED) as zip_file:
        for name, data in sorted(contents.items()):
            zip_file.writestr(name, data)
    lock = {'repository': 'https://github.com/Nexa-MC/PluginSidecar.git',
            'commit': commit, 'version': version, 'distribution': {
                'repository': 'Nexa-MC/NexaCL', 'release': 'plugin-sdk-inputs-' + commit[:12],
                'file': bundle.name, 'size': bundle.stat().st_size,
                'sha256': hashlib.sha256(bundle.read_bytes()).hexdigest(),
                'sourceFile': archive.name, 'sourceSha256': expected}}
    (output / 'sdk.lock.json').write_text(json.dumps(lock, indent=2) + '\n')
    (output / 'notes.md').write_text(
        f'Pinned Plugin SDK {version} build inputs. Apache-2.0. Exact source commit `{commit}`. '
        'Includes six Host packages and the verified source snapshot. '
        'The attached sdk.lock.json records the source and package integrity digests.\n')


if __name__ == '__main__':
    main()
