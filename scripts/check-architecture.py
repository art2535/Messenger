#!/usr/bin/env python3
"""Validate Messenger project-reference direction for the refactoring baseline."""

from pathlib import Path
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
PROJECTS = {
    "Core": ROOT / "Messenger.Core" / "Messenger.Core.csproj",
    "Application": ROOT / "Messenger.Application" / "Messenger.Application.csproj",
    "Infrastructure": ROOT / "Messenger.Infrastructure" / "Messenger.Infrastructure.csproj",
    "Realtime": ROOT / "Messenger.Realtime" / "Messenger.Realtime.csproj",
    "API": ROOT / "Messenger.API" / "Messenger.API.csproj",
    "Web": ROOT / "Messenger.Web" / "Messenger.Web.csproj",
    "AppHost": ROOT / "Messenger.AppHost" / "Messenger.AppHost.csproj",
}

EXPECTED = {
    "Core": set(),
    "Application": {"Core"},
    "Infrastructure": {"Core", "Application"},
    "Realtime": {"Application"},
    "API": {"Application", "Infrastructure", "Realtime"},
    "Web": {"Core", "Application"},
    "AppHost": set(),
}


def project_references(project_file: Path) -> set[str]:
    root = ET.parse(project_file).getroot()
    references = set()
    for node in root.findall(".//ProjectReference"):
        include = node.attrib.get("Include")
        if not include:
            continue
        target = (project_file.parent / include.replace("\\", "/")).resolve()
        for name, path in PROJECTS.items():
            if target == path.resolve():
                references.add(name)
                break
        else:
            raise ValueError(f"{project_file.relative_to(ROOT)} references unknown project: {include}")
    return references


def main() -> int:
    errors = []
    for name, path in PROJECTS.items():
        if not path.is_file():
            errors.append(f"{name}: project file not found: {path.relative_to(ROOT)}")
            continue
        actual = project_references(path)
        expected = EXPECTED[name]
        if actual != expected:
            errors.append(
                f"{name}: expected references {sorted(expected)}, got {sorted(actual)}"
            )
        else:
            print(f"OK {name}: {', '.join(sorted(actual)) or 'no project references'}")

    if errors:
        print("\nArchitecture validation failed:", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1

    print("\nArchitecture validation passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
