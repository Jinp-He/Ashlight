using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Text;
using _Scripts.UI;
using UnityEngine.UI;
using TMPro;
using System;
using System.Reflection;
using UnityEditor.Callbacks;

namespace _Scripts.Editor
{
    /// <summary>
    /// UI绑定代码生成器
    /// </summary>
    public class UIBindGenerator
    {
        private const string PREF_PENDING_INSTANCE_ID = "UIBind_PendingInstanceID";
        private const string PREF_PENDING_CLASS_NAME = "UIBind_PendingClassName";
        private const string PREF_PENDING_NAMESPACE = "UIBind_PendingNamespace";
        private const string PREF_PENDING_SCENE_PATH = "UIBind_PendingScenePath";
        private const string PREF_IS_WAITING = "UIBind_IsWaiting";
        
        [MenuItem("GameObject/生成UI绑定代码", false, 0)]
        static void GenerateUIBindCode()
        {
            GameObject selectedObject = Selection.activeGameObject;
            
            if (selectedObject == null)
            {
                EditorUtility.DisplayDialog("错误", "请先选中一个GameObject", "确定");
                return;
            }
            
            UIBindParent bindParent = selectedObject.GetComponent<UIBindParent>();
            if (bindParent == null)
            {
                EditorUtility.DisplayDialog("错误", "选中的GameObject需要有UIBindParent组件", "确定");
                return;
            }
            
            // 收集所有UIBind组件（排除嵌套的UIBindParent下的UIBind）
            UIBind[] binds = CollectUIBinds(selectedObject.transform, bindParent);
            
            if (binds.Length == 0)
            {
                EditorUtility.DisplayDialog("提示", "没有找到任何UIBind组件", "确定");
                return;
            }
            
            // 生成代码
            string code = GenerateCode(bindParent, binds);
            
            // 保存文件
            string fullPath = Path.Combine(Application.dataPath, bindParent.ScriptPath);
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }
            
            string fileName = bindParent.GetClassName() + ".cs";
            string filePath = Path.Combine(fullPath, fileName);
            
            File.WriteAllText(filePath, code);
            
            // 使用 SessionState 保存待处理信息（在编译后仍然保留）
            SessionState.SetInt(PREF_PENDING_INSTANCE_ID, selectedObject.GetInstanceID());
            SessionState.SetString(PREF_PENDING_CLASS_NAME, bindParent.GetClassName());
            SessionState.SetString(PREF_PENDING_NAMESPACE, bindParent.Namespace);
            SessionState.SetString(PREF_PENDING_SCENE_PATH, selectedObject.scene.path);
            SessionState.SetBool(PREF_IS_WAITING, true);
            
            AssetDatabase.Refresh();
            
            Debug.Log($"已生成UI绑定代码：{filePath}\n等待编译完成后自动添加组件...");
        }
        
        [MenuItem("GameObject/生成UI绑定代码", true)]
        static bool ValidateGenerateUIBindCode()
        {
            GameObject selectedObject = Selection.activeGameObject;
            return selectedObject != null && selectedObject.GetComponent<UIBindParent>() != null;
        }
        
        [MenuItem("GameObject/手动绑定UI引用", false, 1)]
        static void ManualBindReferences()
        {
            GameObject selectedObject = Selection.activeGameObject;
            
            if (selectedObject == null)
            {
                EditorUtility.DisplayDialog("错误", "请先选中一个GameObject", "确定");
                return;
            }
            
            UIBindParent bindParent = selectedObject.GetComponent<UIBindParent>();
            if (bindParent == null)
            {
                EditorUtility.DisplayDialog("错误", "选中的GameObject需要有UIBindParent组件", "确定");
                return;
            }
            
            // 查找生成的组件
            string className = bindParent.GetClassName();
            string namespaceName = bindParent.Namespace;
            string fullTypeName = string.IsNullOrEmpty(namespaceName) 
                ? className 
                : $"{namespaceName}.{className}";
            
            Type generatedType = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                generatedType = assembly.GetType(fullTypeName);
                if (generatedType != null)
                    break;
            }
            
            if (generatedType == null)
            {
                EditorUtility.DisplayDialog("错误", 
                    $"找不到生成的类型: {fullTypeName}\n请先生成UI绑定代码", 
                    "确定");
                return;
            }
            
            Component targetComponent = selectedObject.GetComponent(generatedType);
            if (targetComponent == null)
            {
                // 尝试添加组件
                if (EditorUtility.DisplayDialog("提示", 
                    $"GameObject上没有{className}组件\n是否自动添加？", 
                    "添加", "取消"))
                {
                    targetComponent = selectedObject.AddComponent(generatedType);
                    Debug.Log($"已添加组件: {className}");
                }
                else
                {
                    return;
                }
            }
            
            // 收集UIBind
            UIBind[] binds = CollectUIBinds(selectedObject.transform, bindParent);
            
            if (binds.Length == 0)
            {
                EditorUtility.DisplayDialog("提示", "没有找到任何UIBind组件", "确定");
                return;
            }
            
            // 绑定引用
            AutoBindReferences(targetComponent, binds);
            
            // 标记场景已修改
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(selectedObject.scene);
            
            EditorUtility.DisplayDialog("成功", 
                $"已完成引用绑定！\n组件: {className}\n绑定数量: {binds.Length}", 
                "确定");
        }
        
        [MenuItem("GameObject/手动绑定UI引用", true)]
        static bool ValidateManualBindReferences()
        {
            GameObject selectedObject = Selection.activeGameObject;
            return selectedObject != null && selectedObject.GetComponent<UIBindParent>() != null;
        }
        
        /// <summary>
        /// 编译完成后的回调，自动添加组件并绑定引用
        /// </summary>
        [DidReloadScripts]
        static void OnScriptsReloaded()
        {
            // 从 SessionState 读取待处理信息
            bool isWaiting = SessionState.GetBool(PREF_IS_WAITING, false);
            if (!isWaiting)
            {
                return;
            }
            
            // 立即清除标志，避免重复执行
            SessionState.SetBool(PREF_IS_WAITING, false);
            
            // 延迟执行，确保所有脚本已加载
            EditorApplication.delayCall += () =>
            {
                try
                {
                    // 读取保存的信息
                    int instanceID = SessionState.GetInt(PREF_PENDING_INSTANCE_ID, 0);
                    string className = SessionState.GetString(PREF_PENDING_CLASS_NAME, "");
                    string namespaceName = SessionState.GetString(PREF_PENDING_NAMESPACE, "");
                    string scenePath = SessionState.GetString(PREF_PENDING_SCENE_PATH, "");
                    
                    if (instanceID == 0 || string.IsNullOrEmpty(className))
                    {
                        Debug.LogWarning("无效的待处理信息");
                        return;
                    }
                    
                    // 通过 InstanceID 查找 GameObject
                    GameObject targetObject = EditorUtility.InstanceIDToObject(instanceID) as GameObject;
                    
                    if (targetObject == null)
                    {
                        Debug.LogWarning($"找不到GameObject (InstanceID: {instanceID})，可能已被删除");
                        return;
                    }
                    
                    // 查找生成的类型
                    string fullTypeName = string.IsNullOrEmpty(namespaceName) 
                        ? className 
                        : $"{namespaceName}.{className}";
                    
                    Type generatedType = null;
                    foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        generatedType = assembly.GetType(fullTypeName);
                        if (generatedType != null)
                            break;
                    }
                    
                    if (generatedType == null)
                    {
                        Debug.LogError($"找不到生成的类型: {fullTypeName}");
                        return;
                    }
                    
                    // 获取 UIBindParent 和 UIBind 信息
                    UIBindParent bindParent = targetObject.GetComponent<UIBindParent>();
                    if (bindParent == null)
                    {
                        Debug.LogWarning("GameObject上没有UIBindParent组件");
                        return;
                    }
                    
                    UIBind[] binds = CollectUIBinds(targetObject.transform, bindParent);
                    
                    // 检查是否已经有该组件
                    Component existingComponent = targetObject.GetComponent(generatedType);
                    if (existingComponent == null)
                    {
                        // 添加组件
                        existingComponent = targetObject.AddComponent(generatedType);
                        Debug.Log($"✅ 已自动添加组件: {className}");
                    }
                    else
                    {
                        Debug.Log($"组件已存在: {className}，将更新引用");
                    }
                    
                    // 自动绑定引用
                    AutoBindReferences(existingComponent, binds);
                    
                    // 标记场景已修改
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                        targetObject.scene);
                    
                    EditorUtility.DisplayDialog("成功", 
                        $"已完成UI绑定代码生成和组件自动挂载！\n" +
                        $"组件: {className}\n" +
                        $"绑定数量: {binds.Length}", 
                        "确定");
                }
                catch (Exception e)
                {
                    Debug.LogError($"自动添加组件失败: {e.Message}\n{e.StackTrace}");
                }
                finally
                {
                    // 清理 SessionState
                    SessionState.EraseInt(PREF_PENDING_INSTANCE_ID);
                    SessionState.EraseString(PREF_PENDING_CLASS_NAME);
                    SessionState.EraseString(PREF_PENDING_NAMESPACE);
                    SessionState.EraseString(PREF_PENDING_SCENE_PATH);
                }
            };
        }
        
        /// <summary>
        /// 自动绑定引用
        /// </summary>
        static void AutoBindReferences(Component component, UIBind[] binds)
        {
            if (component == null || binds == null || binds.Length == 0)
                return;
            
            SerializedObject serializedObject = new SerializedObject(component);
            Type componentType = component.GetType();
            
            // 构建绑定信息字典
            Dictionary<string, UIBind> bindDict = new Dictionary<string, UIBind>();
            Dictionary<string, int> nameCount = new Dictionary<string, int>();
            
            foreach (UIBind bind in binds)
            {
                string bindName = bind.GetBindName();
                
                // 处理重名（与生成代码时的逻辑一致）
                string finalName = bindName;
                if (nameCount.ContainsKey(bindName))
                {
                    nameCount[bindName]++;
                    finalName = bindName + nameCount[bindName];
                }
                else
                {
                    nameCount[bindName] = 0;
                }
                
                bindDict[finalName] = bind;
            }
            
            // 遍历所有字段并赋值
            int assignedCount = 0;
            foreach (var kvp in bindDict)
            {
                string fieldName = kvp.Key;
                UIBind bind = kvp.Value;
                
                // 获取字段
                FieldInfo field = componentType.GetField(fieldName, 
                    BindingFlags.Public | BindingFlags.Instance);
                
                if (field == null)
                {
                    Debug.LogWarning($"找不到字段: {fieldName}");
                    continue;
                }
                
                // 获取要绑定的组件或GameObject
                UnityEngine.Object targetObject = null;
                string componentTypeName = bind.GetComponentType();
                
                if (string.IsNullOrEmpty(componentTypeName))
                {
                    // 自动检测
                    Component mainComponent = GetMainComponent(bind.gameObject);
                    targetObject = mainComponent != null ? mainComponent : bind.gameObject;
                }
                else if (componentTypeName == "GameObject")
                {
                    targetObject = bind.gameObject;
                }
                else
                {
                    // 获取指定类型的组件
                    targetObject = bind.gameObject.GetComponent(componentTypeName);
                }
                
                if (targetObject != null)
                {
                    // 使用SerializedProperty赋值
                    SerializedProperty property = serializedObject.FindProperty(fieldName);
                    if (property != null)
                    {
                        property.objectReferenceValue = targetObject;
                        assignedCount++;
                    }
                    else
                    {
                        // 直接赋值
                        field.SetValue(component, targetObject);
                        assignedCount++;
                    }
                }
                else
                {
                    Debug.LogWarning($"无法获取组件: {fieldName} ({componentTypeName})");
                }
            }
            
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(component);
            
            Debug.Log($"已自动绑定 {assignedCount}/{bindDict.Count} 个引用");
        }

        static string GenerateCode(UIBindParent bindParent, UIBind[] binds)
        {
            StringBuilder sb = new StringBuilder();
            
            // Using statements
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine("using UnityEngine.UI;");
            sb.AppendLine("using TMPro;");
            sb.AppendLine("using _Scripts.UI;");
            sb.AppendLine();
            
            // Namespace
            if (!string.IsNullOrEmpty(bindParent.Namespace))
            {
                sb.AppendLine($"namespace {bindParent.Namespace}");
                sb.AppendLine("{");
            }
            
            // Class
            string indent = string.IsNullOrEmpty(bindParent.Namespace) ? "" : "    ";
            sb.AppendLine($"{indent}/// <summary>");
            sb.AppendLine($"{indent}/// 自动生成的UI绑定代码");
            sb.AppendLine($"{indent}/// </summary>");
            sb.AppendLine($"{indent}public partial class {bindParent.GetClassName()} : MonoBehaviour");
            sb.AppendLine($"{indent}{{");
            
            // 构建层级结构
            List<BindNode> rootNodes = BuildBindHierarchy(binds);
            
            // 存储已使用的名称，避免重复
            Dictionary<string, int> nameCount = new Dictionary<string, int>();
            List<BindInfo> bindInfos = new List<BindInfo>();
            
            // 生成字段
            sb.AppendLine($"{indent}    #region UI Bindings");
            sb.AppendLine();
            
            GenerateBindFields(rootNodes, sb, indent + "    ", nameCount, bindInfos, 0);
            
            sb.AppendLine();
            sb.AppendLine($"{indent}    #endregion");
            sb.AppendLine();
            
            // 生成初始化方法
            sb.AppendLine($"{indent}    /// <summary>");
            sb.AppendLine($"{indent}    /// 初始化UI绑定");
            sb.AppendLine($"{indent}    /// </summary>");
            sb.AppendLine($"{indent}    private void InitUIBindings()");
            sb.AppendLine($"{indent}    {{");
            sb.AppendLine($"{indent}        UIBindParent bindParent = GetComponent<UIBindParent>();");
            sb.AppendLine($"{indent}        if (bindParent == null) return;");
            sb.AppendLine();
            sb.AppendLine($"{indent}        // 收集UIBind，排除嵌套的UIBindParent（不收集自身）");
            sb.AppendLine($"{indent}        System.Collections.Generic.List<UIBind> bindsList = new System.Collections.Generic.List<UIBind>();");
            sb.AppendLine($"{indent}        CollectUIBindsRecursive(transform, bindParent, bindsList, true);");
            sb.AppendLine($"{indent}        UIBind[] binds = bindsList.ToArray();");
            sb.AppendLine();
            sb.AppendLine($"{indent}        foreach (UIBind bind in binds)");
            sb.AppendLine($"{indent}        {{");
            sb.AppendLine($"{indent}            string bindName = bind.GetBindName();");
            sb.AppendLine($"{indent}            switch (bindName)");
            sb.AppendLine($"{indent}            {{");
            
            foreach (BindInfo info in bindInfos)
            {
                sb.AppendLine($"{indent}                case \"{info.OriginalName}\":");
                if (info.ComponentType == "GameObject")
                {
                    sb.AppendLine($"{indent}                    {info.BindName} = bind.gameObject;");
                }
                else
                {
                    sb.AppendLine($"{indent}                    {info.BindName} = bind.GetComponent<{info.ComponentType}>();");
                }
                sb.AppendLine($"{indent}                    break;");
            }
            
            sb.AppendLine($"{indent}            }}");
            sb.AppendLine($"{indent}        }}");
            sb.AppendLine($"{indent}    }}");
            sb.AppendLine();
            
            // 生成递归收集方法
            sb.AppendLine($"{indent}    /// <summary>");
            sb.AppendLine($"{indent}    /// 递归收集UIBind，遇到其他UIBindParent就停止（但会收集该UIBindParent自身的UIBind）");
            sb.AppendLine($"{indent}    /// </summary>");
            sb.AppendLine($"{indent}    private void CollectUIBindsRecursive(Transform current, UIBindParent rootParent, System.Collections.Generic.List<UIBind> result, bool isRoot = false)");
            sb.AppendLine($"{indent}    {{");
            sb.AppendLine($"{indent}        // 不收集 root 自己的 UIBind（因为 root 就是 UIBindParent 本身）");
            sb.AppendLine($"{indent}        if (!isRoot)");
            sb.AppendLine($"{indent}        {{");
            sb.AppendLine($"{indent}            UIBind bind = current.GetComponent<UIBind>();");
            sb.AppendLine($"{indent}            if (bind != null)");
            sb.AppendLine($"{indent}            {{");
            sb.AppendLine($"{indent}                result.Add(bind);");
            sb.AppendLine($"{indent}            }}");
            sb.AppendLine($"{indent}        }}");
            sb.AppendLine();
            sb.AppendLine($"{indent}        foreach (Transform child in current)");
            sb.AppendLine($"{indent}        {{");
            sb.AppendLine($"{indent}            UIBindParent childParent = child.GetComponent<UIBindParent>();");
            sb.AppendLine($"{indent}            if (childParent != null && childParent != rootParent)");
            sb.AppendLine($"{indent}            {{");
            sb.AppendLine($"{indent}                // 如果子对象同时有UIBind，添加它（但不继续递归）");
            sb.AppendLine($"{indent}                UIBind childBind = child.GetComponent<UIBind>();");
            sb.AppendLine($"{indent}                if (childBind != null)");
            sb.AppendLine($"{indent}                {{");
            sb.AppendLine($"{indent}                    result.Add(childBind);");
            sb.AppendLine($"{indent}                }}");
            sb.AppendLine($"{indent}                continue;");
            sb.AppendLine($"{indent}            }}");
            sb.AppendLine($"{indent}            CollectUIBindsRecursive(child, rootParent, result, false);");
            sb.AppendLine($"{indent}        }}");
            sb.AppendLine($"{indent}    }}");
            
            // Close class
            sb.AppendLine($"{indent}}}");
            
            // Close namespace
            if (!string.IsNullOrEmpty(bindParent.Namespace))
            {
                sb.AppendLine("}");
            }
            
            return sb.ToString();
        }
        
        /// <summary>
        /// 构建层级结构
        /// </summary>
        static List<BindNode> BuildBindHierarchy(UIBind[] binds)
        {
            List<BindNode> allNodes = new List<BindNode>();
            Dictionary<UIBind, BindNode> nodeMap = new Dictionary<UIBind, BindNode>();
            
            // 创建所有节点
            foreach (UIBind bind in binds)
            {
                BindNode node = new BindNode { Bind = bind };
                allNodes.Add(node);
                nodeMap[bind] = node;
            }
            
            // 构建父子关系
            foreach (BindNode node in allNodes)
            {
                if (node.Bind.IsContainer)
                {
                    UIBind[] childBinds = node.Bind.GetChildBinds();
                    foreach (UIBind childBind in childBinds)
                    {
                        if (nodeMap.ContainsKey(childBind))
                        {
                            node.Children.Add(nodeMap[childBind]);
                            nodeMap[childBind].Parent = node;
                        }
                    }
                }
            }
            
            // 返回根节点（没有父节点的）
            List<BindNode> rootNodes = new List<BindNode>();
            foreach (BindNode node in allNodes)
            {
                if (node.Parent == null)
                {
                    rootNodes.Add(node);
                }
            }
            
            return rootNodes;
        }
        
        /// <summary>
        /// 递归生成绑定字段
        /// </summary>
        static void GenerateBindFields(List<BindNode> nodes, StringBuilder sb, string indent, 
            Dictionary<string, int> nameCount, List<BindInfo> bindInfos, int depth)
        {
            foreach (BindNode node in nodes)
            {
                UIBind bind = node.Bind;
                string originalName = bind.GetBindName();
                string bindName = originalName;
                
                // 处理重名
                if (nameCount.ContainsKey(bindName))
                {
                    nameCount[bindName]++;
                    bindName = bindName + nameCount[bindName];
                }
                else
                {
                    nameCount[bindName] = 0;
                }
                
                // 获取组件类型（优先使用自定义的ComponentType）
                string componentType = bind.GetComponentType();
                if (string.IsNullOrEmpty(componentType))
                {
                    Component component = GetMainComponent(bind.gameObject);
                    componentType = component != null ? component.GetType().Name : "GameObject";
                }
                
                BindInfo info = new BindInfo
                {
                    BindName = bindName,
                    OriginalName = originalName,
                    ComponentType = componentType,
                    Bind = bind
                };
                bindInfos.Add(info);
                
                // 如果是容器，添加注释
                if (bind.IsContainer && node.Children.Count > 0)
                {
                    sb.AppendLine($"{indent}// Container: {bindName}");
                }
                
                sb.AppendLine($"{indent}public {componentType} {bindName};");
                
                // 递归处理子节点（增加缩进以显示层级）
                if (node.Children.Count > 0)
                {
                    sb.AppendLine();
                    GenerateBindFields(node.Children, sb, indent + "    ", nameCount, bindInfos, depth + 1);
                }
            }
        }
        
        /// <summary>
        /// 收集UIBind组件，排除嵌套的UIBindParent下的UIBind
        /// </summary>
        static UIBind[] CollectUIBinds(Transform root, UIBindParent rootParent)
        {
            List<UIBind> collectedBinds = new List<UIBind>();
            // 从 root 开始，但标记这是初始调用（不收集 root 自己的 UIBind）
            CollectUIBindsRecursive(root, rootParent, collectedBinds, true);
            return collectedBinds.ToArray();
        }
        
        /// <summary>
        /// 递归收集UIBind，遇到其他UIBindParent就停止（但会收集该UIBindParent自身的UIBind）
        /// </summary>
        static void CollectUIBindsRecursive(Transform current, UIBindParent rootParent, List<UIBind> result, bool isRoot = false)
        {
            // 不收集 root 自己的 UIBind（因为 root 就是 UIBindParent 本身）
            if (!isRoot)
            {
                UIBind bind = current.GetComponent<UIBind>();
                if (bind != null)
                {
                    result.Add(bind);
                }
            }
            
            // 遍历所有子对象
            foreach (Transform child in current)
            {
                // 检查子对象是否有另一个UIBindParent
                UIBindParent childParent = child.GetComponent<UIBindParent>();
                
                // 如果子对象有UIBindParent，并且不是root本身
                if (childParent != null && childParent != rootParent)
                {
                    // 如果子对象同时有UIBind，添加它（但不继续递归）
                    UIBind childBind = child.GetComponent<UIBind>();
                    if (childBind != null)
                    {
                        result.Add(childBind);
                    }
                    // 不继续递归到这个子树
                    continue;
                }
                
                // 否则继续递归查找（递归会处理子对象的UIBind）
                CollectUIBindsRecursive(child, rootParent, result, false);
            }
        }
        
        class BindNode
        {
            public UIBind Bind;
            public BindNode Parent;
            public List<BindNode> Children = new List<BindNode>();
        }
        
        /// <summary>
        /// 获取GameObject上最主要的UI组件
        /// </summary>
        static Component GetMainComponent(GameObject obj)
        {
            // 优先级顺序
            Component component = null;
            
            // TextMeshPro组件
            component = obj.GetComponent<TMP_Text>();
            if (component != null) return component;
            
            component = obj.GetComponent<TextMeshProUGUI>();
            if (component != null) return component;
            
            component = obj.GetComponent<TMP_InputField>();
            if (component != null) return component;
            
            // Unity UI组件
            component = obj.GetComponent<Button>();
            if (component != null) return component;
            
            component = obj.GetComponent<Image>();
            if (component != null) return component;
            
            component = obj.GetComponent<RawImage>();
            if (component != null) return component;
            
            component = obj.GetComponent<Toggle>();
            if (component != null) return component;
            
            component = obj.GetComponent<Slider>();
            if (component != null) return component;
            
            component = obj.GetComponent<Scrollbar>();
            if (component != null) return component;
            
            component = obj.GetComponent<Dropdown>();
            if (component != null) return component;
            
            component = obj.GetComponent<InputField>();
            if (component != null) return component;
            
            component = obj.GetComponent<Text>();
            if (component != null) return component;
            
            // 布局组件
            component = obj.GetComponent<ScrollRect>();
            if (component != null) return component;
            
            component = obj.GetComponent<RectTransform>();
            if (component != null) return component;
            
            return null;
        }
        
        class BindInfo
        {
            public string BindName;
            public string OriginalName;
            public string ComponentType;
            public UIBind Bind;
        }
    }
}

