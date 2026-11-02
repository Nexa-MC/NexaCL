"""Build native Explorer commands and their sparse identity package using the Windows SDK."""
import argparse
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
CLSID = "522C4ED3-0A1F-47E8-B018-961652D63816"
PACKAGE = "NexaCL.ExplorerIntegration"

def sdk_tool(name):
    direct = shutil.which(name)
    if direct:
        return Path(direct)
    base = Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")) / "Windows Kits/10/bin"
    versions = sorted((p for p in base.glob("10.*") if (p / "x64" / name).is_file()),
                      key=lambda p: tuple(int(part) for part in p.name.split(".")), reverse=True)
    if not versions:
        raise RuntimeError(f"Windows SDK tool missing: {name}")
    return versions[0] / "x64" / name

def manifest(arch, publisher, version="1.0.0.0"):
    if arch not in ("x64", "arm64") or not re.fullmatch(r"\d+\.\d+\.\d+\.\d+", version):
        raise ValueError("Invalid package architecture or version")
    # Escape the certificate's distinguished name as XML, never shell text.
    identity = ET.Element("Identity", Name=PACKAGE, Publisher=publisher, Version=version, ProcessorArchitecture=arch)
    return f'''<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
 xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
 xmlns:uap10="http://schemas.microsoft.com/appx/manifest/uap/windows10/10"
 xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
 xmlns:com="http://schemas.microsoft.com/appx/manifest/com/windows10"
 xmlns:desktop4="http://schemas.microsoft.com/appx/manifest/desktop/windows10/4"
 xmlns:desktop5="http://schemas.microsoft.com/appx/manifest/desktop/windows10/5"
 IgnorableNamespaces="uap uap10 rescap com desktop4 desktop5">
 {ET.tostring(identity, encoding="unicode")}
 <Properties><DisplayName>NexaCL</DisplayName><PublisherDisplayName>Nexa</PublisherDisplayName><Logo>Assets\\Logo.png</Logo><uap10:AllowExternalContent>true</uap10:AllowExternalContent></Properties>
 <Resources><Resource Language="en-us"/><Resource Language="zh-cn"/><Resource Language="zh-tw"/></Resources>
 <Dependencies><TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.19041.0" MaxVersionTested="10.0.26100.0"/></Dependencies>
 <Applications><Application Id="NexaCL" Executable="Nexa.Desktop.exe" uap10:RuntimeBehavior="win32App" uap10:TrustLevel="mediumIL">
  <uap:VisualElements DisplayName="NexaCL" Description="NexaCL archive installation" Square150x150Logo="Assets\\Logo.png" Square44x44Logo="Assets\\Logo.png" BackgroundColor="transparent" AppListEntry="none"/>
  <Extensions>
   <com:Extension Category="windows.comServer"><com:ComServer><com:SurrogateServer DisplayName="NexaCL">
    <com:Class Id="{CLSID}" Path="ShellIntegration\\Nexa.ExplorerCommand.dll" ThreadingModel="STA"/>
   </com:SurrogateServer></com:ComServer></com:Extension>
   <desktop4:Extension Category="windows.fileExplorerContextMenus"><desktop4:FileExplorerContextMenus>
    <desktop5:ItemType Type=".zip"><desktop5:Verb Id="NexaCLInstallArchive" Clsid="{CLSID}"/></desktop5:ItemType>
   </desktop4:FileExplorerContextMenus></desktop4:Extension>
  </Extensions>
 </Application></Applications>
 <Capabilities><rescap:Capability Name="runFullTrust"/><rescap:Capability Name="unvirtualizedResources"/></Capabilities>
</Package>'''

def build(payload, arch, version="1.0.0.0"):
    payload = Path(payload).resolve()
    output = payload / "ShellIntegration"
    output.mkdir(parents=True, exist_ok=True)
    thumb = os.environ.get("NEXA_WINDOWS_SIGNING_THUMBPRINT", "").strip()
    publisher = "CN=Nexa"
    if thumb:
        if not re.fullmatch(r"[a-fA-F0-9]{40}", thumb):
            raise ValueError("Invalid Windows signing certificate thumbprint")
        publisher = subprocess.check_output(["powershell.exe", "-NoProfile", "-NonInteractive", "-Command",
            f"[Console]::OutputEncoding=[Text.Encoding]::UTF8; (Get-Item -LiteralPath 'Cert:\\CurrentUser\\My\\{thumb}' -ErrorAction Stop).Subject"], encoding="utf-8-sig").strip()
    with tempfile.TemporaryDirectory(prefix="nexacl-shell-build-") as temporary:
        subprocess.run(["cmake", "-S", str(ROOT / "src/platform/Nexa.ExplorerCommand"), "-B", temporary, "-A", "ARM64" if arch == "arm64" else "x64"], check=True)
        subprocess.run(["cmake", "--build", temporary, "--config", "Release"], check=True)
        if arch == "x64":
            subprocess.run(["ctest", "--test-dir", temporary, "-C", "Release", "--output-on-failure"], check=True)
        for name in ("Nexa.ExplorerCommand.dll", "Nexa.ShellRegistration.exe"):
            shutil.copy2(Path(temporary) / "Release" / name, output / name)
    package = output / "package"
    (package / "Assets").mkdir(parents=True, exist_ok=True)
    shutil.copy2(ROOT / "src/app/Nexa.Desktop/Assets/icon.png", package / "Assets/Logo.png")
    (package / "AppxManifest.xml").write_text(manifest(arch, publisher, version), encoding="utf-8")
    msix = output / f"{PACKAGE}.msix"
    # Sparse packages deliberately refer to executable/COM files in ExternalLocation.
    subprocess.run([str(sdk_tool("makeappx.exe")), "pack", "/d", str(package), "/p", str(msix), "/nv", "/o"], check=True)
    if thumb:
        signer = str(sdk_tool("signtool.exe"))
        for artifact in (output / "Nexa.ExplorerCommand.dll", output / "Nexa.ShellRegistration.exe", msix):
            subprocess.run([signer, "sign", "/sha1", thumb, "/fd", "SHA256", "/tr", "http://timestamp.digicert.com", "/td", "SHA256", str(artifact)], check=True)
            subprocess.run([signer, "verify", "/pa", str(artifact)], check=True)
    else:
        print("Explorer identity package is unsigned: production registration requires a trusted signing certificate. Development registration must be explicit.", flush=True)
    return output

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--payload", type=Path, required=True)
    parser.add_argument("--arch", choices=("x64", "arm64"), required=True)
    parser.add_argument("--version", default="1.0.0.0")
    args = parser.parse_args()
    build(args.payload, args.arch, args.version)
