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

    def test_no_installer_enables_developer_mode_or_installs_trust(self):
        for relative in ('eng/release/windows.iss', 'eng/release/windows.wxs', 'src/platform/Nexa.ExplorerCommand/Registration.cpp'):
            source = (ROOT / relative).read_text(encoding='utf-8')
            for forbidden in ('AllowDevelopmentWithoutDevLicense', 'Import-Certificate', 'TrustedPeople', 'Root\\Certificates'):
                self.assertNotIn(forbidden, source)

if __name__ == '__main__':
    unittest.main()
