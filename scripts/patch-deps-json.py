"""deps.json と runtimeconfig.json をパッチして runtime/ サブフォルダからDLLを解決できるようにする"""
import json
import glob
import os
import sys

stage_dir = sys.argv[1]

# deps.json をパッチ
for deps_file in glob.glob(os.path.join(stage_dir, "*.deps.json")):
    with open(deps_file, "r", encoding="utf-8-sig") as f:
        data = json.load(f)

    # targets 内の DLL パスをフラット化（サブディレクトリ除去）
    for target_name in data.get("targets", {}):
        target = data["targets"][target_name]
        for pkg_name in target:
            pkg = target[pkg_name]
            for section in ["runtime", "native"]:
                if section in pkg and isinstance(pkg[section], dict):
                    new_entries = {}
                    for dll_path, dll_info in pkg[section].items():
                        flat_name = os.path.basename(dll_path)
                        new_entries[flat_name] = dll_info
                    pkg[section] = new_entries

    # libraries 内の path を "." に設定
    for lib_name in data.get("libraries", {}):
        lib = data["libraries"][lib_name]
        if lib.get("type") in ("runtimepack", "package"):
            lib["path"] = "."

    with open(deps_file, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)
    print(f"  Patched: {os.path.basename(deps_file)}")

# runtimeconfig.json に additionalProbingPaths を追加
for rc_file in glob.glob(os.path.join(stage_dir, "*.runtimeconfig.json")):
    with open(rc_file, "r", encoding="utf-8-sig") as f:
        data = json.load(f)

    opts = data.setdefault("runtimeOptions", {})
    if "additionalProbingPaths" not in opts:
        opts["additionalProbingPaths"] = ["runtime"]

    with open(rc_file, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)
    print(f"  Patched: {os.path.basename(rc_file)}")
