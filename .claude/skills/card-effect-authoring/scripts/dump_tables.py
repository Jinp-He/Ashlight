# -*- coding: utf-8 -*-
"""紧凑输出数据表，替代每次现写 openpyxl 代码。

用法（仓库根目录）：
    python .claude/skills/card-effect-authoring/scripts/dump_tables.py            # 三张表
    python .claude/skills/card-effect-authoring/scripts/dump_tables.py -t card    # card|enemy|buff
    python .claude/skills/card-effect-authoring/scripts/dump_tables.py -t card --full  # 附加列

OFF 前缀 = 该行被 ## 注释，未导入游戏。
"""
import argparse
import os

from tablelib import read_sheet, header_columns, data_rows

TABLES = {
    "card": ("DataTables/Datas/Character/#CardInfo.xlsx",
             ["Id", "Name", "Description", "Effects"],
             ["CardType", "TargetType", "Energy", "ExecutingCost",
              "TargetZone", "CastZone", "IsEthereal", "IsExhaust"]),
    "enemy": ("DataTables/Datas/Enemy/#EnemySkillInfo.xlsx",
              ["Id", "Name", "Description", "Effects"],
              ["TargetType", "ExecutingCost", "TargetZone"]),
    "buff": ("DataTables/Datas/#BuffInfo.xlsx",
             ["Id", "Name", "Description", "Polarity"],
             ["DefaultDuration", "MaxStack", "RefreshOnReapply"]),
}


def dump(key, full):
    path, base_cols, extra_cols = TABLES[key]
    cols = base_cols + (extra_cols if full else [])
    rows = read_sheet(path)
    header = header_columns(rows)
    print(f"===== {key}: {path} =====")
    for is_off, row in data_rows(rows, include_off=True):
        get = lambda name: row[header[name]] if name in header and header[name] < len(row) else ""
        if not get("Id"):
            continue
        prefix = "OFF " if is_off else ""
        print(prefix + " | ".join(f"{c}={get(c)}" if i >= 2 else get(c)
                                  for i, c in enumerate(cols)))


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("-t", "--table", choices=[*TABLES, "all"], default="all")
    ap.add_argument("--full", action="store_true", help="附加 CardType/Energy 等列")
    ap.add_argument("--repo", default=".", help="仓库根目录")
    args = ap.parse_args()
    os.chdir(args.repo)
    for key in (TABLES if args.table == "all" else [args.table]):
        dump(key, args.full)
