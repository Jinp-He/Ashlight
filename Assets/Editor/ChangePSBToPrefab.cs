using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;
using _Scripts.UI;
using System.Collections.Generic;

/// <summary>
/// PSB转Prefab编辑器工具
/// 功能：
/// 1. 扫描选中GameObject的所有子物体
/// 2. 根据命名中的"/"重组层级结构
/// 3. 转换所有SpriteRenderer为Image组件
/// 4. 为"Txt"开头的物体添加TextMeshPro子物体（名称与父物体相同）
/// 5. 为根物体添加UIBindParent组件
/// </summary>
public class ChangePSBToPrefab
{
    [MenuItem("Tools/ChangePSBToPrefab")]
    public static void ProcessSelectedGameObject()
    {
        // 获取当前选中的GameObject
        GameObject selectedObject = Selection.activeGameObject;
        
        if (selectedObject == null)
        {
            EditorUtility.DisplayDialog("错误", "请先选中一个GameObject！", "确定");
            return;
        }
        
        Debug.Log($"[ChangePSBToPrefab] 开始处理: {selectedObject.name}");
        
        // 记录Undo，允许撤销操作
        Undo.RegisterFullObjectHierarchyUndo(selectedObject, "ChangePSBToPrefab");
        
        // 步骤1 & 2: 收集所有子物体并重组层级结构
        ReorganizeHierarchy(selectedObject);
        
        // 步骤2.5: 颠倒每个层级的子物体顺序
        ReverseHierarchyOrder(selectedObject);
        
        // 步骤3: 处理所有子物体，添加UIBind和TextMeshPro
        ProcessAllChildren(selectedObject);
        
        // 步骤4: 为父物体添加UIBindParent
        AddUIBindParentToRoot(selectedObject);
        
        // 标记场景为已修改
        EditorUtility.SetDirty(selectedObject);
        
        Debug.Log($"[ChangePSBToPrefab] 处理完成！");
        EditorUtility.DisplayDialog("成功", $"已完成处理: {selectedObject.name}", "确定");
    }
    
    /// <summary>
    /// 重组层级结构：根据名称中的"/"创建父子关系
    /// </summary>
    private static void ReorganizeHierarchy(GameObject root)
    {
        Debug.Log("[ChangePSBToPrefab] 步骤2: 开始重组层级结构...");
        
        // 收集所有需要处理的子物体（避免在遍历时修改集合）
        List<Transform> allChildren = new List<Transform>();
        CollectAllChildren(root.transform, allChildren);
        
        Debug.Log($"[ChangePSBToPrefab] 找到 {allChildren.Count} 个子物体");
        
        // 用于缓存已创建的路径节点
        Dictionary<string, Transform> pathCache = new Dictionary<string, Transform>();
        
        // 处理每个子物体
        foreach (Transform child in allChildren)
        {
            string childName = child.name;
            
            // 如果名称中包含"/"，需要重组
            if (childName.Contains("/"))
            {
                Debug.Log($"[ChangePSBToPrefab] 处理路径: {childName}");
                
                // 分割路径
                string[] pathParts = childName.Split('/');
                
                // 获取或创建父级层级
                Transform currentParent = root.transform;
                string currentPath = "";
                
                // 创建中间层级（除了最后一个部分）
                for (int i = 0; i < pathParts.Length - 1; i++)
                {
                    string partName = pathParts[i];
                    currentPath += (i > 0 ? "/" : "") + partName;
                    
                    // 检查是否已经创建过这个路径
                    if (!pathCache.ContainsKey(currentPath))
                    {
                        // 在当前父级下查找是否已存在同名物体
                        Transform existingChild = currentParent.Find(partName);
                        
                        if (existingChild == null)
                        {
                            // 创建新的中间节点
                            GameObject newParent = new GameObject(partName);
                            newParent.transform.SetParent(currentParent, false);
                            
                            // 如果父级有RectTransform，也为这个中间节点添加RectTransform
                            if (currentParent.GetComponent<RectTransform>() != null)
                            {
                                RectTransform newRect = newParent.AddComponent<RectTransform>();
                                // 设置为填充父物体
                                newRect.anchorMin = Vector2.zero;
                                newRect.anchorMax = Vector2.one;
                                newRect.sizeDelta = Vector2.zero;
                                newRect.anchoredPosition = Vector2.zero;
                            }
                            
                            pathCache[currentPath] = newParent.transform;
                            currentParent = newParent.transform;
                            Debug.Log($"[ChangePSBToPrefab] 创建中间节点: {currentPath}");
                        }
                        else
                        {
                            pathCache[currentPath] = existingChild;
                            currentParent = existingChild;
                        }
                    }
                    else
                    {
                        currentParent = pathCache[currentPath];
                    }
                }
                
                // 重命名最后一级并移动到正确的父级下
                string finalName = pathParts[pathParts.Length - 1];
                child.name = finalName;
                child.SetParent(currentParent, false);
                
                Debug.Log($"[ChangePSBToPrefab] 将 '{childName}' 重命名为 '{finalName}' 并移动到 '{currentParent.name}' 下");
            }
        }
        
        Debug.Log("[ChangePSBToPrefab] 层级重组完成");
    }
    
    /// <summary>
    /// 层序遍历收集所有子物体（BFS顺序，确保父物体先于子物体被处理）
    /// </summary>
    private static void CollectAllChildren(Transform parent, List<Transform> result)
    {
        Queue<Transform> queue = new Queue<Transform>();
        
        // 将第一层子物体加入队列
        foreach (Transform child in parent)
        {
            queue.Enqueue(child);
        }
        
        // BFS遍历
        while (queue.Count > 0)
        {
            Transform current = queue.Dequeue();
            result.Add(current);
            
            // 将当前节点的子物体加入队列
            foreach (Transform child in current)
            {
                queue.Enqueue(child);
            }
        }
    }
    
    /// <summary>
    /// 递归颠倒每个层级的子物体顺序
    /// </summary>
    private static void ReverseHierarchyOrder(GameObject root)
    {
        Debug.Log("[ChangePSBToPrefab] 步骤2.5: 开始颠倒层级顺序...");
        
        // 从根节点开始递归颠倒
        ReverseChildrenOrder(root.transform);
        
        Debug.Log("[ChangePSBToPrefab] 层级顺序颠倒完成");
    }
    
    /// <summary>
    /// 递归颠倒指定Transform的所有子物体顺序
    /// </summary>
    private static void ReverseChildrenOrder(Transform parent)
    {
        // 获取当前层级的所有直接子物体
        int childCount = parent.childCount;
        
        if (childCount <= 1)
        {
            // 如果只有0个或1个子物体，无需颠倒
            return;
        }
        
        // 先递归处理所有子物体的子层级
        for (int i = 0; i < childCount; i++)
        {
            Transform child = parent.GetChild(i);
            ReverseChildrenOrder(child);
        }
        
        // 然后颠倒当前层级的子物体顺序
        // 将子物体从后往前重新排序
        for (int i = 0; i < childCount; i++)
        {
            // 将最后一个子物体移到第i个位置
            parent.GetChild(childCount - 1).SetSiblingIndex(i);
        }
        
        Debug.Log($"[ChangePSBToPrefab] 颠倒了 '{parent.name}' 的 {childCount} 个子物体顺序");
    }
    
    /// <summary>
    /// 处理所有子物体：转换所有SpriteRenderer为Image，为Txt添加TextMeshPro子物体
    /// </summary>
    private static void ProcessAllChildren(GameObject root)
    {
        Debug.Log("[ChangePSBToPrefab] 步骤3: 开始处理子物体...");
        
        // 再次收集所有子物体（因为层级已经改变）
        List<Transform> allChildren = new List<Transform>();
        CollectAllChildren(root.transform, allChildren);
        
        int spriteRendererCount = 0;
        int txtCount = 0;
        
        foreach (Transform child in allChildren)
        {
            GameObject childObj = child.gameObject;
            string childName = child.name;
            
            // 第一步：先检查并转换所有的SpriteRenderer为Image（不管名字是什么）
            SpriteRenderer spriteRenderer = childObj.GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                ConvertSpriteRendererToImage(childObj, spriteRenderer);
                spriteRendererCount++;
            }
            
            // 第二步：根据名字添加相应的组件
            // 处理Txt开头的物体 - 添加TextMeshPro子物体
            if (childName.StartsWith("Txt"))
            {
                AddTextMeshProChild(childObj);
                txtCount++;
                Debug.Log($"[ChangePSBToPrefab] 为 '{childName}' 添加TextMeshPro子物体");
            }
        }
        
        Debug.Log($"[ChangePSBToPrefab] 处理完成: 转换了 {spriteRendererCount} 个SpriteRenderer, {txtCount} 个Txt添加TextMeshPro子物体");
    }
    
    /// <summary>
    /// 为GameObject添加UIBind组件，并将SpriteRenderer转换为Image
    /// </summary>
    private static void AddUIBindComponent(GameObject obj)
    {
        // 检查是否有SpriteRenderer组件，如果有则转换为Image
        SpriteRenderer spriteRenderer = obj.GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            ConvertSpriteRendererToImage(obj, spriteRenderer);
        }
        
        // 检查是否已存在UIBind组件
        UIBind uiBind = obj.GetComponent<UIBind>();
        if (uiBind == null)
        {
            uiBind = obj.AddComponent<UIBind>();
        }
        
        // 自动设置BindName为GameObject名称
        uiBind.BindName = obj.name;
    }
    
    /// <summary>
    /// 将SpriteRenderer转换为Image组件
    /// 保持原有位置不变，只替换组件
    /// </summary>
    private static void ConvertSpriteRendererToImage(GameObject obj, SpriteRenderer spriteRenderer)
    {
        Transform transform = obj.transform;
        
        // 保存原始的Transform信息（只保存本地坐标）
        Vector3 originalLocalPosition = transform.localPosition;
        Vector3 originalLocalScale = transform.localScale;
        Quaternion originalLocalRotation = transform.localRotation;
        
        // 获取Sprite信息
        Sprite sprite = spriteRenderer.sprite;
        Color color = spriteRenderer.color;
        float ppu = (sprite != null) ? sprite.pixelsPerUnit : 100f;
        
        // 计算sprite的尺寸
        Vector2 spriteSize = Vector2.zero;
        if (sprite != null)
        {
            spriteSize = sprite.rect.size;
        }
        
        // 检查父物体是否已经有RectTransform（是否已经在UI坐标系中）
        bool parentHasRectTransform = transform.parent != null && 
                                       transform.parent.GetComponent<RectTransform>() != null;
        
        // 移除 Sprite Renderer 组件
        Object.DestroyImmediate(spriteRenderer);
        
        // 添加 UI Image 组件（Unity会自动添加RectTransform）
        Image image = obj.AddComponent<Image>();
        
        // 设置Sprite和颜色
        image.sprite = sprite;
        image.color = color;
        image.type = Image.Type.Simple;
        image.raycastTarget = true;
        
        // 获取RectTransform
        RectTransform rectTransform = obj.GetComponent<RectTransform>();
        
        // 设置锚点和轴心为中心
        rectTransform.anchorMin = Vector2.one * 0.5f;
        rectTransform.anchorMax = Vector2.one * 0.5f;
        rectTransform.pivot = Vector2.one * 0.5f;
        
        // 设置位置
        if (parentHasRectTransform)
        {
            // 父物体已经是UI，直接使用本地位置（已经是像素坐标）
            rectTransform.anchoredPosition = new Vector2(originalLocalPosition.x, originalLocalPosition.y);
            Debug.Log($"[ChangePSBToPrefab] 转换 '{obj.name}': 父物体是UI，保持LocalPos({originalLocalPosition.x:F2}, {originalLocalPosition.y:F2})");
        }
        else
        {
            // 父物体不是UI，需要乘以PPU转换坐标
            rectTransform.anchoredPosition = new Vector2(
                originalLocalPosition.x * ppu, 
                originalLocalPosition.y * ppu
            );
            Debug.Log($"[ChangePSBToPrefab] 转换 '{obj.name}': 父物体非UI，LocalPos({originalLocalPosition.x:F2}, {originalLocalPosition.y:F2}) * {ppu} -> AnchoredPos({rectTransform.anchoredPosition.x:F2}, {rectTransform.anchoredPosition.y:F2})");
        }
        
        // 恢复旋转和缩放
        rectTransform.localRotation = originalLocalRotation;
        rectTransform.localScale = originalLocalScale;
        
        // 设置尺寸为sprite的像素尺寸
        if (sprite != null)
        {
            rectTransform.sizeDelta = spriteSize;
        }
    }
    
    /// <summary>
    /// 为Txt开头的物体添加TextMeshPro子物体（子物体名称与父物体相同）
    /// </summary>
    private static void AddTextMeshProChild(GameObject txtObject)
    {
        // 检查是否有SpriteRenderer组件，如果有则转换为Image（作为背景）
        SpriteRenderer spriteRenderer = txtObject.GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            ConvertSpriteRendererToImage(txtObject, spriteRenderer);
        }
        
        // 确保有RectTransform
        RectTransform rectTransform = txtObject.GetComponent<RectTransform>();
        if (rectTransform == null)
        {
            rectTransform = txtObject.AddComponent<RectTransform>();
        }
        
        // 检查是否已经存在TextMeshPro子物体
        TextMeshProUGUI existingTMP = txtObject.GetComponentInChildren<TextMeshProUGUI>();
        if (existingTMP != null)
        {
            Debug.Log($"[ChangePSBToPrefab] '{txtObject.name}' 已存在TextMeshPro组件，跳过创建");
            return;
        }
        
        // 创建TextMeshPro子物体，使用与parent相同的名称
        GameObject tmpChild = new GameObject(txtObject.name);
        tmpChild.transform.SetParent(txtObject.transform, false);
        
        // 添加RectTransform
        RectTransform tmpRectTransform = tmpChild.AddComponent<RectTransform>();
        
        // 添加TextMeshProUGUI组件
        TextMeshProUGUI tmpComponent = tmpChild.AddComponent<TextMeshProUGUI>();
        
        // 设置默认属性
        tmpComponent.text = txtObject.name;
        tmpComponent.fontSize = 24;
        tmpComponent.color = Color.white;
        tmpComponent.alignment = TextAlignmentOptions.Center;
        
        // 设置RectTransform填充父物体
        tmpRectTransform.anchorMin = Vector2.zero;
        tmpRectTransform.anchorMax = Vector2.one;
        tmpRectTransform.sizeDelta = Vector2.zero;
        tmpRectTransform.anchoredPosition = Vector2.zero;
        
        Debug.Log($"[ChangePSBToPrefab] 为 '{txtObject.name}' 创建了TextMeshPro子物体");
    }
    
    /// <summary>
    /// 为根物体添加UIBindParent组件
    /// </summary>
    private static void AddUIBindParentToRoot(GameObject root)
    {
        Debug.Log("[ChangePSBToPrefab] 步骤4: 为根物体添加UIBindParent...");
        
        // 检查是否已存在UIBindParent组件
        UIBindParent bindParent = root.GetComponent<UIBindParent>();
        if (bindParent == null)
        {
            bindParent = root.AddComponent<UIBindParent>();
            Debug.Log($"[ChangePSBToPrefab] 为 '{root.name}' 添加了UIBindParent组件");
        }
        else
        {
            Debug.Log($"[ChangePSBToPrefab] '{root.name}' 已存在UIBindParent组件");
        }
        
        // 设置默认值
        if (string.IsNullOrEmpty(bindParent.ClassName))
        {
            bindParent.ClassName = root.name;
        }
        
        if (string.IsNullOrEmpty(bindParent.Namespace))
        {
            bindParent.Namespace = "_Scripts.UI";
        }
        
        if (string.IsNullOrEmpty(bindParent.ScriptPath))
        {
            bindParent.ScriptPath = "Scripts/UI/Generated";
        }
    }
}

