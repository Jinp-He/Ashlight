using UnityEngine;
using UnityEditor;
using _Scripts.UI;
using System.Collections.Generic;

namespace _Scripts.Editor
{
    /// <summary>
    /// UIBindParent的自定义编辑器
    /// 显示依赖树和所有绑定信息
    /// </summary>
    [CustomEditor(typeof(UIBindParent))]
    public class UIBindParentEditor : UnityEditor.Editor
    {
        private SerializedProperty classNameProperty;
        private SerializedProperty namespaceProperty;
        private SerializedProperty scriptPathProperty;
        
        private bool showBindTree = true;
        private Vector2 scrollPosition;

        private void OnEnable()
        {
            classNameProperty = serializedObject.FindProperty("ClassName");
            namespaceProperty = serializedObject.FindProperty("Namespace");
            scriptPathProperty = serializedObject.FindProperty("ScriptPath");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            
            UIBindParent bindParent = (UIBindParent)target;
            
            EditorGUILayout.Space(5);
            
            // 基本信息
            EditorGUILayout.LabelField("UI绑定配置", EditorStyles.boldLabel);
            EditorGUILayout.Space(3);
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(classNameProperty, new GUIContent("类名"));
            if (GUILayout.Button("使用GameObject名称", GUILayout.Width(120)))
            {
                classNameProperty.stringValue = bindParent.gameObject.name;
            }
            EditorGUILayout.EndHorizontal();
            
            if (string.IsNullOrEmpty(classNameProperty.stringValue))
            {
                EditorGUILayout.HelpBox($"将使用GameObject名称: {bindParent.gameObject.name}", MessageType.Info);
            }
            
            EditorGUILayout.PropertyField(namespaceProperty, new GUIContent("命名空间"));
            EditorGUILayout.PropertyField(scriptPathProperty, new GUIContent("脚本路径"));
            
            EditorGUILayout.Space(10);
            
            // 收集绑定信息
            UIBind[] binds = CollectUIBinds(bindParent.transform, bindParent);
            
            // 统计信息
            EditorGUILayout.LabelField("绑定统计", EditorStyles.boldLabel);
            EditorGUILayout.Space(3);
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"总绑定数量: {binds.Length}", EditorStyles.miniLabel);
            
            // 统计组件类型
            Dictionary<string, int> typeCount = new Dictionary<string, int>();
            foreach (UIBind bind in binds)
            {
                string componentType = GetComponentTypeName(bind);
                if (typeCount.ContainsKey(componentType))
                    typeCount[componentType]++;
                else
                    typeCount[componentType] = 1;
            }
            
            foreach (var kvp in typeCount)
            {
                EditorGUILayout.LabelField($"  - {kvp.Key}: {kvp.Value}", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.Space(10);
            
            // 绑定树视图
            showBindTree = EditorGUILayout.Foldout(showBindTree, $"绑定树视图 ({binds.Length} 项)", true, EditorStyles.foldoutHeader);
            
            if (showBindTree && binds.Length > 0)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.MaxHeight(300));
                
                foreach (UIBind bind in binds)
                {
                    DrawBindItem(bind, bindParent.transform);
                }
                
                EditorGUILayout.EndScrollView();
                EditorGUILayout.EndVertical();
            }
            else if (binds.Length == 0)
            {
                EditorGUILayout.HelpBox("没有找到任何UIBind组件\n请在子对象上添加UIBind组件", MessageType.Warning);
            }
            
            EditorGUILayout.Space(10);
            
            // 生成按钮
            GUI.backgroundColor = Color.green;
            if (GUILayout.Button("生成UI绑定代码", GUILayout.Height(35)))
            {
                // 触发生成代码菜单项
                EditorApplication.ExecuteMenuItem("GameObject/生成UI绑定代码");
            }
            GUI.backgroundColor = Color.white;
            
            if (binds.Length == 0)
            {
                EditorGUILayout.HelpBox("提示：生成前请确保已添加UIBind组件", MessageType.Info);
            }
            
            serializedObject.ApplyModifiedProperties();
        }
        
        /// <summary>
        /// 绘制单个绑定项
        /// </summary>
        private void DrawBindItem(UIBind bind, Transform root)
        {
            EditorGUILayout.BeginHorizontal();
            
            // 计算缩进级别
            int indentLevel = GetIndentLevel(bind.transform, root);
            GUILayout.Space(indentLevel * 20);
            
            // 图标
            string icon = GetComponentIcon(bind);
            GUIStyle labelStyle = new GUIStyle(EditorStyles.label);
            labelStyle.richText = true;
            
            // 显示层级和名称
            string prefix = GetTreePrefix(indentLevel);
            string bindName = bind.GetBindName();
            string componentType = GetComponentTypeName(bind);
            
            // 颜色标记不同类型
            Color typeColor = GetTypeColor(componentType);
            string colorHex = ColorUtility.ToHtmlStringRGB(typeColor);
            
            GUILayout.Label($"{prefix}{icon}", GUILayout.Width(30));
            GUILayout.Label($"{bindName}", GUILayout.Width(150));
            GUILayout.Label($"<color=#{colorHex}>{componentType}</color>", labelStyle, GUILayout.Width(120));
            
            GUILayout.FlexibleSpace();
            
            // 操作按钮
            if (GUILayout.Button("选择", GUILayout.Width(50)))
            {
                Selection.activeGameObject = bind.gameObject;
                EditorGUIUtility.PingObject(bind.gameObject);
            }
            
            EditorGUILayout.EndHorizontal();
            
            // 如果是容器，显示子项数量
            if (bind.IsContainer)
            {
                UIBind[] children = bind.GetChildBinds();
                if (children.Length > 0)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space((indentLevel + 1) * 20);
                    GUILayout.Label($"(容器，包含 {children.Length} 个子项)", EditorStyles.miniLabel);
                    EditorGUILayout.EndHorizontal();
                }
            }
        }
        
        /// <summary>
        /// 获取缩进级别
        /// </summary>
        private int GetIndentLevel(Transform current, Transform root)
        {
            int level = 0;
            Transform parent = current.parent;
            while (parent != null && parent != root)
            {
                level++;
                parent = parent.parent;
            }
            return level;
        }
        
        /// <summary>
        /// 获取树形前缀
        /// </summary>
        private string GetTreePrefix(int level)
        {
            if (level == 0) return "├─";
            return new string(' ', (level - 1) * 2) + "└─";
        }
        
        /// <summary>
        /// 获取组件图标
        /// </summary>
        private string GetComponentIcon(UIBind bind)
        {
            string componentType = GetComponentTypeName(bind);
            
            if (componentType.Contains("Button")) return "▶";
            if (componentType.Contains("Text")) return "T";
            if (componentType.Contains("Image")) return "◆";
            if (componentType.Contains("Toggle")) return "☑";
            if (componentType.Contains("Slider")) return "―";
            if (componentType.Contains("Input")) return "✎";
            if (componentType.Contains("Scroll")) return "↕";
            if (componentType.Contains("Dropdown")) return "▼";
            if (bind.IsContainer) return "◉";
            
            return "●";
        }
        
        /// <summary>
        /// 获取类型颜色
        /// </summary>
        private Color GetTypeColor(string componentType)
        {
            if (componentType.Contains("Button")) return new Color(0.3f, 0.7f, 1f);
            if (componentType.Contains("Text")) return new Color(1f, 0.8f, 0.3f);
            if (componentType.Contains("Image")) return new Color(0.8f, 0.3f, 1f);
            if (componentType.Contains("Toggle")) return new Color(0.3f, 1f, 0.5f);
            if (componentType.Contains("Slider")) return new Color(1f, 0.5f, 0.3f);
            if (componentType.Contains("Input")) return new Color(0.5f, 1f, 1f);
            
            return Color.gray;
        }
        
        /// <summary>
        /// 获取组件类型名称
        /// </summary>
        private string GetComponentTypeName(UIBind bind)
        {
            string componentType = bind.GetComponentType();
            if (!string.IsNullOrEmpty(componentType))
            {
                return componentType;
            }
            
            // 自动检测
            Component component = GetMainComponent(bind.gameObject);
            return component != null ? component.GetType().Name : "GameObject";
        }
        
        /// <summary>
        /// 收集UIBind组件
        /// </summary>
        private UIBind[] CollectUIBinds(Transform root, UIBindParent rootParent)
        {
            List<UIBind> collectedBinds = new List<UIBind>();
            CollectUIBindsRecursive(root, rootParent, collectedBinds, true);
            return collectedBinds.ToArray();
        }
        
        /// <summary>
        /// 递归收集UIBind
        /// </summary>
        private void CollectUIBindsRecursive(Transform current, UIBindParent rootParent, List<UIBind> result, bool isRoot = false)
        {
            if (!isRoot)
            {
                UIBind bind = current.GetComponent<UIBind>();
                if (bind != null)
                {
                    result.Add(bind);
                }
            }
            
            foreach (Transform child in current)
            {
                UIBindParent childParent = child.GetComponent<UIBindParent>();
                if (childParent != null && childParent != rootParent)
                {
                    UIBind childBind = child.GetComponent<UIBind>();
                    if (childBind != null)
                    {
                        result.Add(childBind);
                    }
                    continue;
                }
                CollectUIBindsRecursive(child, rootParent, result, false);
            }
        }
        
        /// <summary>
        /// 获取主要组件
        /// </summary>
        private Component GetMainComponent(GameObject obj)
        {
            Component component = null;
            
            // TextMeshPro
            component = obj.GetComponent<TMPro.TMP_Text>();
            if (component != null) return component;
            
            component = obj.GetComponent<TMPro.TextMeshProUGUI>();
            if (component != null) return component;
            
            component = obj.GetComponent<TMPro.TMP_InputField>();
            if (component != null) return component;
            
            // Unity UI
            component = obj.GetComponent<UnityEngine.UI.Button>();
            if (component != null) return component;
            
            component = obj.GetComponent<UnityEngine.UI.Image>();
            if (component != null) return component;
            
            component = obj.GetComponent<UnityEngine.UI.Toggle>();
            if (component != null) return component;
            
            component = obj.GetComponent<UnityEngine.UI.Slider>();
            if (component != null) return component;
            
            component = obj.GetComponent<UnityEngine.UI.InputField>();
            if (component != null) return component;
            
            component = obj.GetComponent<UnityEngine.UI.ScrollRect>();
            if (component != null) return component;
            
            return null;
        }
    }
}

