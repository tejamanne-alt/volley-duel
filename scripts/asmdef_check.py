#!/usr/bin/env python3
"""Check that every Unity assembly definition references what its code uses.

The headless build compiles Unity-facing code in one project against API stubs, so it cannot see
asmdef boundaries. Unity can: a script that uses uGUI from an asmdef without "UnityEngine.UI" in its
references does not compile, and that stops every build. This script reads each .asmdef, scans
the scripts it owns for namespaces, and checks that the providing assemblies are referenced. It also
checks that editor-only APIs stay in editor assemblies, that engine-free assemblies stay engine-free,
and that every reference names a real assembly.

  python3 scripts/asmdef_check.py      # used by scripts/check.sh
"""
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TREES = ["Assets", os.path.join("Packages", "com.duel.core")]

# Namespaces that live in package assemblies, and the assembly that provides each.
PACKAGE_NAMESPACES = [
    ("UnityEngine.UI", "UnityEngine.UI"),
    ("UnityEngine.EventSystems", "UnityEngine.UI"),
    ("TMPro", "Unity.TextMeshPro"),
    ("UnityEngine.InputSystem", "Unity.InputSystem"),
    ("UnityEngine.Rendering.Universal", "Unity.RenderPipelines.Universal.Runtime"),
    ("NUnit.Framework", "nunit.framework.dll"),
]

# Package assemblies the project may reference (the manifest's packages and the test framework).
KNOWN_PACKAGE_ASSEMBLIES = {
    "UnityEngine.UI", "Unity.TextMeshPro", "Unity.TextMeshPro.Editor", "Unity.InputSystem",
    "Unity.RenderPipelines.Core.Runtime", "Unity.RenderPipelines.Universal.Runtime",
    "UnityEngine.TestRunner", "UnityEditor.TestRunner",
}

USING = re.compile(r"^\s*using\s+(?:static\s+)?(?:\w+\s*=\s*)?([A-Za-z_][\w.]*)\s*;", re.M)
QUALIFIED = re.compile(r"\b(UnityEngine\.UI|UnityEngine\.EventSystems|UnityEngine\.InputSystem|UnityEngine\.Rendering\.Universal|TMPro|UnityEditor)\.")


def find_asmdefs():
    found = []
    for tree in TREES:
        for dirpath, dirnames, filenames in os.walk(os.path.join(ROOT, tree)):
            dirnames[:] = [d for d in dirnames if not d.startswith(".") and d not in ("bin", "obj")]
            for f in filenames:
                if f.endswith(".asmdef"):
                    path = os.path.join(dirpath, f)
                    with open(path, encoding="utf-8") as fh:
                        found.append((dirpath, json.load(fh), os.path.relpath(path, ROOT)))
    return found


def owned_scripts(folder, asmdef_folders):
    for dirpath, dirnames, filenames in os.walk(folder):
        dirnames[:] = [d for d in dirnames if os.path.join(dirpath, d) not in asmdef_folders]
        for f in filenames:
            if f.endswith(".cs"):
                yield os.path.join(dirpath, f)


def strip_comments(text):
    """Drops comments and the contents of string and char literals, which can mention namespaces without using them."""
    text = re.sub(r"'(?:\\.|[^'\\\n])'", "''", text)
    text = re.sub(r'@"(?:""|[^"])*"', '""', text)
    text = re.sub(r'"(?:\\.|[^"\\\n])*"', '""', text)
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return re.sub(r"//[^\n]*", "", text)


def main():
    asmdefs = find_asmdefs()
    folders = {folder for folder, _, _ in asmdefs}
    by_name = {d["name"]: d for _, d, _ in asmdefs}
    own_namespaces = sorted(((d.get("rootNamespace") or d["name"], d["name"]) for _, d, _ in asmdefs), key=lambda p: -len(p[0]))
    errors = []

    for folder, d, rel in asmdefs:
        name = d["name"]
        refs = set(d.get("references", [])) | set(d.get("precompiledReferences", []))
        editor_only = d.get("includePlatforms") == ["Editor"]
        engine_free = d.get("noEngineReferences", False)
        for r in d.get("references", []):
            if r.startswith("GUID:"):
                errors.append("%s: use assembly names, not GUID references (%s)" % (rel, r))
            elif r not in by_name and r not in KNOWN_PACKAGE_ASSEMBLIES:
                errors.append("%s: references unknown assembly %s" % (rel, r))
        for script in owned_scripts(folder, folders - {folder}):
            srel = os.path.relpath(script, ROOT)
            with open(script, encoding="utf-8") as fh:
                text = strip_comments(fh.read())
            used = set(USING.findall(text)) | set(m.group(1) for m in QUALIFIED.finditer(text))
            for ns in used:
                if ns == "UnityEngine" or ns.startswith("UnityEngine."):
                    if engine_free:
                        errors.append("%s: %s is engine-free but uses %s" % (srel, name, ns))
                if ns == "UnityEditor" or ns.startswith("UnityEditor."):
                    if not editor_only and "#if UNITY_EDITOR" not in text:
                        errors.append("%s: runtime assembly %s uses %s outside #if UNITY_EDITOR" % (srel, name, ns))
                    continue
                for prefix, assembly in PACKAGE_NAMESPACES:
                    if ns == prefix or ns.startswith(prefix + "."):
                        if assembly not in refs:
                            errors.append("%s: uses %s but %s does not reference %s" % (srel, ns, name, assembly))
                        break
                else:
                    for root_ns, assembly in own_namespaces:
                        if ns == root_ns or ns.startswith(root_ns + "."):
                            if assembly != name and assembly not in refs:
                                errors.append("%s: uses %s but %s does not reference %s" % (srel, ns, name, assembly))
                            break

    if errors:
        for e in sorted(set(errors)):
            print(e)
        sys.exit(1)
    print("%d assembly definitions reference what their code uses" % len(asmdefs))


if __name__ == "__main__":
    main()
