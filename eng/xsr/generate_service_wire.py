"""Generate bounded native service DTO codecs from explicitly selected contract roots."""
import argparse
from pathlib import Path
import subprocess
import tempfile
from xml.sax.saxutils import escape


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--assembly", required=True, type=Path)
    parser.add_argument("--roots", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--namespace", default="Nexa.Native.Services.Wire")
    parser.add_argument("--sidecar-contracts", type=Path, help="Emit independent DTOs and matching client codecs")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    with tempfile.TemporaryDirectory(prefix="nexa-service-wire-") as temporary:
        directory = Path(temporary)
        project = directory / "generator.csproj"
        project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework>'
                           '<OutputType>Exe</OutputType><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>'
                           '</PropertyGroup><ItemGroup><Compile Include="' + escape(str(root / "eng/xsr/generate_service_wire.cs"), {'"': '&quot;'}) +
                           '" /></ItemGroup></Project>', encoding="utf-8")
        destination = directory / "generated.cs" if args.check else args.output.resolve()
        client_destination = (directory / "client" if args.check else args.sidecar_contracts.resolve()) if args.sidecar_contracts else None
        destination.parent.mkdir(parents=True, exist_ok=True)
        subprocess.run(["dotnet", "run", "--project", str(project), "--", str(args.assembly.resolve()),
                        str(args.roots.resolve()), str(destination), args.namespace,
                        *([str(client_destination)] if client_destination else [])], check=True, cwd=root)
        if args.check and destination.read_text(encoding="utf-8") != args.output.read_text(encoding="utf-8"):
            raise SystemExit("Native service DTO schema is stale; regenerate and review the ABI change")
        if args.check and client_destination:
            for name in ("BusinessData.Generated.cs", "BusinessWire.Generated.cs"):
                expected = args.sidecar_contracts / name
                if not expected.exists() or (client_destination / name).read_bytes() != expected.read_bytes():
                    raise SystemExit("Sidecar client DTO schema is stale: " + str(expected))


if __name__ == "__main__":
    main()
