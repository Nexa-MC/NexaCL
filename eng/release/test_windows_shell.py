import unittest
import xml.etree.ElementTree as ET
from pathlib import Path
from build_windows_shell import manifest, PACKAGE, CLSID

ROOT = Path(__file__).resolve().parents[2]

class WindowsShellTests(unittest.TestCase):
    def test_sparse_package_exposes_one_zip_flyout_and_matching_native_class(self):
        for arch in ("x64", "arm64"):
            tree = ET.fromstring(manifest(arch, 'CN=Nexa & Test'))
            identity = tree.find('{*}Identity')
            self.assertEqual(PACKAGE, identity.attrib['Name'])
            self.assertEqual('CN=Nexa & Test', identity.attrib['Publisher'])
            self.assertEqual(arch, identity.attrib['ProcessorArchitecture'])
            self.assertEqual('true', tree.find('.//{*}AllowExternalContent').text)
            item = tree.findall('.//{*}ItemType')
            self.assertEqual(['.zip'], [entry.attrib['Type'] for entry in item])
            self.assertEqual(CLSID, tree.find('.//{*}Class').attrib['Id'])
            self.assertEqual(CLSID, tree.find('.//{*}Verb').attrib['Clsid'])
            self.assertEqual(r'ShellIntegration\Nexa.ExplorerCommand.dll', tree.find('.//{*}Class').attrib['Path'])
            self.assertEqual('Nexa.Desktop.exe', tree.find('.//{*}Application').attrib['Executable'])

    def test_all_user_installers_register_each_user_and_remove_owned_identity(self):
        wix = ET.parse(ROOT / 'eng/release/windows.wxs')
        values = wix.findall('.//{*}RegistryValue')
        self.assertTrue(any(v.get('Name') == 'StubPath' and v.get('Value', '').endswith('--register') for v in values))
        cleanup = wix.find('.//{*}CustomAction[@Id="RemoveNativeExplorerPackage"]')
        self.assertEqual('no', cleanup.get('Impersonate'))
        self.assertIn('--unregister-all', cleanup.get('ExeCommand'))
        inno = (ROOT / 'eng/release/windows.iss').read_text(encoding='utf-8')
        self.assertIn('runasoriginaluser', inno)
        self.assertIn('--unregister-all', inno)

    def test_optional_trust_never_enables_developer_mode_or_installs_a_root(self):
        for relative in ('eng/release/windows.iss', 'eng/release/windows.wxs', 'src/platform/Nexa.ExplorerCommand/Registration.cpp'):
            source = (ROOT / relative).read_text(encoding='utf-8')
            for forbidden in ('AllowDevelopmentWithoutDevLicense', 'Import-PfxCertificate', 'Root\\Certificates'):
                self.assertNotIn(forbidden, source)
        native = (ROOT / 'src/platform/Nexa.ExplorerCommand/CertificateTrust.h').read_text()
        self.assertIn('L"TrustedPeople"', native)
        self.assertNotIn('L"Root"', native)
        self.assertIn('NEXA_SHELL_CERT_SHA256', native)
        wix = ET.parse(ROOT / 'eng/release/windows.wxs')
        self.assertEqual('200', wix.find('.//{*}Feature[@Id="ExplorerIntegration"]').get('Level'))
        self.assertEqual('rollback', wix.find('.//{*}CustomAction[@Id="RollbackShellTrust"]').get('Execute'))
        inno = (ROOT / 'eng/release/windows.iss').read_text()
        task = next(line for line in inno.splitlines() if line.startswith('Name: "explorerintegration"'))
        self.assertIn('unchecked', task)

    def test_welcome_and_finish_launch_are_interactive_and_user_context(self):
        inno = (ROOT / 'eng/release/windows.iss').read_text()
        self.assertIn('DisableWelcomePage=no', inno)
        launch = next(line for line in inno.splitlines() if line.startswith('Filename: "{app}\\Nexa.Desktop.exe"'))
        for flag in ('postinstall', 'skipifsilent', 'runasoriginaluser'):
            self.assertIn(flag, launch)
        wix = ET.parse(ROOT / 'eng/release/windows.wxs')
        self.assertEqual('WixUI_FeatureTree', wix.find('.//{*}WixUI').get('Id'))
        self.assertEqual('yes', wix.find('.//{*}CustomAction[@Id="LaunchNexaCL"]').get('Impersonate'))
        self.assertTrue(any(p.get('Value') == 'LaunchNexaCL' and 'NOT Installed' in p.get('Condition', '') for p in wix.findall('.//{*}Publish')))

if __name__ == '__main__':
    unittest.main()
