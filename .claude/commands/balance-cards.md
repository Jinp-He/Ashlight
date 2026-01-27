# 卡牌数值平衡工具

执行卡牌数值平衡分析和修改。

## 使用方法

输入 `/balance-cards` 或 `/balance-cards [角色名]` 来执行平衡操作。

## 参数

- 无参数: 分析所有角色的卡牌数值
- `rocket` / `战士`: 只分析战士卡牌
- `irene` / `法师`: 只分析法师卡牌
- `zhouzhou` / `游侠`: 只分析游侠卡牌
- `apply`: 应用推荐的平衡修改到 JSON 文件

## 平衡公式

```
基础伤害 = 格数 × 5
格数 = Channeling + Duration + Recoil

调整系数:
- AOE: ×0.8
- 迅捷: -3
- 控制: -2~3
- 条件伤害: 基础×0.8, 触发×1.5~2
- 护甲: 1护甲 ≈ 0.8伤害
- 穿甲: -5伤害
```

## 示例

- 2格普通攻击 = 10伤害
- 3格普通攻击 = 15伤害
- 3格AOE = 12伤害
- 5格大招 = 25伤害

---

当用户执行此命令时，请:

1. 读取 `f:\Ashlight\Assets\Resources\Config\character_tbcardinfo.json`
2. 分析每张卡牌的:
   - 总格数 (Channeling + Duration + Recoil)
   - 当前伤害值
   - 推荐伤害值 (基于平衡公式)
   - 差异百分比
3. 生成平衡报告表格
4. 如果用户指定 `apply`，则修改 JSON 文件

$ARGUMENTS
