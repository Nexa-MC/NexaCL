#!/usr/bin/env python3
"""Build the companion with Xcode's native App Intents extraction/signing pipeline."""
import argparse
import json
import pathlib
import plistlib
import shutil
import subprocess
import tempfile


def build(output, arch):
    root = pathlib.Path(__file__).resolve().parents[2]
    source = root / 'Nexa.Platform.Runtime/Native/macos/Actions.swift'
    output.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='nexa-actions-') as directory:
        scratch = pathlib.Path(directory)
        project = scratch / 'NexaActions.xcodeproj'
        project.mkdir()
        info = scratch / 'Info.plist'
        with info.open('wb') as stream:
            plistlib.dump(dict(CFBundleIdentifier='org.nexacl.launcher.actions', CFBundleName='NexaCL Actions',
                               CFBundleDisplayName='NexaCL Actions', CFBundleExecutable='NexaCL.Actions',
                               CFBundlePackageType='APPL', CFBundleVersion='1', CFBundleShortVersionString='1.0',
                               LSMinimumSystemVersion='13.0', LSUIElement=True, NSPrincipalClass='NSApplication',
                               NSUserActivityTypes=['com.apple.corespotlightitem']), stream)
        settings = '''SDKROOT = macosx; MACOSX_DEPLOYMENT_TARGET = 13.0; SWIFT_VERSION = 5.0;
            PRODUCT_NAME = "NexaCL Actions"; EXECUTABLE_NAME = "NexaCL.Actions";
            PRODUCT_BUNDLE_IDENTIFIER = org.nexacl.launcher.actions;
            CODE_SIGNING_ALLOWED = NO; SWIFT_OPTIMIZATION_LEVEL = "-O";
            SWIFT_COMPILATION_MODE = wholemodule; SWIFT_ENABLE_EMIT_CONST_VALUES = YES;
            SWIFT_EMIT_LOC_STRINGS = YES; GENERATE_INFOPLIST_FILE = NO;
            INFOPLIST_FILE = ''' + json.dumps(str(info)) + ';'
        # Intent metadata is a native Xcode task, not a hand-maintained approximation of
        # compiler supplementary-output maps and SDK-specific extractor arguments.
        document = '''// !$*UTF8*$!
{ archiveVersion = 1; classes = {}; objectVersion = 56; objects = {
000000000000000000000001 = {isa = PBXProject; buildConfigurationList = 000000000000000000000010;
 compatibilityVersion = "Xcode 14.0"; developmentRegion = en; knownRegions = (en, Base);
 mainGroup = 000000000000000000000003; productRefGroup = 000000000000000000000004;
 projectDirPath = ""; projectRoot = ""; targets = (000000000000000000000002); };
000000000000000000000002 = {isa = PBXNativeTarget; name = NexaActions; productName = "NexaCL Actions";
 productType = "com.apple.product-type.application"; productReference = 000000000000000000000006;
 buildConfigurationList = 000000000000000000000011; buildPhases = (000000000000000000000008,000000000000000000000009);
 buildRules = (); dependencies = (); };
000000000000000000000003 = {isa = PBXGroup; children = (000000000000000000000005,000000000000000000000004); sourceTree = "<group>"; };
000000000000000000000004 = {isa = PBXGroup; children = (000000000000000000000006); name = Products; sourceTree = "<group>"; };
000000000000000000000005 = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = SOURCE_PATH; sourceTree = "<absolute>"; };
000000000000000000000006 = {isa = PBXFileReference; explicitFileType = wrapper.application; path = "NexaCL Actions.app"; sourceTree = BUILT_PRODUCTS_DIR; };
000000000000000000000007 = {isa = PBXBuildFile; fileRef = 000000000000000000000005; };
000000000000000000000008 = {isa = PBXSourcesBuildPhase; buildActionMask = 2147483647; files = (000000000000000000000007); runOnlyForDeploymentPostprocessing = 0; };
000000000000000000000009 = {isa = PBXFrameworksBuildPhase; buildActionMask = 2147483647; files = (); runOnlyForDeploymentPostprocessing = 0; };
000000000000000000000010 = {isa = XCConfigurationList; buildConfigurations = (000000000000000000000012); defaultConfigurationIsVisible = 0; defaultConfigurationName = Release; };
000000000000000000000011 = {isa = XCConfigurationList; buildConfigurations = (000000000000000000000013); defaultConfigurationIsVisible = 0; defaultConfigurationName = Release; };
000000000000000000000012 = {isa = XCBuildConfiguration; name = Release; buildSettings = {}; };
000000000000000000000013 = {isa = XCBuildConfiguration; name = Release; buildSettings = { SETTINGS }; };
}; rootObject = 000000000000000000000001; }
'''.replace('SOURCE_PATH', json.dumps(str(source))).replace('SETTINGS', settings)
        (project / 'project.pbxproj').write_text(document, encoding='utf-8')
        products = scratch / 'products'
        subprocess.run(['xcodebuild', '-quiet', '-project', str(project), '-target', 'NexaActions',
                        '-configuration', 'Release', '-sdk', 'macosx',
                        'ARCHS=' + ('x86_64' if arch == 'x64' else 'arm64'), 'ONLY_ACTIVE_ARCH=NO',
                        'SYMROOT=' + str(scratch / 'build'), 'OBJROOT=' + str(scratch / 'objects'),
                        'CONFIGURATION_BUILD_DIR=' + str(products), 'CODE_SIGNING_ALLOWED=NO',
                        'COMPILER_INDEX_STORE_ENABLE=NO', 'build'], check=True)
        app = products / 'NexaCL Actions.app'
        metadata = list((app / 'Contents/Resources').rglob('*.actionsdata'))
        if not metadata:
            raise RuntimeError('App Intents metadata was not generated')
        combined = b''.join(path.read_bytes() for path in metadata)
        if not all(name in combined for name in (b'OpenLauncher', b'OpenResources', b'OpenSettings')):
            raise RuntimeError('App Intents metadata is missing required launcher actions')
        target = output / app.name
        if target.exists():
            shutil.rmtree(target)
        shutil.copytree(app, target, symlinks=True)
        subprocess.run(['codesign', '--force', '--deep', '--sign', '-', str(target)], check=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=pathlib.Path, required=True)
    parser.add_argument('--arch', choices=['x64', 'arm64'], required=True)
    args = parser.parse_args()
    build(args.output, args.arch)
