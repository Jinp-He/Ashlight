# ArtSource — 美术资源暂存区

> **这是什么**：你整理美术时的暂存文件夹，位于 Godot 工程（`ASHLIGHT\`）**之外**，但仍在 `ai\` 内统一管理。
> **最终去向**：整理齐后，由 AI 程序员把这里的文件**导入**进 `A:\ASHlight\ai\ASHLIGHT\assets\`，代码才能用 `res://assets/...` 加载。
> **对应规格**：`A:\ASHlight\【单位部件·视觉规格】.md` §12。

## 文件夹 → 放什么

| 文件夹 | 放什么 | 对应规格 |
|--------|--------|----------|
| `units/{id}/` | 该角色/怪物的**五态 PNG**：`{id}_Idle.png` / `{id}_Attack.png` / `{id}_Cast.png` / `{id}_Hit.png` / `{id}_Charge.png`（Charge 占位，暂无美术，回退 Idle） | §12.3 B 组 |
| `ui/unit_hud/` | `hp_bar_fill.png`（血条实心层·当前血量）、`hp_bar_bg.png`（血条空心底槽·损失后暗色轨道）、`hp_bar_frame.png`（外框·可选）、`hp_shield.png`（护盾图标）、`buff_toast.png`（滑入提示底）、`buff_box_frame.png`（可选虚线框） | §12.3 A 组 |
| `buffs/icons/` | 每个 buff 一个图标：`buff{nnn}.png`（如 `buff001.png`） | §12.3 A 组 #4 |
| `tooltip/` | `tooltip_bg.png`（补充框背景，已放入） | §12.3 C 组 |
| `fonts/` | `Biantao.ttf`（字魂扁桃体）✅已放入、`DouyinSansBold.otf`（抖音美好体）✅已放入；黑体角标无需单独文件，复用 DouyinSansBold（不加粗） | §12.4 |

## 已知单位 id（units/ 下已建好对应空文件夹）

- 我方：`Zhouzhou` / `Irene` / `Rocket`（洛克）
- 敌方：`DogKnight` / `Mafe` / `Remy` / `Tusk` / `VeiledAcolyte` / `Wangg` / `Grey` / `TheDisciplinarian` / `Banner` / `AshrobeDevotee`（无 `dog`）

## 命名铁律

- 全英文 + 下划线，**禁止中文路径/文件名**（Godot 资源加载稳定性）。
- 没美术的，先放**占位图**（粉色/灰色方块）即可，程序员先用占位跑通，你后面替换真图。
- 每个单位只需 5 张五态 PNG；某态缺失会自动回退 `Idle`，不必凑齐。
