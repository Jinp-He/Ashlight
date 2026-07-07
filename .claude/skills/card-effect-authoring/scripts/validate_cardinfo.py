#!/usr/bin/env python3
"""
校验 Ashlight CardInfo 表里每个 Effects 单元格是否合法。
对照 DataTables/Defines/defines.xml 检查：类型名、字段数、EffectType 缩写，
以及 BuffEffect.buff_id / AddToHandEffect.card_id 的引用是否存在。

用法（从仓库根目录跑）：
    python .claude/skills/card-effect-authoring/scripts/validate_cardinfo.py
    python .claude/skills/card-effect-authoring/scripts/validate_cardinfo.py --repo F:/Ashlight

退出码 0 = 全部通过；1 = 发现问题。
"""
import argparse
import os
import re
import sys
from xml.etree import ElementTree as ET  # 仅用于解析 defines.xml

EFFECT_TYPE_ALIASES = {"A", "D", "T", "H", "N", "B", "F"}  # EffectEnum 缩写

# 描述里 {X}/{X:i} 占位符的字母 -> EffectEnum 名。
# 必须与 CardDescriptionParser.GetEffectTypeFromCode 保持一致（含 V 是 Fill 的别名）。
PLACEHOLDER_CODE_TO_ENUM = {
    "A": "Attack", "D": "Defense", "T": "TimeSlot", "H": "Heal",
    "B": "Buff", "N": "Null", "F": "Fill", "V": "Fill",
}
# effect 单元格第 2 段（EffectType 缩写）-> EffectEnum 名
EFFECT_ALIAS_TO_ENUM = {
    "A": "Attack", "D": "Defense", "T": "TimeSlot", "H": "Heal",
    "N": "Null", "B": "Buff", "F": "Fill",
}
PLACEHOLDER_RE = re.compile(r"\{([A-Z])(?::(\d+))?\}")


def load_effect_schema(defines_path):
    """解析 defines.xml，返回 {子类名: [var_name,...]}（仅 parent='Effect' 的 bean，字段按声明顺序）。"""
    root = ET.parse(defines_path).getroot()
    schema = {}
    for bean in root.iter("bean"):
        if bean.get("parent") == "Effect":
            name = bean.get("name")
            fields = [v.get("name") for v in bean.findall("var")]
            schema[name] = fields
    return schema


def read_sheet(xlsx_path):
    """读数据 sheet，返回行列表（每行是字符串列表，按真实列号对齐，空单元格为 ''）。

    用 openpyxl 而非手写 zipfile 读取，原因：xlsx 里逻辑 sheet 名（如 'Rocket'）到物理文件
    （sheet1.xml/sheet2.xml…）的映射走 rId 关系表，rId1 未必是 sheet1.xml。硬取 sheet1.xml
    会读串 sheet。这里改为遍历所有 worksheet，选 A1=='##var' 的那张（Luban 数据表的标志），
    找不到再退回第一张。
    """
    import openpyxl

    wb = openpyxl.load_workbook(xlsx_path, data_only=True, read_only=True)
    merged = []
    fallback = None
    for ws in wb.worksheets:
        sheet_rows = [
            ["" if v is None else str(v) for v in row]
            for row in ws.iter_rows(values_only=True)
        ]
        if fallback is None and sheet_rows:
            fallback = sheet_rows
        # Luban 会把 workbook 里所有 A1=='##var' 的 sheet 合并成一张逻辑表
        # （如 #CardInfo 的飞刀 token Extra001 就单独放在 Sheet2）。这里同样合并，
        # 否则只读第一张会漏掉其它 sheet 的行，导致 card_id/buff_id 引用误报缺失。
        if sheet_rows and sheet_rows[0] and sheet_rows[0][0].strip() == "##var":
            merged.extend(sheet_rows)
    wb.close()
    return merged if merged else (fallback or [])


def header_columns(rows):
    """从 ##var 行取列名 -> 列号 映射。"""
    for row in rows:
        if row and row[0] == "##var":
            return {name: i for i, name in enumerate(row) if name}
    return {}


def data_rows(rows):
    """产出数据行（跳过元信息行和整行空行）。

    注意：Luban 表的数据行 A 列本就是空的（##var/##type 等元标记才在 A 列，
    真正的 Id 在 B 列）。因此绝不能用 `not row[0]` 判空——那会把每一条数据行都跳过。
    只跳过：整行全空、或 A 列以 '##' 开头的元信息行。
    """
    for row in rows:
        if not any((c or "").strip() for c in row):  # 整行空
            continue
        if row[0].startswith("##"):  # 元信息行 (##var/##type/##group/##)
            continue
        yield row


def collect_ids(xlsx_path, id_col_name="Id"):
    rows = read_sheet(xlsx_path)
    cols = header_columns(rows)
    ci = cols.get(id_col_name)
    ids = set()
    if ci is None:
        return ids
    for row in data_rows(rows):
        if ci < len(row) and row[ci]:
            ids.add(row[ci])
    return ids


def validate(repo):
    defines = os.path.join(repo, "DataTables/Defines/defines.xml")
    cardinfo = os.path.join(repo, "DataTables/Datas/Character/#CardInfo.xlsx")
    buffinfo = os.path.join(repo, "DataTables/Datas/#BuffInfo.xlsx")

    schema = load_effect_schema(defines)
    card_ids = collect_ids(cardinfo)
    buff_ids = collect_ids(buffinfo) if os.path.exists(buffinfo) else None

    rows = read_sheet(cardinfo)
    cols = header_columns(rows)
    id_col = cols.get("Id")
    eff_col = cols.get("Effects")
    desc_col = cols.get("Description")
    if eff_col is None:
        print("ERROR: 未找到 Effects 列", file=sys.stderr)
        return 1

    problems = []
    for row in data_rows(rows):
        card_id = row[id_col] if id_col is not None and id_col < len(row) else "?"
        cell = row[eff_col] if eff_col < len(row) else ""
        desc = row[desc_col] if desc_col is not None and desc_col < len(row) else ""

        # 该卡各 EffectEnum 的效果数量，供描述占位符校验用
        type_counts = {}
        for eff in cell.split(";"):
            eff = eff.strip()
            if not eff:
                continue
            parts = eff.split(",")
            tname = parts[0]
            # 统计 EffectEnum（即便后面字段有问题，类型仍可计入占位符匹配）
            if len(parts) >= 2:
                en = EFFECT_ALIAS_TO_ENUM.get(parts[1])
                if en:
                    type_counts[en] = type_counts.get(en, 0) + 1
            # 1. 类型名
            if tname not in schema:
                problems.append(f"[{card_id}] 未知 effect 类型 '{tname}'  ({eff})")
                continue
            # 2. EffectType 缩写
            if len(parts) < 2 or parts[1] not in EFFECT_TYPE_ALIASES:
                got = parts[1] if len(parts) > 1 else "(缺失)"
                problems.append(f"[{card_id}] {tname} 的 EffectType 非法: '{got}'  ({eff})")
            # 3. 字段数（首段类型名 + EffectType + 各 var）
            expected = 2 + len(schema[tname])
            if len(parts) != expected:
                problems.append(
                    f"[{card_id}] {tname} 字段数不符: 期望 {expected} 段(含类型名/EffectType)，"
                    f"实得 {len(parts)}  ({eff})"
                )
            # 4. 引用 Id
            fields = schema[tname]
            if tname == "BuffEffect" and "buff_id" in fields and buff_ids is not None:
                bi = 2 + fields.index("buff_id")
                if bi < len(parts) and parts[bi] not in buff_ids:
                    problems.append(f"[{card_id}] BuffEffect 引用了不存在的 buff_id '{parts[bi]}'")
            if tname == "AddToHandEffect" and "card_id" in fields:
                ki = 2 + fields.index("card_id")
                if ki < len(parts) and parts[ki] not in card_ids:
                    problems.append(f"[{card_id}] AddToHandEffect 引用了不存在的 card_id '{parts[ki]}'")

        # 5. 描述占位符 {X}/{X:i} 必须能匹配到卡上对应 EffectType 的效果
        #    （否则运行时 CardDescriptionParser 替换不了，会原样显示 {X}，如 {N}）
        if desc and "{" in desc:
            for mt in PLACEHOLDER_RE.finditer(desc):
                code = mt.group(1)
                idx = int(mt.group(2)) if mt.group(2) else 0
                en = PLACEHOLDER_CODE_TO_ENUM.get(code)
                if en is None:
                    problems.append(
                        f"[{card_id}] 描述占位符 {mt.group(0)} 用了未知字母 '{code}'"
                        f"（合法: A/D/T/H/B/N/F/V）  desc={desc!r}"
                    )
                elif type_counts.get(en, 0) < idx + 1:
                    problems.append(
                        f"[{card_id}] 描述占位符 {mt.group(0)} 匹配不到效果："
                        f"需要第 {idx} 个 {en} 类型效果，但该卡只有 {type_counts.get(en, 0)} 个"
                        f"（{code} 对应 {en}）  desc={desc!r}"
                    )

    if problems:
        print(f"发现 {len(problems)} 个问题：")
        for p in problems:
            print("  - " + p)
        return 1
    print("OK：所有 Effects 单元格 + 描述占位符通过校验。")
    return 0


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--repo", default=".", help="仓库根目录（默认当前目录）")
    args = ap.parse_args()
    sys.exit(validate(args.repo))
