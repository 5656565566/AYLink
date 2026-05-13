#!/usr/bin/env python3
"""
AYLink i18n 翻译键导出工具

从 AYLink.Desktop 项目的 XAML 和 C# 源码中提取所有翻译键 生成嵌套 JSON 模板文件

扫描模式：
  - XAML:  {i18n:Tr AppPage.CtxLaunch, DefaultText='启动应用'}
  - C#:    GetString("AppPage.CtxLaunch", "启动应用")
  - C#:    Instance["AppPage.CtxLaunch"] / Localizer["AppPage.CtxLaunch"]

输出嵌套 JSON
  {
    "LanguageName": "",
    "AppPage": {
      "CtxLaunch": "启动应用",
      "Search": "搜索"
    }
  }

用法：
  python scripts/export-i18n-keys.py
  python scripts/export-i18n-keys.py --scan-path ./AYLink.Desktop --output ./AYLink.Desktop/Language/template.json
"""

import argparse
import json
import os
import re
import sys
from collections import OrderedDict
from pathlib import Path


def find_files(scan_path: str, extension: str) -> list[Path]:
    """递归查找指定扩展名的文件"""
    return list(Path(scan_path).rglob(f"*{extension}"))


def extract_from_xaml(content: str) -> dict[str, str]:
    """从 XAML 内容中提取 {i18n:Tr Key, DefaultText='...'} 模式的翻译键"""
    keys = {}
    # 匹配 {i18n:Tr AppPage.CtxLaunch, DefaultText='启动应用'}
    # 也匹配 {i18n:Tr AppPage.CtxLaunch} (无默认文本)
    pattern = r"""\{i18n:Tr\s+([A-Za-z0-9_.]+)(?:\s*,\s*DefaultText\s*=\s*(['"])((?:\\.|(?!\2).)*)\2)?\s*\}"""
    for match in re.finditer(pattern, content):
        key = match.group(1)
        default_text = match.group(3) or ""
        default_text = default_text.replace("\\'", "'").replace('\\"', '"')
        if key not in keys or (keys[key] == "" and default_text != ""):
            keys[key] = default_text
    return keys


def extract_from_cs(content: str) -> dict[str, str]:
    """从 C# 内容中提取 GetString(...) 和索引器访问的翻译键"""
    keys = {}

    # 匹配 GetString("AppPage.CtxLaunch", "启动应用")
    pattern1 = r'GetString\(\s*"([A-Za-z0-9_.]+)"(?:\s*,\s*"([^"]*)")?\s*\)'
    for match in re.finditer(pattern1, content):
        key = match.group(1)
        default_text = match.group(2) or ""
        if key not in keys or (keys[key] == "" and default_text != ""):
            keys[key] = default_text

    # 匹配索引器 Instance["Key"] / Localizer["Key"] / _localizer["Key"]
    pattern2 = r'(?:Instance|Localizer|_localizer|localizer)\["([A-Za-z0-9_.]+)"\]'
    for match in re.finditer(pattern2, content):
        key = match.group(1)
        if key not in keys:
            keys[key] = ""

    # 匹配 [LocalizedRegularExpression(@"...", "DeviceSettings.InvalidResolution", "默认文本")]
    pattern3 = r'\[(?:Services\.Localization\.)?LocalizedRegularExpression\([^,]+,\s*"([A-Za-z0-9_.]+)"(?:,\s*"([^"]*)")?\s*\)\]'
    for match in re.finditer(pattern3, content):
        key = match.group(1)
        default_text = match.group(2) or ""
        if key not in keys or (keys[key] == "" and default_text != ""):
            keys[key] = default_text

    return keys


def build_nested_dict(flat_keys: dict[str, str]) -> OrderedDict:
    """将扁平化的点分隔键转换为嵌套 OrderedDict"""
    nested = OrderedDict()
    nested["LanguageName"] = ""

    for key in sorted(flat_keys.keys()):
        parts = key.split(".")
        current = nested

        for part in parts[:-1]:
            if part not in current:
                current[part] = OrderedDict()
            elif not isinstance(current[part], dict):
                # 如果已存在同名的字符串值，转换为字典
                current[part] = OrderedDict()
            current = current[part]

        leaf = parts[-1]
        current[leaf] = flat_keys[key]

    return nested


def print_summary(flat_keys: dict[str, str]) -> None:
    """打印按分组的键统计摘要"""
    groups: dict[str, int] = {}
    for key in flat_keys:
        parts = key.split(".")
        group = parts[0] if len(parts) > 1 else "(root)"
        groups[group] = groups.get(group, 0) + 1

    for group in sorted(groups.keys()):
        print(f"  {group}: {groups[group]} keys")


def flatten_nested_dict(data: dict, prefix: str = "") -> dict[str, str]:
    """将嵌套 JSON 结构展平为点分隔键，仅收集真实翻译键"""
    flat: dict[str, str] = {}

    for key, value in data.items():
        if key == "LanguageName":
            continue

        full_key = f"{prefix}.{key}" if prefix else key
        if isinstance(value, dict):
            flat.update(flatten_nested_dict(value, full_key))
        elif prefix:
            flat[full_key] = value

    return flat



def sync_language_file(template_dict: dict, lang_file_path: Path) -> None:
    """将模板中的新键同步到现有的语言文件中 保持现有翻译不变 并提示过期键"""
    try:
        with open(lang_file_path, "r", encoding="utf-8") as f:
            existing_data = json.load(f)
    except Exception as e:
        print(f"  Warning: Failed to read {lang_file_path}: {e}", file=sys.stderr)
        return

    template_flat_keys = set(flatten_nested_dict(template_dict).keys())
    existing_flat_keys = set(flatten_nested_dict(existing_data).keys())
    stale_keys = sorted(existing_flat_keys - template_flat_keys)

    def merge(tmpl, exist):
        result = OrderedDict()
        for k, v in tmpl.items():
            if k in exist:
                if isinstance(v, dict) and isinstance(exist[k], dict):
                    result[k] = merge(v, exist[k])
                else:
                    result[k] = exist[k]
            else:
                result[k] = v

        # 保留旧文件中存在但模板中没有的键（防止意外丢失数据）
        for k, v in exist.items():
            if k not in result:
                result[k] = v

        return result

    merged_data = merge(template_dict, existing_data)

    with open(lang_file_path, "w", encoding="utf-8") as f:
        json.dump(merged_data, f, ensure_ascii=False, indent=2)
        f.write("\n")
    print(f"  Synced: {lang_file_path.name}")
    if stale_keys:
        print(f"  Warning: {lang_file_path.name} contains {len(stale_keys)} stale keys:")
        for key in stale_keys:
            print(f"    - {key}")


def main():
    parser = argparse.ArgumentParser(
        description="AYLink i18n 翻译键导出工具 - 从源码提取翻译键生成嵌套 JSON"
    )
    parser.add_argument(
        "--scan-path",
        default="./AYLink.Desktop",
        help="要扫描的项目目录 默认: ./AYLink.Desktop",
    )
    parser.add_argument(
        "--output",
        default="./AYLink.Desktop/Language/template.json",
        help="输出 JSON 文件路径 默认: ./AYLink.Desktop/Language/template.json",
    )
    args = parser.parse_args()

    scan_path = args.scan_path
    output_path = args.output

    print("=== i18n Key Exporter ===")
    print(f"Scanning: {scan_path}")

    all_keys: dict[str, str] = {}

    # 扫描 XAML 文件
    axaml_files = find_files(scan_path, ".axaml")
    for file in axaml_files:
        try:
            content = file.read_text(encoding="utf-8")
            keys = extract_from_xaml(content)
            for k, v in keys.items():
                if k not in all_keys or (all_keys[k] == "" and v != ""):
                    all_keys[k] = v
        except Exception as e:
            print(f"  Warning: Failed to read {file}: {e}", file=sys.stderr)

    print(f"Scanned {len(axaml_files)} .axaml files")

    # 扫描 C# 文件
    cs_files = find_files(scan_path, ".cs")
    for file in cs_files:
        try:
            content = file.read_text(encoding="utf-8")
            keys = extract_from_cs(content)
            for k, v in keys.items():
                if k not in all_keys or (all_keys[k] == "" and v != ""):
                    all_keys[k] = v
        except Exception as e:
            print(f"  Warning: Failed to read {file}: {e}", file=sys.stderr)

    print(f"Scanned {len(cs_files)} .cs files")
    print(f"\nFound {len(all_keys)} unique translation keys.\n")

    if not all_keys:
        print("No translation keys found. Make sure you're using {i18n:Tr ...} in XAML")
        print("or GetString(...) in C# code.")
        return

    # 构建嵌套结构
    nested = build_nested_dict(all_keys)

    # 输出 JSON
    os.makedirs(os.path.dirname(output_path) or ".", exist_ok=True)
    with open(output_path, "w", encoding="utf-8") as f:
        json.dump(nested, f, ensure_ascii=False, indent=2)
        f.write("\n")

    print(f"Exported template to: {output_path}")
    
    print("\n=== Syncing Existing Language Files ===")
    lang_dir = Path(output_path).parent
    template_name = Path(output_path).name
    sync_count = 0
    if lang_dir.exists():
        for lang_file in lang_dir.glob("*.json"):
            if lang_file.name == template_name:
                continue
            sync_language_file(nested, lang_file)
            sync_count += 1
            
    if sync_count == 0:
        print("  No existing language files found to sync.")

    print("\n=== Key Summary ===")
    print_summary(all_keys)
    print(f"\nDone! Template updated and {sync_count} language files synced.")


if __name__ == "__main__":
    main()
