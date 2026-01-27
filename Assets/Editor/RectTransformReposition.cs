using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// RectTransform重定位工具
/// 功能：移动父物体到指定位置，同时让子物体反向移动，保持视觉位置不变
/// </summary>
public class RectTransformReposition : EditorWindow
{
    private Vector2 moveOffset = Vector2.zero;
    private bool moveToCenter = true;
    
    [MenuItem("Tools/RectTransform Reposition")]
    public static void ShowWindow()
    {
        GetWindow<RectTransformReposition>("RectTransform重定位");
    }
    
    private void OnGUI()
    {
        GUILayout.Label("RectTransform 重定位工具", EditorStyles.boldLabel);
        GUILayout.Space(10);
        
        // 显示当前选中的对象
        GameObject selected = Selection.activeGameObject;
        if (selected != null)
        {
            EditorGUILayout.LabelField("当前选中:", selected.name);
            
            RectTransform rectTransform = selected.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                EditorGUILayout.LabelField("当前位置:", rectTransform.anchoredPosition.ToString());
                EditorGUILayout.LabelField("子物体数量:", rectTransform.childCount.ToString());
            }
            else
            {
                EditorGUILayout.HelpBox("选中的对象没有RectTransform组件！", MessageType.Warning);
            }
        }
        else
        {
            EditorGUILayout.HelpBox("请在Hierarchy中选择一个GameObject", MessageType.Info);
        }
        
        GUILayout.Space(10);
        
        // 移动选项
        GUILayout.Label("移动选项", EditorStyles.boldLabel);
        
        moveToCenter = EditorGUILayout.Toggle("移动到子物体中心", moveToCenter);
        
        if (!moveToCenter)
        {
            moveOffset = EditorGUILayout.Vector2Field("自定义偏移量", moveOffset);
        }
        
        GUILayout.Space(10);
        
        // 操作按钮
        EditorGUI.BeginDisabledGroup(selected == null || selected.GetComponent<RectTransform>() == null);
        
        if (GUILayout.Button("执行重定位", GUILayout.Height(30)))
        {
            ExecuteReposition(selected);
        }
        
        GUILayout.Space(5);
        
        if (GUILayout.Button("移动到原点 (0, 0)", GUILayout.Height(25)))
        {
            MoveToOrigin(selected);
        }
        
        EditorGUI.EndDisabledGroup();
        
        GUILayout.Space(10);
        
        // 帮助信息
        EditorGUILayout.HelpBox(
            "使用说明：\n" +
            "1. 选择一个有RectTransform的父物体\n" +
            "2. 选择移动方式：\n" +
            "   - 移动到子物体中心：自动计算子物体的中心点\n" +
            "   - 自定义偏移量：手动输入移动距离\n" +
            "3. 点击'执行重定位'\n" +
            "4. 父物体会移动，子物体会反向移动，整体视觉不变", 
            MessageType.Info);
    }
    
    /// <summary>
    /// 执行重定位
    /// </summary>
    private void ExecuteReposition(GameObject obj)
    {
        if (obj == null) return;
        
        RectTransform parentRect = obj.GetComponent<RectTransform>();
        if (parentRect == null) return;
        
        // 记录Undo
        Undo.RegisterFullObjectHierarchyUndo(obj, "RectTransform Reposition");
        
        Vector2 offset;
        
        if (moveToCenter)
        {
            // 计算子物体的中心点
            offset = CalculateChildrenCenter(parentRect);
            Debug.Log($"[RectTransformReposition] 计算的子物体中心偏移: {offset}");
        }
        else
        {
            offset = moveOffset;
        }
        
        // 执行移动
        RepositionWithOffset(parentRect, offset);
        
        EditorUtility.SetDirty(obj);
        Debug.Log($"[RectTransformReposition] 完成重定位: {obj.name}, 偏移: {offset}");
    }
    
    /// <summary>
    /// 移动到原点
    /// </summary>
    private void MoveToOrigin(GameObject obj)
    {
        if (obj == null) return;
        
        RectTransform parentRect = obj.GetComponent<RectTransform>();
        if (parentRect == null) return;
        
        // 记录Undo
        Undo.RegisterFullObjectHierarchyUndo(obj, "RectTransform Move To Origin");
        
        // 当前位置就是需要的偏移量（移动到原点）
        Vector2 offset = parentRect.anchoredPosition;
        
        // 执行移动（父物体移动到原点，子物体反向移动）
        RepositionWithOffset(parentRect, -offset);
        
        EditorUtility.SetDirty(obj);
        Debug.Log($"[RectTransformReposition] 已移动到原点: {obj.name}");
    }
    
    /// <summary>
    /// 计算所有子物体的中心点
    /// </summary>
    private Vector2 CalculateChildrenCenter(RectTransform parent)
    {
        if (parent.childCount == 0)
        {
            return Vector2.zero;
        }
        
        Vector2 sum = Vector2.zero;
        int count = 0;
        
        foreach (Transform child in parent)
        {
            RectTransform childRect = child.GetComponent<RectTransform>();
            if (childRect != null)
            {
                sum += childRect.anchoredPosition;
                count++;
            }
        }
        
        if (count == 0)
        {
            return Vector2.zero;
        }
        
        return sum / count;
    }
    
    /// <summary>
    /// 使用指定偏移量重定位
    /// 父物体移动offset，子物体移动-offset
    /// </summary>
    private void RepositionWithOffset(RectTransform parent, Vector2 offset)
    {
        if (offset == Vector2.zero)
        {
            Debug.Log("[RectTransformReposition] 偏移量为0，无需移动");
            return;
        }
        
        // 收集所有直接子物体的RectTransform
        List<RectTransform> children = new List<RectTransform>();
        foreach (Transform child in parent)
        {
            RectTransform childRect = child.GetComponent<RectTransform>();
            if (childRect != null)
            {
                children.Add(childRect);
            }
        }
        
        // 先移动所有子物体（反向）
        foreach (RectTransform child in children)
        {
            child.anchoredPosition -= offset;
            Debug.Log($"[RectTransformReposition] 子物体 '{child.name}' 移动: -{offset}");
        }
        
        // 再移动父物体
        parent.anchoredPosition += offset;
        Debug.Log($"[RectTransformReposition] 父物体 '{parent.name}' 移动: +{offset}");
    }
    
    /// <summary>
    /// 快捷菜单：移动到子物体中心
    /// </summary>
    [MenuItem("GameObject/RectTransform/移动到子物体中心", false, 0)]
    public static void MoveToChildrenCenter()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected == null) return;
        
        RectTransform parentRect = selected.GetComponent<RectTransform>();
        if (parentRect == null)
        {
            EditorUtility.DisplayDialog("错误", "选中的对象没有RectTransform组件！", "确定");
            return;
        }
        
        // 记录Undo
        Undo.RegisterFullObjectHierarchyUndo(selected, "Move To Children Center");
        
        // 计算中心点
        Vector2 center = CalculateChildrenCenterStatic(parentRect);
        
        // 执行移动
        RepositionWithOffsetStatic(parentRect, center);
        
        EditorUtility.SetDirty(selected);
        Debug.Log($"[RectTransformReposition] 已移动到子物体中心: {selected.name}");
    }
    
    /// <summary>
    /// 快捷菜单：移动到原点并补偿子物体
    /// </summary>
    [MenuItem("GameObject/RectTransform/移动到原点(0,0)", false, 1)]
    public static void MoveToOriginMenu()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected == null) return;
        
        RectTransform parentRect = selected.GetComponent<RectTransform>();
        if (parentRect == null)
        {
            EditorUtility.DisplayDialog("错误", "选中的对象没有RectTransform组件！", "确定");
            return;
        }
        
        // 记录Undo
        Undo.RegisterFullObjectHierarchyUndo(selected, "Move To Origin");
        
        // 当前位置的负值就是偏移量
        Vector2 offset = -parentRect.anchoredPosition;
        
        // 执行移动
        RepositionWithOffsetStatic(parentRect, offset);
        
        EditorUtility.SetDirty(selected);
        Debug.Log($"[RectTransformReposition] 已移动到原点: {selected.name}");
    }
    
    // 静态版本的方法，供菜单项使用
    private static Vector2 CalculateChildrenCenterStatic(RectTransform parent)
    {
        if (parent.childCount == 0)
        {
            return Vector2.zero;
        }
        
        Vector2 sum = Vector2.zero;
        int count = 0;
        
        foreach (Transform child in parent)
        {
            RectTransform childRect = child.GetComponent<RectTransform>();
            if (childRect != null)
            {
                sum += childRect.anchoredPosition;
                count++;
            }
        }
        
        if (count == 0)
        {
            return Vector2.zero;
        }
        
        return sum / count;
    }
    
    private static void RepositionWithOffsetStatic(RectTransform parent, Vector2 offset)
    {
        if (offset == Vector2.zero)
        {
            Debug.Log("[RectTransformReposition] 偏移量为0，无需移动");
            return;
        }
        
        // 先移动所有子物体（反向）
        foreach (Transform child in parent)
        {
            RectTransform childRect = child.GetComponent<RectTransform>();
            if (childRect != null)
            {
                childRect.anchoredPosition -= offset;
            }
        }
        
        // 再移动父物体
        parent.anchoredPosition += offset;
    }
}


