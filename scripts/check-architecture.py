#!/usr/bin/env python3
"""Validate project references and framework boundaries for the refactoring baseline."""

from pathlib import Path
import re
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
    "Web": {"Application", "Realtime"},
    "AppHost": set(),
}

# Core and Application must not compile against persistence or web frameworks.
FORBIDDEN_SOURCE_REFERENCES = re.compile(
    r"^\s*using\s+(?:Microsoft\.EntityFrameworkCore|Microsoft\.AspNetCore|"
    r"Messenger\.Infrastructure)(?:\.|\s*;)|"
    r"\b(?:Microsoft\.EntityFrameworkCore|Microsoft\.AspNetCore|"
    r"Messenger\.Infrastructure)\.",
)
FORBIDDEN_PACKAGES = (
    "Microsoft.EntityFrameworkCore",
    "Microsoft.AspNetCore",
)


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


def framework_boundary_violations(project_name: str, project_file: Path) -> list[str]:
    if project_name not in {"Core", "Application"}:
        return []

    errors = []
    project_root = ET.parse(project_file).getroot()
    for node in project_root.findall(".//PackageReference"):
        package = node.attrib.get("Include", "")
        if any(package == prefix or package.startswith(prefix + ".") for prefix in FORBIDDEN_PACKAGES):
            errors.append(
                f"{project_file.relative_to(ROOT)}: forbidden package reference {package}"
            )

    source_root = project_file.parent
    for source_file in sorted(source_root.rglob("*.cs")):
        try:
            lines = source_file.read_text(encoding="utf-8-sig").splitlines()
        except UnicodeDecodeError as error:
            errors.append(f"{source_file.relative_to(ROOT)}: cannot read source as UTF-8: {error}")
            continue

        for line_number, line in enumerate(lines, start=1):
            if FORBIDDEN_SOURCE_REFERENCES.search(line):
                errors.append(
                    f"{source_file.relative_to(ROOT)}:{line_number}: "
                    f"forbidden framework/infrastructure reference: {line.strip()}"
                )
    return errors


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

        boundary_errors = framework_boundary_violations(name, path)
        errors.extend(boundary_errors)
        if name in {"Core", "Application"} and not boundary_errors:
            print(f"OK {name}: no EF Core, ASP.NET Core or Infrastructure source/package references")

    if errors:
        print("\nArchitecture validation failed:", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1

    print("\nArchitecture validation passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
