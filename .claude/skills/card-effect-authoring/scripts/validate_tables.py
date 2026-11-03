# -*- coding: utf-8 -*-
"""校验 CardInfo + EnemySkillInfo 的 Effects 单元格与描述占位符，并检查战斗映射。

对照 defines.xml 检查：类型名、EffectType 缩写、字段数、buff_id/card_id 引用。
描述 {X}/{X:i} 占位符按各自解析器的字母表校验：
  - 卡牌 CardDescriptionParser：A/D/T/H/B/N/F/V（V 是 F 别名）
  - 敌技 EnemySkillDescriptionParser：只认 A/D/T/H/B/N（无 V/F——用了会原样显示）
战斗映射（WARN，不影响退出码）：直接解析战斗代码里的 `is XxxEffect`，
表里用到但未映射的类型 = 导表能过但战斗中是哑效果。
  - 卡牌 -> CardPlayResolver.cs（当前 ATB 路径）
  - 敌技 -> EnemySkillToTimelineConverter.cs

用法（仓库根目录）：
    python .claude/skills/card-effect-authoring/scripts/validate_tables.py [--repo PATH]
退出码 0 = 无 ERROR（WARN 不算）；1 = 有 ERROR。
"""
import argparse
import os
import re
import sys
from xml.etree import ElementTree as ET

from tablelib import read_sheet, header_columns, data_rows, collect_ids

EFFECT_TYPE_ALIASES = {"A", "D", "T", "H", "N", "B", "F"}
ALIAS_TO_ENUM = {"A": "Attack", "D": "Defense", "T": "TimeSlot", "H": "Heal",
                 "N": "Null", "B": "Buff", "F": "Fill"}
# 占位符字母表：与各解析器的 GetEffectTypeFromCode 保持一致
CARD_PLACEHOLDERS = dict(ALIAS_TO_ENUM, V="Fill")   # CardDescriptionParser
ENEMY_PLACEHOLDERS = {k: v for k, v in ALIAS_TO_ENUM.items() if k != "F"}  # 敌技解析器无 V/F
PLACEHOLDER_RE = re.compile(r"\{([A-Z])(?::(\d+))?\}")
IS_EFFECT_RE = re.compile(r"\bis\s+(\w+Effect)\b")


def load_effect_schema(defines_path):
    root = ET.parse(defines_path).getroot()
    return {b.get("name"): [v.get("name") for v in b.findall("var")]
            for b in root.iter("bean") if b.get("parent") == "Effect"}


def mapped_effect_types(cs_path):
    """从战斗代码提取已映射的 effect 类型（跳过 // 注释行）。"""
    mapped = set()
    with open(cs_path, encoding="utf-8") as f:
        for line in f:
            if line.strip().startswith("//"):
                continue
            mapped.update(IS_EFFECT_RE.findall(line))
    return mapped


def validate_table(label, xlsx, schema, placeholders, card_ids, buff_ids,
                   mapped, problems, warnings):
    rows = read_sheet(xlsx)
    cols = header_columns(rows)
    id_col, eff_col, desc_col = cols.get("Id"), cols.get("Effects"), cols.get("Description")
    if eff_col is None:
        problems.append(f"[{label}] 未找到 Effects 列")
        return

    for _, row in data_rows(rows):
        rid = row[id_col] if id_col is not None and id_col < len(row) else "?"
        cell = row[eff_col] if eff_col < len(row) else ""
        desc = row[desc_col] if desc_col is not None and desc_col < len(row) else ""

        type_counts = {}
        for eff in cell.split(";"):
            eff = eff.strip()
            if not eff:
                continue
            parts = eff.split(",")
            tname = parts[0]
            if len(parts) >= 2:
                en = ALIAS_TO_ENUM.get(parts[1])
                if en:
                    type_counts[en] = type_counts.get(en, 0) + 1
            if tname not in schema:
                problems.append(f"[{label}:{rid}] 未知 effect 类型 '{tname}'  ({eff})")
                continue
            if mapped is not None and tname not in mapped:
                warnings.append(f"[{label}:{rid}] {tname} 战斗侧无映射，将是哑效果  ({eff})")
            if len(parts) < 2 or parts[1] not in EFFECT_TYPE_ALIASES:
                got = parts[1] if len(parts) > 1 else "(缺失)"
                problems.append(f"[{label}:{rid}] {tname} 的 EffectType 非法: '{got}'  ({eff})")
            expected = 2 + len(schema[tname])
            if len(parts) != expected:
                problems.append(f"[{label}:{rid}] {tname} 字段数不符: 期望 {expected} 段，"
                                f"实得 {len(parts)}  ({eff})")
            fields = schema[tname]
            if tname == "BuffEffect" and "buff_id" in fields and buff_ids is not None:
                bi = 2 + fields.index("buff_id")
                if bi < len(parts) and parts[bi] not in buff_ids:
                    problems.append(f"[{label}:{rid}] 引用了不存在的 buff_id '{parts[bi]}'")
            if tname == "BuffConditionalEffect" and "buff_id" in fields and buff_ids is not None:
                bi = 2 + fields.index("buff_id")
                if bi < len(parts) and parts[bi] not in buff_ids:
                    problems.append(f"[{label}:{rid}] 引用了不存在的 buff_id '{parts[bi]}'")
            if tname == "AddToHandEffect" and "card_id" in fields:
                ki = 2 + fields.index("card_id")
                if ki < len(parts) and parts[ki] not in card_ids:
                    problems.append(f"[{label}:{rid}] 引用了不存在的 card_id '{parts[ki]}'")

        if desc and "{" in desc:
            for mt in PLACEHOLDER_RE.finditer(desc):
                code, idx = mt.group(1), int(mt.group(2) or 0)
                en = placeholders.get(code)
                if en is None:
                    problems.append(f"[{label}:{rid}] 占位符 {mt.group(0)} 字母 '{code}' "
                                    f"该表解析器不认识（合法: {'/'.join(sorted(placeholders))}）  desc={desc!r}")
                elif type_counts.get(en, 0) < idx + 1:
                    problems.append(f"[{label}:{rid}] 占位符 {mt.group(0)} 匹配不到效果："
                                    f"需要第 {idx} 个 {en} 效果，实有 {type_counts.get(en, 0)} 个  desc={desc!r}")


def main(repo):
    j = lambda p: os.path.join(repo, p)
    schema = load_effect_schema(j("DataTables/Defines/defines.xml"))
    card_ids = collect_ids(j("DataTables/Datas/Character/#CardInfo.xlsx"))
    buff_path = j("DataTables/Datas/#BuffInfo.xlsx")
    buff_ids = collect_ids(buff_path) if os.path.exists(buff_path) else None

    problems, warnings = [], []
    validate_table(
        "Card", j("DataTables/Datas/Character/#CardInfo.xlsx"), schema,
        CARD_PLACEHOLDERS, card_ids, buff_ids,
        mapped_effect_types(j("Assets/Scripts/Battle/Core/Engine/CardPlayResolver.cs")),
        problems, warnings)
    validate_table(
        "Enemy", j("DataTables/Datas/Enemy/#EnemySkillInfo.xlsx"), schema,
        ENEMY_PLACEHOLDERS, card_ids, buff_ids,
        mapped_effect_types(j("Assets/Scripts/Battle/Core/Engine/EnemySkillToTimelineConverter.cs")),
        problems, warnings)

    if warnings:
        print(f"WARN {len(warnings)} 条（战斗中不生效，导表能过）：")
        for w in warnings:
            print("  ! " + w)
    if problems:
        print(f"ERROR {len(problems)} 条：")
        for p in problems:
            print("  - " + p)
        return 1
    print("OK：Effects 编码 + 描述占位符全部通过（WARN 见上，如有）。")
    return 0


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--repo", default=".", help="仓库根目录")
    args = ap.parse_args()
    sys.exit(main(args.repo))
