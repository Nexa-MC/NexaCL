#!/usr/bin/env python3
"""Regenerate the native UI wire codecs from a built renderer; --check rejects schema drift."""
import argparse
from pathlib import Path
import subprocess
import tempfile
from xml.sax.saxutils import escape


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--assembly", type=Path)
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--window", action="store_true", help="Generate native window value contracts without input dispatch.")
    options = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    default = ("tests/Nexa.Xsr.Renderer.Backend.Avalonia.Tests/bin/Release/net10.0/Nexa.Xsr.Renderer.Backend.Avalonia.dll"
               if options.window else "Nexa.Xsr.Renderer/bin/Release/net10.0/Nexa.Xsr.Renderer.dll")
    assembly = (options.assembly or root / default).resolve()
    if not assembly.is_file():
        parser.error("Build the renderer (or backend tests for --window) in Release before generating the wire codecs.")
    target = root / ("native/WindowProtocol" if options.window else "Nexa.Xsr.Renderer/Wire")
    target.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="nexa-ui-wire-") as temporary:
        directory = Path(temporary)
        project = directory / "generator.csproj"
        project.write_text(
            '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework>'
            '<OutputType>Exe</OutputType><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>'
            '</PropertyGroup><ItemGroup><Compile Include="' + escape(str(root / "eng/xsr/generate_ui_wire.cs"), {'"': '&quot;'}) +
            '" /></ItemGroup></Project>', encoding="utf-8")
        output = directory if options.check else target
        subprocess.run(["dotnet", "run", "--project", str(project), "--", str(assembly),
                        str(output / "UiWire.Generated.cs"), *(["window"] if options.window else [])], check=True, cwd=root)
        if options.check:
            for name in (("UiWire.Generated.cs",) if options.window else ("UiWire.Generated.cs", "XsrUiInput.Generated.cs")):
                if (output / name).read_bytes() != (target / name).read_bytes():
                    raise SystemExit(f"UI wire schema is stale: {name}. Regenerate and review the ABI change.")


if __name__ == "__main__":
    main()
