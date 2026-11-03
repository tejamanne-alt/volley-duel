#!/usr/bin/env python3
"""Create missing Unity .meta files with deterministic GUIDs.

Unity assigns random GUIDs when it first imports a file, so two clones would disagree about every
script's identity. This script gives each file under Assets/ and Packages/com.duel.core/ a GUID
derived from its path, plus the importer settings it needs. Native plug-ins are restricted to their
platform: Swift on iOS, Kotlin on Android.

  python3 scripts/gen_meta.py           # write missing .meta files
  python3 scripts/gen_meta.py --check   # fail if any are missing or orphaned (used by check.sh)
"""
import hashlib
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TREES = ["Assets", os.path.join("Packages", "com.duel.core")]
SKIP_DIRS = {".git", "bin", "obj", "Library", "Temp", "Logs"}

TAIL = "  userData: \n  assetBundleName: \n  assetBundleVariant: \n"


def guid_for(rel):
    return hashlib.md5(("volley-duel:" + rel.replace(os.sep, "/")).encode("utf-8")).hexdigest()


def plugin_meta(platform_block):
    return (
        "PluginImporter:\n"
        "  externalObjects: {}\n"
        "  serializedVersion: 2\n"
        "  iconMap: {}\n"
        "  executionOrder: {}\n"
        "  defineConstraints: []\n"
        "  isPreloaded: 0\n"
        "  isOverridable: 1\n"
        "  isExplicitlyReferenced: 0\n"
        "  validateReferences: 1\n"
        "  platformData:\n"
        "  - first:\n"
        "      Any: \n"
        "    second:\n"
        "      enabled: 0\n"
        "      settings: {}\n"
        "  - first:\n"
        "      Editor: Editor\n"
        "    second:\n"
        "      enabled: 0\n"
        "      settings:\n"
        "        DefaultValueInitialized: true\n"
        + platform_block + TAIL
    )


IOS_ONLY = plugin_meta(
    "  - first:\n"
    "      iPhone: iOS\n"
    "    second:\n"
    "      enabled: 1\n"
    "      settings: {}\n"
)

ANDROID_ONLY = plugin_meta(
    "  - first:\n"
    "      Android: Android\n"
    "    second:\n"
    "      enabled: 1\n"
    "      settings: {}\n"
)


def importer(rel, is_dir):
    if is_dir:
        return "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n" + TAIL
    ext = os.path.splitext(rel)[1].lower()
    name = os.path.basename(rel)
    if ext == ".cs":
        return (
            "MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n"
            "  executionOrder: 0\n  icon: {instanceID: 0}\n" + TAIL
        )
    if ext == ".asmdef":
        return "AssemblyDefinitionImporter:\n  externalObjects: {}\n" + TAIL
    if name == "package.json":
        return "PackageManifestImporter:\n  externalObjects: {}\n" + TAIL
    if ext in (".json", ".md", ".txt"):
        return "TextScriptImporter:\n  externalObjects: {}\n" + TAIL
    if ext == ".swift":
        return IOS_ONLY
    if ext in (".kt", ".java"):
        return ANDROID_ONLY
    return "DefaultImporter:\n  externalObjects: {}\n" + TAIL


def expected():
    out = {}
    for tree in TREES:
        base = os.path.join(ROOT, tree)
        if not os.path.isdir(base):
            continue
        for dirpath, dirnames, filenames in os.walk(base):
            dirnames[:] = sorted(d for d in dirnames if d not in SKIP_DIRS and not d.startswith("."))
            rel_dir = os.path.relpath(dirpath, ROOT)
            if rel_dir not in TREES:
                out[rel_dir + ".meta"] = (rel_dir, True)
            for f in sorted(filenames):
                if f.endswith(".meta") or f.startswith("."):
                    continue
                rel = os.path.join(rel_dir, f)
                out[rel + ".meta"] = (rel, False)
    return out


def existing():
    found = set()
    for tree in TREES:
        base = os.path.join(ROOT, tree)
        for dirpath, dirnames, filenames in os.walk(base):
            dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS and not d.startswith(".")]
            for f in filenames:
                if f.endswith(".meta"):
                    found.add(os.path.relpath(os.path.join(dirpath, f), ROOT))
    return found


def main():
    check = "--check" in sys.argv
    want = expected()
    have = existing()
    missing = sorted(set(want) - have)
    orphaned = sorted(have - set(want))
    if check:
        for m in missing:
            print("missing " + m)
        for o in orphaned:
            print("orphaned " + o)
        if missing or orphaned:
            print("run: python3 scripts/gen_meta.py")
            sys.exit(1)
        print("%d .meta files OK" % len(want))
        return
    for m in missing:
        rel, is_dir = want[m]
        with open(os.path.join(ROOT, m), "w", newline="\n") as fh:
            fh.write("fileFormatVersion: 2\nguid: %s\n%s" % (guid_for(rel), importer(rel, is_dir)))
    for o in orphaned:
        os.remove(os.path.join(ROOT, o))
    print("wrote %d, removed %d .meta files" % (len(missing), len(orphaned)))


if __name__ == "__main__":
    main()
