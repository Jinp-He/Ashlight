using UnityEngine;
using UnityEditor;
using _Scripts.UI;

namespace _Scripts.Editor
{
    /// <summary>
    /// UIBind的Inspector编辑器
    /// 自动填充BindName和ComponentType
    /// </summary>
    [CustomEditor(typeof(UIBind))]
    public class UIBindEditor : UnityEditor.Editor
    {
        /// <summary>
        /// 快捷键：为选中的GameObject添加UIBind组件
        /// 快捷键：Ctrl+Shift+B
        /// </summary>
        [MenuItem("Tools/UIBind/Add UIBind to Selected %#b")]
        private static void AddUIBindToSelected()
        {
            if (Selection.gameObjects.Length == 0)
            {
                EditorUtility.DisplayDialog("提示", "请先选择要添加UIBind的GameObject", "确定");
                return;
            }

            int addedCount = 0;
            int skippedCount = 0;

            foreach (GameObject obj in Selection.gameObjects)
            {
                // 检查是否已经有UIBind组件
                if (obj.GetComponent<UIBind>() != null)
                {
                    skippedCount++;
                    Debug.LogWarning($"GameObject [{obj.name}] 已经包含UIBind组件，跳过");
                    continue;
                }

                // 添加UIBind组件
                UIBind bind = obj.AddComponent<UIBind>();
                
                // 自动填充BindName为GameObject名称
                bind.BindName = obj.name;
                
                // 自动检测并填充ComponentType
                Component mainComponent = GetMainComponentStatic(obj);
                if (mainComponent != null)
                {
                    bind.ComponentType = mainComponent.GetType().Name;
                }
                else
                {
                    bind.ComponentType = "GameObject";
                }

                // 标记为已修改
                EditorUtility.SetDirty(obj);
                
                addedCount++;
                Debug.Log($"已为 [{obj.name}] 添加UIBind组件，类型: {bind.ComponentType}");
            }

            // 显示结果提示
            string message = $"成功添加: {addedCount} 个";
            if (skippedCount > 0)
            {
                message += $"\n跳过(已存在): {skippedCount} 个";
            }
            
            EditorUtility.DisplayDialog("添加UIBind完成", message, "确定");
        }

        /// <summary>
        /// 静态方法：获取GameObject上最主要的UI组件
        /// </summary>
        private static Component GetMainComponentStatic(GameObject obj)
        {
            Component component = null;
            
            // TextMeshPro组件
            component = obj.GetComponent<TMPro.TMP_Text>();
            if (component != null) return component;
            
            component = obj.GetComponent<TMPro.TextMeshProUGUI>();
            if (component != null) return component;
            
            component = obj.GetComponent<TMPro.TMP_InputField>();
            if (component != null) return component;
            
            // Unity UI组件
            component = obj.GetComponent<UnityEngine.UI.Button>();
            if (component != null) return component;
            
            component = obj.GetComponent<UnityEngine.UI.Image>();
            if (component != null) return component;
            
            component = obj.GetComponent<UnityEngine.UI.RawImage>();
            if (component != null) return component;
            
            component = obj.GetComponent<UnityEngine.UI.Toggle>();
            if (component != null) return component;
            
            component = obj.GetComponent<UnityEngine.UI.Slider>();
            if (component != null) return component;
            
            component = obj.GetComponent<UnityEngine.UI.Scrollbar>();
            if (component != null) return component;
            
            component = obj.GetComponent<UnityEngine.UI.Dropdown>();
            if (component != null) return component;
            
            component = obj.GetComponent<UnityEngine.UI.InputField>();
            if (component != null) return component;
            
            component = obj.GetComponent<UnityEngine.UI.Text>();
            if (component != null) return component;
            
            // 布局组件
            component = obj.GetComponent<UnityEngine.UI.ScrollRect>();
            if (component != null) return component;
            
            component = obj.GetComponent<RectTransform>();
            if (component != null) return component;
            
            return null;
        }
        private SerializedProperty bindNameProperty;
        private SerializedProperty componentTypeProperty;
        private SerializedProperty isContainerProperty;

        private void OnEnable()
        {
            bindNameProperty = serializedObject.FindProperty("BindName");
            componentTypeProperty = serializedObject.FindProperty("ComponentType");
            isContainerProperty = serializedObject.FindProperty("IsContainer");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            
            UIBind bind = (UIBind)target;
            
            EditorGUILayout.Space(5);
            
            // BindName字段
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(bindNameProperty, new GUIContent("Bind Name", "留空自动使用GameObject名称"));
            if (GUILayout.Button("Auto Fill", GUILayout.Width(80)))
            {
                bindNameProperty.stringValue = bind.gameObject.name;
            }
            EditorGUILayout.EndHorizontal();
            
            // 显示实际使用的名称（如果为空）
            if (string.IsNullOrEmpty(bindNameProperty.stringValue))
            {
                EditorGUILayout.HelpBox($"将自动使用GameObject名称: {bind.gameObject.name}", MessageType.Info);
            }
            
            EditorGUILayout.Space(5);
            
            // ComponentType字段 - 使用下拉选择
            EditorGUILayout.LabelField("Component Type", EditorStyles.boldLabel);
            
            // 获取所有组件
            Component[] components = bind.gameObject.GetComponents<Component>();
            string[] componentNames = new string[components.Length + 2];
            componentNames[0] = "(Auto Detect)";
            componentNames[1] = "GameObject";
            
            int currentIndex = 0;
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] != null)
                {
                    string typeName = components[i].GetType().Name;
                    componentNames[i + 2] = typeName;
                    
                    // 找到当前选中的索引
                    if (!string.IsNullOrEmpty(componentTypeProperty.stringValue) && 
                        componentTypeProperty.stringValue == typeName)
                    {
                        currentIndex = i + 2;
                    }
                }
            }
            
            // 如果是空值，显示为 Auto Detect
            if (string.IsNullOrEmpty(componentTypeProperty.stringValue))
            {
                currentIndex = 0;
            }
            else if (componentTypeProperty.stringValue == "GameObject")
            {
                currentIndex = 1;
            }
            
            EditorGUILayout.BeginHorizontal();
            int newIndex = EditorGUILayout.Popup(currentIndex, componentNames);
            
            if (newIndex != currentIndex)
            {
                if (newIndex == 0)
                {
                    // Auto Detect
                    componentTypeProperty.stringValue = "";
                }
                else if (newIndex == 1)
                {
                    // GameObject
                    componentTypeProperty.stringValue = "GameObject";
                }
                else
                {
                    // 选择的组件
                    componentTypeProperty.stringValue = componentNames[newIndex];
                }
            }
            
            // 添加一个按钮可以快速设置为自动检测
            if (GUILayout.Button("Auto", GUILayout.Width(50)))
            {
                Component component = GetMainComponent(bind.gameObject);
                componentTypeProperty.stringValue = component != null ? component.GetType().Name : "GameObject";
            }
            
            EditorGUILayout.EndHorizontal();
            
            // 显示当前选择的提示
            if (string.IsNullOrEmpty(componentTypeProperty.stringValue))
            {
                Component detectedComponent = GetMainComponent(bind.gameObject);
                string detectedType = detectedComponent != null ? detectedComponent.GetType().Name : "GameObject";
                EditorGUILayout.HelpBox($"将自动检测为: {detectedType}", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox($"当前绑定类型: {componentTypeProperty.stringValue}", MessageType.None);
            }
            
            EditorGUILayout.Space(5);
            
            // IsContainer字段
            EditorGUILayout.PropertyField(isContainerProperty, new GUIContent("Is Container", "作为容器，可包含子UIBind"));
            
            // 如果是容器，显示子Bind信息
            if (isContainerProperty.boolValue)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Child Binds:", EditorStyles.boldLabel);
                
                UIBind[] childBinds = bind.GetChildBinds();
                if (childBinds.Length > 0)
                {
                    EditorGUI.indentLevel++;
                    foreach (UIBind childBind in childBinds)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField($"└─ {childBind.GetBindName()}", EditorStyles.miniLabel);
                        if (GUILayout.Button("Select", GUILayout.Width(60)))
                        {
                            Selection.activeGameObject = childBind.gameObject;
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                    EditorGUI.indentLevel--;
                }
                else
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.LabelField("(没有直接子级UIBind)", EditorStyles.miniLabel);
                    EditorGUI.indentLevel--;
                }
            }
            
            serializedObject.ApplyModifiedProperties();
        }
        
        /// <summary>
        /// 获取GameObject上最主要的UI组件
        /// </summary>
        private Component GetMainComponent(GameObject obj)
        {
            Component component = null;
            
            // TextMeshPro组件
            component = obj.GetComponent<TMPro.TMP_Text>();
            if (component != null) return component;
            
            component = obj.GetComponent<TMPro.TextMeshProUGUI>();
            if (component != null) return component;
            
            component = obj.GetComponent<TMPro.TMP_InputField>();
            if (component != null) return component;
            
            // Unity UI组件
            component = obj.GetComponent<UnityEngine.UI.Button>();
            if (component != null) return component;
            
            component = obj.GetComponent<UnityEngine.UI.Image>();
            if (component != null) return component;
            
            component = obj.GetComponent<UnityEngine.UI.RawImage>();
            if (component != null) return component;
            
            component = obj.GetComponent<UnityEngine.UI.Toggle>();
            if (component != null) return component;
            
            component = obj.GetComponent<UnityEngine.UI.Slider>();
            if (component != null) return component;
            
            component = obj.GetComponent<UnityEngine.UI.Scrollbar>();
            if (component != null) return component;
            
            component = obj.GetComponent<UnityEngine.UI.Dropdown>();
            if (component != null) return component;
            
            component = obj.GetComponent<UnityEngine.UI.InputField>();
            if (component != null) return component;
            
            component = obj.GetComponent<UnityEngine.UI.Text>();
            if (component != null) return component;
            
            // 布局组件
            component = obj.GetComponent<UnityEngine.UI.ScrollRect>();
            if (component != null) return component;
            
            component = obj.GetComponent<RectTransform>();
            if (component != null) return component;
            
            return null;
        }
    }
}

