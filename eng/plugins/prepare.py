#!/usr/bin/env python3
"""Build only the pinned public PluginSidecar packages, outside the Host source graph."""
import argparse, hashlib, json, subprocess
from pathlib import Path
root = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser()
parser.add_argument('--source', type=Path)
parser.add_argument('--dotnet', default='dotnet')
args = parser.parse_args()
lock = json.loads((root/'eng/plugins/sdk.lock.json').read_text())
feed = root/'eng/plugins/feed'
manifest = feed/'feed-manifest.json'
if manifest.exists():
    data = json.loads(manifest.read_text())
    if data['commit'] == lock['commit'] and all((feed/p['file']).is_file() and hashlib.sha256((feed/p['file']).read_bytes()).hexdigest() == p['sha256'] for p in data['packages']):
        print('Pinned plugin SDK feed verified.'); raise SystemExit(0)
checkout = Path.home()/'.cache/nexa-plugin-sdk'/lock['commit']
checkout.parent.mkdir(parents=True, exist_ok=True)
if not (checkout/'.git').exists():
    subprocess.run(['git','clone','--no-checkout',str(args.source.resolve()) if args.source else lock['repository'],str(checkout)],check=True)
subprocess.run(['git','-C',str(checkout),'checkout','--detach',lock['commit']],check=True)
if subprocess.check_output(['git','-C',str(checkout),'rev-parse','HEAD'],text=True).strip() != lock['commit']:
    raise SystemExit('SDK source does not match its immutable lock.')
feed.mkdir(parents=True,exist_ok=True)
packages=[]
for name in ['Nexa.Plugin.Bootstrap.V1','Nexa.Plugin.Sdk.Abi100','Nexa.Plugin.Hosting','Nexa.Plugin.MarketDelivery','PluginSidecar.WasmRuntime','PluginSidecar.Packages']:
    subprocess.run([args.dotnet,'pack',str(checkout/'src'/name),'-c','Release','-m:1','-o',str(feed),'-p:RestoreSources=https://api.nuget.org/v3/index.json'],cwd=checkout,check=True)
    version = '0.1.0-alpha.1' if name == 'Nexa.Plugin.MarketDelivery' else lock['version']
    path = feed/f'{name}.{version}.nupkg'
    packages.append(dict(file=path.name,sha256=hashlib.sha256(path.read_bytes()).hexdigest()))
manifest.write_text(json.dumps(dict(commit=lock['commit'],packages=packages),indent=2)+'\n')
print('Pinned plugin SDK feed prepared.')
