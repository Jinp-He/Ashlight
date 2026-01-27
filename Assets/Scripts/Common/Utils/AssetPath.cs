using UnityEngine;
using System.IO;

namespace Ashlight.Common.Utils
{
    /// <summary>
    /// 资源路径管理静态类
    /// 统一管理项目中的公共路径常量，避免硬编码路径分散在各处
    /// </summary>
    public static class AssetPath
    {
        #region 项目基础路径

        /// <summary>
        /// Assets文件夹路径
        /// </summary>
        public static string AssetsPath => Application.dataPath;

        /// <summary>
        /// 项目根目录路径（Assets的父目录）
        /// </summary>
        public static string ProjectRootPath => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        #endregion

        #region 配置数据路径

        /// <summary>
        /// GeneratedDatas目录路径（项目根目录下）
        /// </summary>
        public static string GeneratedDatasPath => Path.Combine(ProjectRootPath, "GeneratedDatas");

        /// <summary>
        /// JSON配置文件目录路径
        /// </summary>
        public static string JsonConfigPath => Path.Combine(GeneratedDatasPath, "json");

        public static string CardViewControllerPath => Path.Combine("Cards","Prefabs", "CardViewController");

        public static string CharactersAssetPathPrefix = "Characters";
        public static string EnemyAssetPathPrefix = "Enemy";

        #region Character资源路径

        /// <summary>
        /// 获取角色图标资源路径
        /// </summary>
        /// <param name="characterId">角色ID</param>
        /// <returns>资源路径（相对于Resources文件夹）</returns>
        public static string GetCharacterIconAssetPath(string characterId)
        {
            return Path.Combine(CharactersAssetPathPrefix, characterId, "Icon", $"Icon_{characterId}");
        }

        /// <summary>
        /// 获取角色Sprite资源路径
        /// </summary>
        /// <param name="characterId">角色ID</param>
        /// <returns>资源路径（相对于Resources文件夹）</returns>
        public static string GetCharacterSpritesAssetPath(string characterId)
        {
            return Path.Combine(CharactersAssetPathPrefix, characterId, "Sprites", characterId);
        }

        /// <summary>
        /// 获取角色Skeleton资源路径
        /// </summary>
        /// <param name="characterId">角色ID</param>
        /// <returns>资源路径（相对于Resources文件夹）</returns>
        public static string GetCharacterSkeletonAssetPath(string characterId)
        {
            return Path.Combine(CharactersAssetPathPrefix, characterId, "Skeleton", "skeleton_SkeletonData");
        }

        // 向后兼容的方法别名
        public static string GetIconAssetPath(string characterId) => GetCharacterIconAssetPath(characterId);
        public static string GetSpritesAssetPath(string characterId) => GetCharacterSpritesAssetPath(characterId);
        public static string GetSkeletonAssetPath(string characterId) => GetCharacterSkeletonAssetPath(characterId);

        #endregion

        #region Enemy资源路径

        /// <summary>
        /// 获取敌人图标资源路径
        /// </summary>
        /// <param name="enemyId">敌人ID</param>
        /// <returns>资源路径（相对于Resources文件夹）</returns>
        public static string GetEnemyIconAssetPath(string enemyId)
        {
            return Path.Combine(EnemyAssetPathPrefix, enemyId, "Icon", $"Icon_{enemyId}");
        }

        /// <summary>
        /// 获取敌人Sprite资源路径
        /// </summary>
        /// <param name="enemyId">敌人ID</param>
        /// <returns>资源路径（相对于Resources文件夹）</returns>
        public static string GetEnemySpritesAssetPath(string enemyId)
        {
            return Path.Combine(EnemyAssetPathPrefix, enemyId, "Sprites", enemyId);
        }

        /// <summary>
        /// 获取敌人Skeleton资源路径
        /// </summary>
        /// <param name="enemyId">敌人ID</param>
        /// <returns>资源路径（相对于Resources文件夹）</returns>
        public static string GetEnemySkeletonAssetPath(string enemyId)
        {
            return Path.Combine(EnemyAssetPathPrefix, enemyId, "Skeleton", "skeleton_SkeletonData");
        }

        #endregion

        /// <summary>
        /// 获取JSON配置文件完整路径
        /// </summary>
        /// <param name="fileName">文件名（不含扩展名）</param>
        /// <returns>完整的文件路径</returns>
        public static string GetJsonConfigFilePath(string fileName)
        {
            return Path.GetFullPath(Path.Combine(JsonConfigPath, $"{fileName}.json"));
        }

        #endregion

        #region 卡牌资源路径

        /// <summary>
        /// 卡牌资源根路径前缀
        /// </summary>
        public const string CardsAssetPathPrefix = "Cards";

        /// <summary>
        /// 获取卡牌图标资源路径
        /// </summary>
        /// <param name="cardId">卡牌ID</param>
        /// <returns>Resources路径（不含扩展名）</returns>
        public static string GetCardIconAssetPath(string cardId)
        {
            return Path.Combine("Sprites", "Cards", "Icons", $"{cardId}_Icon");
        }

        /// <summary>
        /// 获取卡牌全图资源路径
        /// </summary>
        /// <param name="cardId">卡牌ID</param>
        /// <returns>Resources路径（不含扩展名）</returns>
        public static string GetCardFullImageAssetPath(string cardId)
        {
            return Path.Combine("Sprites", "Cards", "FullImages", cardId);
        }

        /// <summary>
        /// 获取卡牌 Sprite 资源路径
        /// </summary>
        /// <param name="cardId">卡牌ID</param>
        /// <returns>Resources路径（相对于Resources文件夹，不含扩展名）</returns>
        public static string GetCardSpriteAssetPath(string cardId)
        {
            return Path.Combine(CardsAssetPathPrefix, "Sprites", cardId);
        }

        /// <summary>
        /// 获取卡牌 MiniSprite 资源路径
        /// </summary>
        /// <param name="cardId">卡牌ID</param>
        /// <returns>Resources路径（相对于Resources文件夹，不含扩展名）</returns>
        public static string GetCardMiniSpriteAssetPath(string cardId)
        {
            return Path.Combine(CardsAssetPathPrefix, "MiniSprites", cardId);
        }

        #endregion

        #region Resources路径

        /// <summary>
        /// Resources文件夹路径（相对于Assets）
        /// </summary>
        public const string ResourcesRelativePath = "Resources";

        /// <summary>
        /// 获取Resources文件夹中的资源路径（用于Resources.Load）
        /// </summary>
        /// <param name="relativePath">相对于Resources文件夹的路径（不含扩展名）</param>
        /// <returns>Resources加载路径</returns>
        public static string GetResourcesPath(string relativePath)
        {
            return relativePath.Replace('\\', '/');
        }

        #endregion
    }
}

