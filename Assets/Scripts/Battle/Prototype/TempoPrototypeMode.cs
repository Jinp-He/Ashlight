using System;
using System.Collections.Generic;
using Ashlight.Battle.Core.Data;
using Ashlight.Config;
using Ashlight.State.Runtime;
using cfg;
using Luban.SimpleJSON;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ashlight.Battle.Prototype
{
    /// <summary>
    /// “一牌一行动”实验规则。只有 BattleScene_TempoPrototype 会启用，正式战斗不受影响。
    /// 原型期复用 CardInfo.Energy 作为印刷行动间隔，避免修改正式 Luban 表结构。
    /// </summary>
    public static class TempoPrototypeMode
    {
        public const string SceneName = "BattleScene_TempoPrototype";
        public const string EncounterId = "TempoEncounter001";
        public const int PassActionDelay = 2;

        private const string CardTablePath = "Config/Prototype/tempo_tbcardinfo";
        private const string EnemySkillTablePath = "Config/Prototype/tempo_tbenemyskillinfo";
        private const string EnemyTablePath = "Config/Prototype/tempo_tbenemyinfo";
        private const string EncounterTablePath = "Config/Prototype/tempo_tbencounter";

        private static readonly Dictionary<string, int> ActionDelayByUnit = new Dictionary<string, int>();
        private static readonly HashSet<string> PlayedUnits = new HashSet<string>();

        private static readonly Dictionary<CharacterEnum, string[]> PrototypeDecks =
            new Dictionary<CharacterEnum, string[]>
            {
                {
                    CharacterEnum.Irene,
                    new[] { "TempoIrene001", "TempoIrene002", "TempoIrene003", "TempoIrene004" }
                },
                {
                    CharacterEnum.Rocket,
                    new[] { "TempoRocket001", "TempoRocket002", "TempoRocket003", "TempoRocket004" }
                },
                {
                    CharacterEnum.Zhouzhou,
                    new[] { "TempoZhouzhou001", "TempoZhouzhou002", "TempoZhouzhou003", "TempoZhouzhou004" }
                }
            };

        // 原型只实验规则，不额外制作临时美术。每张牌复用所属角色的一张正式卡图，
        // 同时保留 001～004 的差异，方便玩家在手牌和行动轨上快速辨认。
        private static readonly Dictionary<string, string> PrototypeVisualCardIds =
            new Dictionary<string, string>
            {
                { "TempoIrene001", "Irene001" },
                { "TempoIrene002", "Irene002" },
                { "TempoIrene003", "Irene003" },
                { "TempoIrene004", "Irene004" },
                { "TempoRocket001", "Rocket001" },
                { "TempoRocket002", "Rocket002" },
                { "TempoRocket003", "Rocket003" },
                { "TempoRocket004", "Rocket004" },
                { "TempoZhouzhou001", "Zhouzhou001" },
                { "TempoZhouzhou002", "Zhouzhou002" },
                { "TempoZhouzhou003", "Zhouzhou003" },
                { "TempoZhouzhou004", "Zhouzhou004" }
            };

        public static bool IsActive => SceneManager.GetActiveScene().name == SceneName;

        public static bool IsPrototypeCard(cfg.Character.CardInfo card)
        {
            return card != null && IsPrototypeCardId(card.Id);
        }

        public static bool IsPrototypeCardId(string cardId)
        {
            return !string.IsNullOrEmpty(cardId)
                   && cardId.StartsWith("Tempo", StringComparison.Ordinal);
        }

        /// <summary>
        /// 返回卡牌在 UI 中使用的美术资源 ID。原型牌始终映射到所属角色的正式卡图；
        /// 非原型牌保持原 ID，不影响正式战斗。
        /// </summary>
        public static string ResolveVisualCardId(string cardId)
        {
            return !string.IsNullOrEmpty(cardId)
                   && PrototypeVisualCardIds.TryGetValue(cardId, out string visualCardId)
                ? visualCardId
                : cardId;
        }

        /// <summary>在 BattleManager 验证 BattleInfo 前调用，让直接打开原型场景也始终获得固定队伍和遭遇。</summary>
        public static void PrepareBattleInfo(BattleInfo battleInfo)
        {
            if (!IsActive || battleInfo == null)
            {
                return;
            }

            EnsurePrototypeTablesInstalled();
            ActionDelayByUnit.Clear();
            PlayedUnits.Clear();

            battleInfo.PlayerCharacters = new List<CharacterEnum>
            {
                CharacterEnum.Irene,
                CharacterEnum.Rocket,
                CharacterEnum.Zhouzhou
            };
            battleInfo.EncounterId = EncounterId;
            battleInfo.InitialDrawCount = 0;

            AttachHud();
            Debug.Log("[TempoPrototype] 已启用：每次行动限出一张牌，印刷费用决定下次行动间隔。");
        }

        /// <summary>构建与存档卡组完全隔离的实验牌组；每张测试牌放两份，保证每次五选一有稳定选择。</summary>
        public static List<CardRuntimeState> CreatePrototypeDeck(IList<CharacterEnum> characters)
        {
            var result = new List<CardRuntimeState>();
            if (!IsActive || characters == null)
            {
                return result;
            }

            foreach (CharacterEnum character in characters)
            {
                if (!PrototypeDecks.TryGetValue(character, out string[] cardIds))
                {
                    continue;
                }

                foreach (string cardId in cardIds)
                {
                    result.Add(CardRuntimeState.CreateDefault(cardId));
                    result.Add(CardRuntimeState.CreateDefault(cardId));
                }
            }

            return result;
        }

        public static void BeginPlayerTurn(string unitId)
        {
            if (!IsActive || string.IsNullOrEmpty(unitId))
            {
                return;
            }

            PlayedUnits.Remove(unitId);
            ActionDelayByUnit.Remove(unitId);
        }

        public static bool CanPlayCard(string unitId)
        {
            return !IsActive || (!string.IsNullOrEmpty(unitId) && !PlayedUnits.Contains(unitId));
        }

        public static void RecordSuccessfulCard(string unitId, cfg.Character.CardInfo card)
        {
            if (!IsActive || string.IsNullOrEmpty(unitId) || !IsPrototypeCard(card))
            {
                return;
            }

            int delay = Mathf.Max(1, card.Energy);
            PlayedUnits.Add(unitId);
            ActionDelayByUnit[unitId] = delay;
            Debug.Log($"[TempoPrototype] {unitId} 打出 {card.Name}，下次行动间隔={delay}");
        }

        public static int GetActionDelayForEndTurn(string unitId, int fallbackSpeed)
        {
            if (!IsActive)
            {
                return Mathf.Max(1, fallbackSpeed);
            }

            return !string.IsNullOrEmpty(unitId) && ActionDelayByUnit.TryGetValue(unitId, out int delay)
                ? Mathf.Max(1, delay)
                : PassActionDelay;
        }

        public static bool HasPlayedCard(string unitId)
        {
            return IsActive && !string.IsNullOrEmpty(unitId) && PlayedUnits.Contains(unitId);
        }

        private static void EnsurePrototypeTablesInstalled()
        {
            ConfigLoader.Load();
            cfg.Tables tables = ConfigLoader.Tables;
            if (tables == null)
            {
                throw new InvalidOperationException("Tempo prototype cannot install because ConfigLoader.Tables is null.");
            }

            // 允许 ConfigLoader.Reload 后再次补装，因此用数据键判断，不只依赖静态 bool。
            if (!tables.TbCardInfo.DataMap.ContainsKey("TempoIrene001"))
            {
                var overlay = new cfg.Character.TbCardInfo(LoadJson(CardTablePath));
                foreach (var card in overlay.DataList)
                {
                    tables.TbCardInfo.DataMap.Add(card.Id, card);
                    tables.TbCardInfo.DataList.Add(card);
                }
                overlay.ResolveRef(tables);
            }

            if (!tables.TbEnemySkillInfo.DataMap.ContainsKey("TempoEnemySkill001"))
            {
                var overlay = new cfg.Enemy.TbEnemySkillInfo(LoadJson(EnemySkillTablePath));
                foreach (var skill in overlay.DataList)
                {
                    tables.TbEnemySkillInfo.DataMap.Add(skill.Id, skill);
                    tables.TbEnemySkillInfo.DataList.Add(skill);
                }
                overlay.ResolveRef(tables);
            }

            if (!tables.TbEnemyInfo.DataMap.ContainsKey("TempoEnemyStriker"))
            {
                var overlay = new cfg.Enemy.TbEnemyInfo(LoadJson(EnemyTablePath));
                foreach (var enemy in overlay.DataList)
                {
                    tables.TbEnemyInfo.DataMap.Add(enemy.Id, enemy);
                    tables.TbEnemyInfo.DataList.Add(enemy);
                }
                overlay.ResolveRef(tables);
            }

            if (!tables.TbEncounter.DataMap.ContainsKey(EncounterId))
            {
                var overlay = new cfg.Enemy.TbEncounter(LoadJson(EncounterTablePath));
                foreach (var encounter in overlay.DataList)
                {
                    tables.TbEncounter.DataMap.Add(encounter.Id, encounter);
                    tables.TbEncounter.DataList.Add(encounter);
                }
                overlay.ResolveRef(tables);
            }

            Debug.Log("[TempoPrototype] 独立卡表、敌人技能/CD表与遭遇表已挂载到运行时配置。正式 JSON 未被改写。");
        }

        private static JSONNode LoadJson(string resourcePath)
        {
            TextAsset asset = Resources.Load<TextAsset>(resourcePath);
            if (asset == null)
            {
                throw new InvalidOperationException($"Tempo prototype config not found: Resources/{resourcePath}.json");
            }

            return JSON.Parse(asset.text);
        }

        private static void AttachHud()
        {
            if (UnityEngine.Object.FindObjectOfType<TempoPrototypeHud>() != null)
            {
                return;
            }

            var go = new GameObject("Tempo Prototype Rules");
            go.AddComponent<TempoPrototypeHud>();
        }
    }

    /// <summary>不依赖场景预制体的轻量规则提示，便于一眼确认当前运行的是实验模式。</summary>
    public sealed class TempoPrototypeHud : MonoBehaviour
    {
        private GUIStyle _boxStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _bodyStyle;

        private void OnGUI()
        {
            if (!TempoPrototypeMode.IsActive)
            {
                return;
            }

            EnsureStyles();
            const float width = 410f;
            const float height = 112f;
            var rect = new Rect(16f, 16f, width, height);
            GUI.Box(rect, GUIContent.none, _boxStyle);
            GUI.Label(new Rect(30f, 26f, width - 28f, 28f), "一牌一行动 · 节奏原型", _titleStyle);
            GUI.Label(
                new Rect(30f, 55f, width - 28f, 66f),
                "每次角色行动只能打出 1 张牌，成功后自动结束。\n费用 = 下次行动间隔；不出牌则间隔 2。敌人技能数字 = 预告/CD。",
                _bodyStyle);
        }

        private void EnsureStyles()
        {
            if (_boxStyle != null)
            {
                return;
            }

            _boxStyle = new GUIStyle(GUI.skin.box);
            _boxStyle.normal.textColor = Color.white;

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.84f, 0.28f) }
            };
            _bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                wordWrap = true,
                normal = { textColor = Color.white }
            };
        }
    }
}
