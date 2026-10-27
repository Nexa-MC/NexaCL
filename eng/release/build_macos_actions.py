#!/usr/bin/env python3
"""Build the local Swift App Intents/Spotlight companion; no Xcode project generation."""
import argparse
import pathlib
import plistlib
import subprocess


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
    subprocess.run(['xcrun', 'swiftc', '-O', '-parse-as-library', '-module-name', 'NexaActions',
                    '-target', target, '-sdk', sdk, '-framework', 'AppIntents', '-framework', 'CoreSpotlight',
                    '-framework', 'SwiftUI', '-framework', 'AppKit', str(source), '-o', str(binary)], check=True)
    with (contents / 'Info.plist').open('wb') as stream:
        plistlib.dump(dict(CFBundleIdentifier='org.nexacl.launcher.actions', CFBundleName='NexaCL Actions',
                           CFBundleDisplayName='NexaCL Actions', CFBundleExecutable='NexaCL.Actions',
                           CFBundlePackageType='APPL', CFBundleVersion='1', CFBundleShortVersionString='1.0',
                           LSMinimumSystemVersion='13.0', LSUIElement=True, NSPrincipalClass='NSApplication',
                           NSUserActivityTypes=['com.apple.corespotlightitem']), stream)
    subprocess.run(['xcrun', 'appintentsmetadataprocessor', '--module-name', 'NexaActions',
                    '--sdk-root', sdk, '--target-triple', target, '--binary-file', str(binary),
                    '--source-files', str(source), '--output', str(resources)], check=True)
    if not any(resources.rglob('*.actionsdata')):
        raise RuntimeError('App Intents metadata was not generated')
    subprocess.run(['codesign', '--force', '--deep', '--sign', '-', str(contents.parent)], check=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=pathlib.Path, required=True)
    parser.add_argument('--arch', choices=['x64', 'arm64'], required=True)
    args = parser.parse_args()
    build(args.output, args.arch)
