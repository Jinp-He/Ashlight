using UnityEngine;
using UnityEngine.UI;

// 单个「插入演出特效」实例。
// 对应网页版 activeEffects 数组中的一个元素：每次打出卡牌都会新建一个实例，
// 旧实例自然播完才销毁，新实例叠在旧实例之上（绘制顺序 = 列表顺序）。
public class CutsceneEffect : MonoBehaviour
{
    [HideInInspector] public RectTransform root;   // 承载 Image 的 RectTransform（用于敌方水平翻转）
    [HideInInspector] public Image image;          // 当前帧序列帧
    [HideInInspector] public float age;            // 自触发后经过的时间（秒），由管理器每帧累加
    [HideInInspector] public CutsceneDirection direction;

    // 是否仍在存活窗口内：age < 序列时长 + 生成延迟
    public bool IsAlive(float spawnDelay, float duration)
    {
        return age < duration + spawnDelay;
    }

    public void Reset(CutsceneDirection dir)
    {
        age = 0f;
        direction = dir;
        gameObject.SetActive(true);
    }
}
