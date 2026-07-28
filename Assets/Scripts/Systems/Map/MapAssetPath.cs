namespace Ashlight.Systems.Map
{
    /// <summary>
    /// MapPanel 使用的 Resources 相对路径（不含扩展名）。集中维护，避免 UI 散落字符串。
    /// </summary>
    public static class MapAssetPath
    {
        public const string AncientRuins = "Map/Img_Guguo";
        public const string TileFrame = "Map/UI_Dikuai";

        public const string OneWayTile = "Map/Tiles/Img_End";
        public const string StraightTile = "Map/Tiles/Img_Straight";
        public const string CornerTile = "Map/Tiles/Img_Turn";
        public const string ThreeWayTile = "Map/Tiles/Img_T-Shaped";
        public const string FourWayTile = "Map/Tiles/Img_Cross";

        public const string BattleIcon = "Map/MapIcon/Img_Xiaoguai";
        public const string EliteIcon = "Map/MapIcon/Img_Jingying";
        public const string EventIcon = "Map/MapIcon/Img_Shijian";
        public const string ShopIcon = "Map/MapIcon/Img_Shop";
        public const string RestIcon = "Map/MapIcon/Img_Xiuxi";
        public const string BossIcon = "Map/MapIcon/Img_Boss";

        public const string Canterbury = "Map/lv2/Img_Kantebolei";
        public const string AncientRuinsLocation = "Map/lv1/Img_lishibeiwen";
        public const string MageTower = "Map/lv3/Img_Fashita";
        public const string AshMistDepths = "Map/lv3/Img_Huiwu";
        public const string SirenTown = "Map/lv3/Img_Sairen";

        public static string GetTileShapePath(Ashlight.State.Runtime.MapTileShape shape)
        {
            switch (shape)
            {
                case Ashlight.State.Runtime.MapTileShape.End: return OneWayTile;
                case Ashlight.State.Runtime.MapTileShape.Cross: return FourWayTile;
                case Ashlight.State.Runtime.MapTileShape.TShaped: return ThreeWayTile;
                case Ashlight.State.Runtime.MapTileShape.Straight: return StraightTile;
                default: return CornerTile;
            }
        }

        public static string GetContentIconPath(Ashlight.State.Runtime.MapTileContent content)
        {
            switch (content)
            {
                case Ashlight.State.Runtime.MapTileContent.Battle: return BattleIcon;
                case Ashlight.State.Runtime.MapTileContent.Elite: return EliteIcon;
                case Ashlight.State.Runtime.MapTileContent.Event: return EventIcon;
                case Ashlight.State.Runtime.MapTileContent.Shop: return ShopIcon;
                case Ashlight.State.Runtime.MapTileContent.Rest: return RestIcon;
                default: return null;
            }
        }
    }
}
