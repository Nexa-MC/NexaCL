#!/usr/bin/env python3
"""Build the local Swift App Intents/Spotlight companion; no Xcode project generation."""
import argparse
import pathlib
import plistlib
import subprocess
import json
import tempfile


def build(output, arch):
    root = pathlib.Path(__file__).resolve().parents[2]
    contents = output / 'NexaCL Actions.app' / 'Contents'
    binary = contents / 'MacOS' / 'NexaCL.Actions'
    resources = contents / 'Resources'
    binary.parent.mkdir(parents=True, exist_ok=True)
    resources.mkdir(parents=True, exist_ok=True)
    target = ('x86_64' if arch == 'x64' else 'arm64') + '-apple-macosx13.0'
    source = root / 'Nexa.Platform.Runtime/Native/macos/Actions.swift'
    sdk = subprocess.check_output(['xcrun', '--sdk', 'macosx', '--show-sdk-path'], text=True).strip()
    scratch = tempfile.TemporaryDirectory(prefix='nexa-appintents-')
    protocols = pathlib.Path(scratch.name) / 'protocols.json'
    constants = pathlib.Path(scratch.name) / 'NexaActions.swiftconstvalues'
    constants_list = pathlib.Path(scratch.name) / 'const-values-list'
    protocols.write_text(json.dumps(['AppIntent', 'AppEntity', 'AppEnum', 'AppShortcutsProvider']), encoding='utf-8')
    constants_list.write_text(str(constants) + '\n', encoding='utf-8')
    subprocess.run(['xcrun', 'swiftc', '-O', '-emit-const-values', '-emit-const-values-path', str(constants),
                    '-Xfrontend', '-const-gather-protocols-file', '-Xfrontend', str(protocols), '-parse-as-library', '-module-name', 'NexaActions',
                    '-target', target, '-sdk', sdk, '-framework', 'AppIntents', '-framework', 'CoreSpotlight',
                    '-framework', 'SwiftUI', '-framework', 'AppKit', str(source), '-o', str(binary)], check=True)
    with (contents / 'Info.plist').open('wb') as stream:
        plistlib.dump(dict(CFBundleIdentifier='org.nexacl.launcher.actions', CFBundleName='NexaCL Actions',
                           CFBundleDisplayName='NexaCL Actions', CFBundleExecutable='NexaCL.Actions',
                           CFBundlePackageType='APPL', CFBundleVersion='1', CFBundleShortVersionString='1.0',
                           LSMinimumSystemVersion='13.0', LSUIElement=True, NSPrincipalClass='NSApplication',
                           NSUserActivityTypes=['com.apple.corespotlightitem']), stream)
    xcode_version = subprocess.check_output(['xcodebuild', '-version'], text=True).splitlines()[-1].split()[-1]
    toolchain = str(pathlib.Path(subprocess.check_output(['xcrun', '-f', 'swiftc'], text=True).strip()).parent)
    subprocess.run(['xcrun', 'appintentsmetadataprocessor', '--module-name', 'NexaActions',
                    '--xcode-version', xcode_version, '--toolchain-dir', toolchain,
                    '--platform-family', 'macOS', '--deployment-target', '13.0',
                    '--bundle-identifier', 'org.nexacl.launcher.actions',
                    '--sdk-root', sdk, '--target-triple', target, '--binary-file', str(binary),
                    '--source-files', str(source), '--swift-const-vals-list', str(constants_list),
                    '--compile-time-extraction', '--output', str(resources)], check=True)
    scratch.cleanup()
    if not any(resources.rglob('*.actionsdata')):
        raise RuntimeError('App Intents metadata was not generated')
    subprocess.run(['codesign', '--force', '--deep', '--sign', '-', str(contents.parent)], check=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=pathlib.Path, required=True)
    parser.add_argument('--arch', choices=['x64', 'arm64'], required=True)
    args = parser.parse_args()
    build(args.output, args.arch)
