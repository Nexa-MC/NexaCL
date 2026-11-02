"""Verify repository project locations, unchanged dependency ownership and documentation links."""
from pathlib import Path
import json
import re
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]


def verify():
    projects = json.loads((ROOT / 'eng/architecture/projects.json').read_text())
    references = json.loads((ROOT / 'eng/architecture/project-references.json').read_text())
    actual = {p.stem: p for base in ('src', 'tests', 'tools') for p in (ROOT / base).rglob('*.csproj')
              if not {'bin', 'obj'}.intersection(p.parts)}
    errors = []
    if projects.keys() != actual.keys() or projects.keys() != references.keys():
        errors.append('Project location inventory, discovered projects and dependency baseline differ')
    for name, directory in projects.items():
        expected = ROOT / directory / (name + '.csproj')
        if actual.get(name) != expected:
            errors.append(f'{name}: incorrect or missing project location {directory}')
            continue
        tree = ET.parse(expected)
        for element in tree.iter('ProjectReference'):
            target = (expected.parent / element.attrib['Include']).resolve()
            if not target.is_file():
                errors.append(f'{name}: missing reference {target}')
            if target.stem != 'Nexa.Xsr.Patch.Compiler' and target.stem not in references[name]:
                errors.append(f'{name}: undeclared dependency {target.stem}')
        for element in tree.iter('Import'):
            value = element.attrib['Project']
            if '$(' not in value and not (expected.parent / value).is_file():
                errors.append(f'{name}: missing import {value}')
    listed = {x.attrib['Path'] for x in ET.parse(ROOT / 'NexaCL.slnx').iter('Project')}
    expected = {str(path.relative_to(ROOT)) for path in actual.values()}
    if listed != expected:
        errors.append('Solution project locations do not match inventory')
    for page in [ROOT / 'README.md', ROOT / 'CHANGELOG.md', ROOT / 'CONTRIBUTER.md', ROOT / 'CONTRIBUTING.md', *(ROOT / 'docs').rglob('*.md')]:
        for target in re.findall(r'(?<!!)\[[^\]]+\]\(([^)]+)\)', page.read_text()):
            target = target.split('#', 1)[0]
            if target and not re.match(r'[a-z]+:', target) and not (page.parent / target).exists():
                errors.append(f'{page.relative_to(ROOT)}: broken link {target}')
    if errors:
        raise ValueError('\n'.join(errors))
    print(f'Repository layout verified: {len(projects)} projects; dependency ownership, solution and document links valid.')


if __name__ == '__main__':
    try:
        verify()
    except (OSError, ValueError, KeyError) as error:
        print(error, file=sys.stderr)
        sys.exit(1)
