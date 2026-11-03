# Ashlight

Unity 回合制卡牌战斗原型：公共回合时钟（ATB 离散调度）× 两区站位 × 可干扰敌人意图。

## 入口

- **设计文档**：[docs/README.md](docs/README.md)（导航页，含基准/定稿/草稿/归档分层）
- **AI 协作指南**：[CODEX.md](CODEX.md)
- **数据表（Luban）**：[DataTables/README.md](DataTables/README.md) — 改表只改 Excel 源，改完跑 `DataTables/gen.bat`，勿直接改 `Assets/Resources/Config/*.json`

## 目录速览

| 位置 | 内容 |
|---|---|
| `Assets/Scripts/Battle/` | 战斗核心（BattleManager、TurnResolver 等） |
| `Assets/Scripts/UI/` | 战斗/卡牌 UI |
| `DataTables/Datas/` | Excel 配置源（卡牌/敌人/Buff/天气/遭遇） |
| `docs/` | 设计文档（现行）；`docs/archive/` 废弃归档 |
