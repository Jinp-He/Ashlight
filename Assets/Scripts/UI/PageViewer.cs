using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Ashlight.UI
{
    /// <summary>
    /// 页面查看器 - 管理内容分页显示和页面指示器
    /// </summary>
    public class PageViewer : MonoBehaviour
    {
        [Header("内容设置")]
        [Tooltip("内容容器，包含所有需要分页的元素")]
        public Transform contentView;
        
        [Tooltip("每页显示的元素数量")]
        public int amountPerPage = 6;
        
        [Header("页面指示器设置")]
        [Tooltip("页面指示器预制体")]
        public GameObject pageIndicatorPrefab;
        
        [Tooltip("页面指示器的父容器")]
        public Transform indicatorContainer;
        
        [Header("翻页按钮")]
        [Tooltip("下一页按钮")]
        public Button nextBtn;
        
        [Tooltip("上一页按钮")]
        public Button prevBtn;
        
        // 私有变量
        private int currentPage = 0;  // 当前页码（从0开始）
        private int totalPages = 0;   // 总页数
        private List<GameObject> pageIndicators = new List<GameObject>();  // 页面指示器列表
        private List<Transform> contentItems = new List<Transform>();  // 内容项列表
        
        /// <summary>
        /// 初始化
        /// </summary>
        private void Start()
        {
            Initialize();
        }
        
        /// <summary>
        /// 初始化页面查看器
        /// </summary>
        private void Initialize()
        {
            // 1. 收集 ContentView 中的所有子元素
            CollectContentItems();
            
            // 2. 计算总页数
            CalculateTotalPages();
            
            // 3. 生成页面指示器
            GeneratePageIndicators();
            
            // 4. 绑定按钮事件
            BindButtonEvents();
            
            // 5. 显示第一页
            ShowPage(0);
        }

        /// <summary>
        /// 刷新页面查看器（当内容发生变化时调用）
        /// </summary>
        public void Refresh()
        {
            // 1. 重新收集内容项
            CollectContentItems();
            
            // 2. 重新计算总页数
            CalculateTotalPages();
            
            // 3. 重新生成页面指示器
            GeneratePageIndicators();
            
            // 4. 如果当前页超出范围，跳转到最后一页或第一页
            if (currentPage >= totalPages)
            {
                currentPage = Mathf.Max(0, totalPages - 1);
            }
            
            // 5. 显示当前页
            ShowPage(currentPage);
            
            Debug.Log($"PageViewer: 已刷新，当前第 {currentPage + 1}/{totalPages} 页");
        }
        
        /// <summary>
        /// 收集 ContentView 中的所有子元素
        /// </summary>
        private void CollectContentItems()
        {
            contentItems.Clear();
            
            if (contentView == null)
            {
                Debug.LogError("PageViewer: ContentView 未设置!");
                return;
            }
            
            // 遍历所有子对象
            for (int i = 0; i < contentView.childCount; i++)
            {
                Transform child = contentView.GetChild(i);
                
                // 检查子对象是否为null（已被销毁）
                if (child == null || child.gameObject == null)
                {
                    continue;
                }
                
                // 只添加激活的子对象
                if (child.gameObject.activeSelf)
                {
                    contentItems.Add(child);
                }
            }
            
            Debug.Log($"PageViewer: 收集到 {contentItems.Count} 个内容项");
        }
        
        /// <summary>
        /// 计算总页数
        /// </summary>
        private void CalculateTotalPages()
        {
            if (contentItems.Count == 0)
            {
                totalPages = 0;
                return;
            }
            
            // 向上取整，确保所有元素都能显示
            totalPages = Mathf.CeilToInt((float)contentItems.Count / amountPerPage);
            Debug.Log($"PageViewer: 总页数 = {totalPages}");
        }
        
        /// <summary>
        /// 生成页面指示器
        /// </summary>
        private void GeneratePageIndicators()
        {
            // 清空旧的指示器
            ClearPageIndicators();
            
            if (pageIndicatorPrefab == null)
            {
                Debug.LogWarning("PageViewer: PageIndicatorPrefab 未设置!");
                return;
            }
            
            if (indicatorContainer == null)
            {
                Debug.LogWarning("PageViewer: IndicatorContainer 未设置!");
                return;
            }
            
            // 根据总页数生成指示器
            for (int i = 0; i < totalPages; i++)
            {
                GameObject indicator = Instantiate(pageIndicatorPrefab, indicatorContainer);
                pageIndicators.Add(indicator);
                
                // 为指示器添加点击事件，点击后跳转到对应页面
                int pageIndex = i; // 捕获当前索引
                Button indicatorBtn = indicator.GetComponent<Button>();
                if (indicatorBtn != null)
                {
                    indicatorBtn.onClick.AddListener(() => ShowPage(pageIndex));
                }
            }
            
            Debug.Log($"PageViewer: 生成了 {pageIndicators.Count} 个页面指示器");
        }
        
        /// <summary>
        /// 清空页面指示器
        /// </summary>
        private void ClearPageIndicators()
        {
            foreach (var indicator in pageIndicators)
            {
                if (indicator != null)
                {
                    Destroy(indicator);
                }
            }
            pageIndicators.Clear();
        }
        
        /// <summary>
        /// 绑定按钮事件
        /// </summary>
        private void BindButtonEvents()
        {
            if (nextBtn != null)
            {
                nextBtn.onClick.RemoveAllListeners();
                nextBtn.onClick.AddListener(NextPage);
            }
            else
            {
                Debug.LogWarning("PageViewer: NextBtn 未设置!");
            }
            
            if (prevBtn != null)
            {
                prevBtn.onClick.RemoveAllListeners();
                prevBtn.onClick.AddListener(PrevPage);
            }
            else
            {
                Debug.LogWarning("PageViewer: PrevBtn 未设置!");
            }
        }
        
        /// <summary>
        /// 显示指定页面
        /// </summary>
        /// <param name="pageIndex">页面索引（从0开始）</param>
        private void ShowPage(int pageIndex)
        {
            // ✅ 首先清理无效的引用（已被销毁的对象）
            CleanupInvalidReferences();
            
            // 重新计算总页数（因为内容项可能已变化）
            CalculateTotalPages();
            
            // 检查页面索引是否有效
            if (pageIndex < 0 || pageIndex >= totalPages)
            {
                Debug.LogWarning($"PageViewer: 无效的页面索引 {pageIndex}，总页数为 {totalPages}");
                // 如果页面索引无效，尝试显示第一页
                if (totalPages > 0)
                {
                    pageIndex = 0;
                }
                else
                {
                    return;
                }
            }
            
            currentPage = pageIndex;
            
            // 计算当前页的起始和结束索引
            int startIndex = currentPage * amountPerPage;
            int endIndex = Mathf.Min(startIndex + amountPerPage, contentItems.Count);
            
            // 隐藏所有内容项
            for (int i = 0; i < contentItems.Count; i++)
            {
                // 检查对象是否已被销毁
                if (contentItems[i] != null && contentItems[i].gameObject != null)
                {
                    contentItems[i].gameObject.SetActive(false);
                }
            }
            
            // 显示当前页的内容项
            for (int i = startIndex; i < endIndex; i++)
            {
                // 检查对象是否已被销毁
                if (contentItems[i] != null && contentItems[i].gameObject != null)
                {
                    contentItems[i].gameObject.SetActive(true);
                }
            }
            
            // 更新按钮状态
            UpdateButtonStates();
            
            // 更新指示器状态
            UpdateIndicatorStates();
            
            Debug.Log($"PageViewer: 显示第 {currentPage + 1}/{totalPages} 页，显示元素 {startIndex}-{endIndex - 1}");
        }
        
        /// <summary>
        /// 清理contentItems列表中的无效引用（已被销毁的对象）
        /// </summary>
        private void CleanupInvalidReferences()
        {
            // 移除所有已被销毁的对象引用
            contentItems.RemoveAll(item => item == null || item.gameObject == null);
        }
        
        /// <summary>
        /// 下一页
        /// </summary>
        private void NextPage()
        {
            if (currentPage < totalPages - 1)
            {
                ShowPage(currentPage + 1);
            }
        }
        
        /// <summary>
        /// 上一页
        /// </summary>
        private void PrevPage()
        {
            if (currentPage > 0)
            {
                ShowPage(currentPage - 1);
            }
        }
        
        /// <summary>
        /// 更新翻页按钮的可交互状态
        /// </summary>
        private void UpdateButtonStates()
        {
            if (prevBtn != null)
            {
                // 第一页时禁用上一页按钮
                prevBtn.interactable = currentPage > 0;
            }
            
            if (nextBtn != null)
            {
                // 最后一页时禁用下一页按钮
                nextBtn.interactable = currentPage < totalPages - 1;
            }
        }
        
        /// <summary>
        /// 更新页面指示器的状态（高亮当前页）
        /// </summary>
        private void UpdateIndicatorStates()
        {
            for (int i = 0; i < pageIndicators.Count; i++)
            {
                if (pageIndicators[i] != null)
                {
                    // 可以通过修改指示器的颜色或缩放来标识当前页
                    // 这里使用简单的缩放效果
                    Transform indicatorTransform = pageIndicators[i].transform;
                    indicatorTransform.localScale = (i == currentPage) ? Vector3.one * 1.2f : Vector3.one;
                    
                    // 如果指示器有 Image 组件，也可以修改颜色
                    Image indicatorImage = pageIndicators[i].GetComponent<Image>();
                    if (indicatorImage != null)
                    {
                        indicatorImage.color = (i == currentPage) ? Color.white : Color.black;
                    }
                }
            }
        }
    }
}

