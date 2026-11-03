# 敌人美术 Todo List

数据来源：`Assets/Resources/Config/enemy_tbenemyinfo.json`。仅追踪战斗动作帧和行动顺序图标。

- 动作资源路径：`Enemy/<英文资源名>/Sprites/{Idle,Attack,Cast,Hit}.png`
- 行动顺序资源路径：`UI/ActionOrder/Img_<英文资源名>.png`
- `[x]` = 文件已存在且命名符合当前配置；`[ ]` = 缺失或需按英文资源名改名。

| ID | 中文名 | 英文资源名 | Idle | Attack | Cast | Hit | ActionOrder |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Enemy001 | 狗骑士 | DogKnight | [x] | [x] | [x] | [x] | [x] `Img_DogKnight` |
| Enemy002 | 马匪 | Mafe | [x] | [x] | [x] | [x] | [x] `Img_Mafe` |
| Enemy003 | 重装步兵 | DogKnight（复用） | [x] | [x] | [x] | [x] | [x] `Img_DogKnight` |
| Enemy004 | 汪汪 | Wangg | [x] | [x] | [x] | [x] | [x] `Img_Wangg` |
| Enemy005 | 雷米 | Remy | [x] | [x] | [x] | [x] | [x] `Img_Remy` |
| Enemy006 | Boss1号 | Boss1 | [ ] | [ ] | [ ] | [ ] | [ ] `Img_Boss1` |
| Enemy101 | 掠林狂犬 | Remy（复用） | [x] | [x] | [x] | [x] | [x] `Img_Remy` |
| Enemy102 | 影猎收税人 | Wangg（复用） | [x] | [x] | [x] | [x] | [x] `Img_Wangg` |
| Enemy103 | 蚀骨炮台 | Mafe（复用） | [x] | [x] | [x] | [x] | [x] `Img_Mafe` |
| Enemy104 | 锈牙掠犬 | DogKnight（复用） | [x] | [x] | [x] | [x] | [x] `Img_DogKnight` |
| Enemy105 | 断律者 | Boss1（复用） | [ ] | [ ] | [ ] | [ ] | [ ] `Img_Boss1` |
| EnemyM01 | 灰袍信徒 | AshrobeDevotee | [x] | [x] | [x] | [x] | [x] `Img_AshrobeDevotee` |
| EnemyM02 | 蒙眼侍僧 | VeiledAcolyte | [x] | [x] | [x] | [x] | [x] `Img_VeiledAcolyte` |
| EnemyM03 | 戒律执事 | TheDisciplinarian | [x] | [x] | [x] | [x] | [x] `Img_TheDisciplinarian` |
| EnemyM04 | 缄默使徒 | Mafe（复用） | [x] | [x] | [x] | [x] | [x] `Img_Mafe` |
| EnemyW01 | 指裔 | Tusk（复用） | [x] | [x] | [x] | [x] | [x] `Img_Tusk` |
| EnemyW02 | 血肉猎手 | Wangg（复用） | [x] | [x] | [x] | [x] | [x] `Img_Wangg` |
| EnemyW03 | 多臂殉道者 | Mafe（复用） | [x] | [x] | [x] | [x] | [x] `Img_Mafe` |

## 已完成的 ActionOrder 命名修正

- [x] `Img_Ashrobe Devotee.png` → `Img_AshrobeDevotee.png`
- [x] `Img_Veiled Acolyte.png` → `Img_VeiledAcolyte.png`
- [x] `Img_The Disciplinarian.png` → `Img_TheDisciplinarian.png`
