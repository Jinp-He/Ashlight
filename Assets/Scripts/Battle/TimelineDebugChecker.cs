using UnityEngine;
using UnityEngine.UI;
using Scripts.UI.Timeline;

namespace Ashlight.Battle
{
    /// <summary>
    /// 时间轴调试检查器
    /// 在Unity编辑器中运行，检查时间轴相关组件是否正确配置
    /// </summary>
    public class TimelineDebugChecker : MonoBehaviour
    {
        [Header("检查设置")]
        [SerializeField]
        [Tooltip("按下按键后执行检查")]
        private KeyCode checkKey = KeyCode.F9;

        private void Update()
        {
            if (Input.GetKeyDown(checkKey))
            {
                CheckTimelineConfiguration();
            }
        }

        [ContextMenu("检查时间轴配置")]
        public void CheckTimelineConfiguration()
        {
            Debug.Log("========== 开始检查时间轴配置 ==========");

            // 1. 检查 Tag 是否存在
            CheckTag();

            // 2. 检查场景中的 TimelineSlotView
            CheckTimelineSlots();

            // 3. 检查 TimelineTrackView
            CheckTimelineTracks();

            // 4. 检查 EventSystem
            CheckEventSystem();

            Debug.Log("========== 时间轴配置检查完成 ==========");
        }

        private void CheckTag()
        {
            Debug.Log("<color=yellow>【1/4】检查 Tag 配置...</color>");

            try
            {
                GameObject[] timeSlots = GameObject.FindGameObjectsWithTag("TimeSlot");
                if (timeSlots == null || timeSlots.Length == 0)
                {
                    Debug.LogError("❌ 场景中未找到 Tag='TimeSlot' 的对象！");
                    Debug.LogWarning("   解决方法: 在 Unity 编辑器中，选择 Edit -> Project Settings -> Tags and Layers，添加 'TimeSlot' 标签");
                }
                else
                {
                    Debug.Log($"✅ 找到 {timeSlots.Length} 个 Tag='TimeSlot' 的对象");
                    foreach (var slot in timeSlots)
                    {
                        Debug.Log($"   - {slot.name} (路径: {GetGameObjectPath(slot)})");
                    }
                }
            }
            catch (UnityException e)
            {
                Debug.LogError($"❌ Tag 'TimeSlot' 未定义在项目中: {e.Message}");
                Debug.LogWarning("   解决方法: 在 Unity 编辑器中，选择 Edit -> Project Settings -> Tags and Layers，添加 'TimeSlot' 标签");
            }
        }

        private void CheckTimelineSlots()
        {
            Debug.Log("<color=yellow>【2/4】检查 TimelineSlotView 组件...</color>");

            TimelineSlotView[] slots = FindObjectsOfType<TimelineSlotView>(true); // 包括未激活的对象
            if (slots == null || slots.Length == 0)
            {
                Debug.LogError("❌ 场景中未找到 TimelineSlotView 组件！");
                return;
            }

            Debug.Log($"✅ 找到 {slots.Length} 个 TimelineSlotView 组件");

            int activeCount = 0;
            int inactiveCount = 0;

            foreach (var slot in slots)
            {
                // 检查激活状态
                bool isActive = slot.gameObject.activeSelf;
                bool isActiveInHierarchy = slot.gameObject.activeInHierarchy;
                
                if (!isActive)
                {
                    inactiveCount++;
                    Debug.LogError($"   ❌ {slot.name} 被 SetActive(false)！");
                    Debug.LogError($"      路径: {GetGameObjectPath(slot.gameObject)}");
                    
                    // 检查父级激活状态
                    Transform parent = slot.transform.parent;
                    while (parent != null)
                    {
                        Debug.Log($"      - 父级 {parent.name}: activeSelf={parent.gameObject.activeSelf}");
                        parent = parent.parent;
                    }
                }
                else if (!isActiveInHierarchy)
                {
                    inactiveCount++;
                    Debug.LogWarning($"   ⚠️ {slot.name} 的父级被禁用了！");
                    
                    // 检查父级激活状态
                    Transform parent = slot.transform.parent;
                    while (parent != null)
                    {
                        if (!parent.gameObject.activeSelf)
                        {
                            Debug.LogError($"      ❌ 父级 {parent.name} 被 SetActive(false)");
                        }
                        parent = parent.parent;
                    }
                }
                else
                {
                    activeCount++;
                }
                
                // 检查 Tag
                if (!slot.CompareTag("TimeSlot"))
                {
                    Debug.LogWarning($"   ⚠️ {slot.name} 的 Tag 不是 'TimeSlot' (当前: {slot.tag})");
                }

                // 检查射线检测组件
                bool hasRaycast = CheckRaycastComponent(slot.gameObject);
                if (!hasRaycast)
                {
                    Debug.LogWarning($"   ⚠️ {slot.name} 缺少射线检测组件 (Image/Graphic Raycaster)");
                }

                // 检查父引用
                var parentTrack = slot.GetParentTrack();
                if (parentTrack == null)
                {
                    Debug.LogWarning($"   ⚠️ {slot.name} 未初始化父引用 (可能是因为场景刚加载)");
                }
                else if (isActive && isActiveInHierarchy)
                {
                    Debug.Log($"   ✅ {slot.name} (索引: {slot.SlotIndex}, 占用: {slot.IsOccupied})");
                }
            }
            
            Debug.Log($"<color=cyan>激活状态统计: 激活={activeCount}, 未激活={inactiveCount}</color>");
        }

        private void CheckTimelineTracks()
        {
            Debug.Log("<color=yellow>【3/4】检查 TimelineTrackView 组件...</color>");

            TimelineTrackView[] tracks = FindObjectsOfType<TimelineTrackView>();
            if (tracks == null || tracks.Length == 0)
            {
                Debug.LogError("❌ 场景中未找到 TimelineTrackView 组件！");
                return;
            }

            Debug.Log($"✅ 找到 {tracks.Length} 个 TimelineTrackView 组件");

            foreach (var track in tracks)
            {
                var trackData = track.GetTrack();
                if (trackData == null)
                {
                    Debug.LogWarning($"   ⚠️ {track.name} 的 Track 数据为 null (可能未初始化)");
                }
                else
                {
                    Debug.Log($"   ✅ {track.name} (Owner: {track.GetOwnerId()})");
                }
            }
        }

        private void CheckEventSystem()
        {
            Debug.Log("<color=yellow>【4/4】检查 EventSystem...</color>");

            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            if (eventSystem == null)
            {
                Debug.LogError("❌ 场景中未找到 EventSystem！");
                Debug.LogWarning("   解决方法: 在 Hierarchy 右键 -> UI -> Event System");
                return;
            }

            Debug.Log($"✅ EventSystem 存在: {eventSystem.name}");

            // 检查 Canvas 的 Graphic Raycaster
            Canvas[] canvases = FindObjectsOfType<Canvas>();
            int raycasterCount = 0;
            foreach (var canvas in canvases)
            {
                if (canvas.GetComponent<GraphicRaycaster>() != null)
                {
                    raycasterCount++;
                    Debug.Log($"   ✅ Canvas '{canvas.name}' 有 GraphicRaycaster");
                }
            }

            if (raycasterCount == 0)
            {
                Debug.LogWarning("   ⚠️ 场景中没有任何 Canvas 带有 GraphicRaycaster 组件");
            }
        }

        private bool CheckRaycastComponent(GameObject obj)
        {
            // 检查是否有 Image 或其他 Graphic 组件
            var graphic = obj.GetComponent<Graphic>();
            if (graphic != null)
            {
                // 检查是否启用了 Raycast Target
                if (graphic.raycastTarget)
                {
                    return true;
                }
            }

            return false;
        }

        private string GetGameObjectPath(GameObject obj)
        {
            string path = obj.name;
            Transform parent = obj.transform.parent;

            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return path;
        }
    }
}

