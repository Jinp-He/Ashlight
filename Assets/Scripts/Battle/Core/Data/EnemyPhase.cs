namespace Ashlight.Battle.Core.Data
{
    /// <summary>
    /// 敌人行动阶段枚举
    /// None: ATB推进中，未到达行动点
    /// IntentAxis: 意图轴阶段，显示意图，可被打断/延后/硬直
    /// ExecuteAxis: 执行轴阶段，不可打断，仅可延后/硬直
    /// </summary>
    public enum EnemyPhase
    {
        None,
        IntentAxis,
        ExecuteAxis
    }
}
