using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Ashlight.EditorTools
{
    /// <summary>
    /// Applies the Ashlight common GUI sprites to UI controls created from Unity's
    /// GameObject/UI menu and exposes explicit Ashlight-styled creation entries.
    /// </summary>
    [InitializeOnLoad]
    public static class AshlightCommonUIFactory
    {
        private const string SpriteRoot = "Assets/Resources/UI/CommonGUI/Sprites/";
        private const string MenuRoot = "GameObject/UI/Ashlight/";
        private static readonly HashSet<int> SuppressedAutomaticStyles = new HashSet<int>();
        private static readonly HashSet<int> KnownSelectables = new HashSet<int>();
        private static bool hierarchyScanPending;

        private enum ToggleStyle
        {
            Switch,
            Checkbox,
            Tab
        }

        static AshlightCommonUIFactory()
        {
            ObjectFactory.componentWasAdded -= OnComponentWasAdded;
            ObjectFactory.componentWasAdded += OnComponentWasAdded;
            CacheExistingSelectables();
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            EditorApplication.delayCall += () => AshlightCommonUISpriteImporter.ConfigureExistingSprites();
            EditorApplication.delayCall += RepairStyledDropdownTemplates;
        }

        [MenuItem(MenuRoot + "Button", false, 0)]
        private static void CreateButton(MenuCommand command)
        {
            GameObject control = TMP_DefaultControls.CreateButton(GetTmpResources());
            control.name = "Button_Ashlight";
            SuppressNextAutomaticStyle(control.GetComponent<Button>());
            SetSize(control, 300f, 88f);
            ApplyButton(control.GetComponent<Button>());

            TextMeshProUGUI label = control.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
            {
                label.text = "Button";
                label.fontSize = 28f;
                label.color = Color.white;
            }

            FinalizeCreation(control, command);
        }

        [MenuItem(MenuRoot + "Toggle Switch", false, 1)]
        private static void CreateToggle(MenuCommand command)
        {
            GameObject control = DefaultControls.CreateToggle(GetLegacyResources());
            control.name = "Toggle_Ashlight";
            SuppressNextAutomaticStyle(control.GetComponent<Toggle>());
            ApplyToggle(control.GetComponent<Toggle>(), ToggleStyle.Switch);
            FinalizeCreation(control, command);
        }

        [MenuItem(MenuRoot + "Checkbox", false, 2)]
        private static void CreateCheckbox(MenuCommand command)
        {
            GameObject control = DefaultControls.CreateToggle(GetLegacyResources());
            control.name = "Checkbox_Ashlight";
            SuppressNextAutomaticStyle(control.GetComponent<Toggle>());
            ApplyToggle(control.GetComponent<Toggle>(), ToggleStyle.Checkbox);
            FinalizeCreation(control, command);
        }

        [MenuItem(MenuRoot + "Tab Toggle", false, 3)]
        private static void CreateTab(MenuCommand command)
        {
            GameObject control = DefaultControls.CreateToggle(GetLegacyResources());
            control.name = "Tab_Ashlight";
            SuppressNextAutomaticStyle(control.GetComponent<Toggle>());
            ApplyToggle(control.GetComponent<Toggle>(), ToggleStyle.Tab);
            FinalizeCreation(control, command);
        }

        [MenuItem(MenuRoot + "Dropdown - TextMeshPro", false, 20)]
        private static void CreateDropdown(MenuCommand command)
        {
            GameObject control = TMP_DefaultControls.CreateDropdown(GetTmpResources());
            control.name = "Dropdown_Ashlight";
            SuppressNextAutomaticStyle(control.GetComponent<TMP_Dropdown>());
            SetSize(control, 360f, 72f);
            ApplyDropdown(control.GetComponent<TMP_Dropdown>());
            FinalizeCreation(control, command);
        }

        [MenuItem(MenuRoot + "Input Field - TextMeshPro", false, 21)]
        private static void CreateInputField(MenuCommand command)
        {
            GameObject control = TMP_DefaultControls.CreateInputField(GetTmpResources());
            control.name = "InputField_Ashlight";
            SuppressNextAutomaticStyle(control.GetComponent<TMP_InputField>());
            SetSize(control, 360f, 72f);
            ApplyInputField(control.GetComponent<TMP_InputField>());
            FinalizeCreation(control, command);
        }

        [MenuItem(MenuRoot + "Slider", false, 22)]
        private static void CreateSlider(MenuCommand command)
        {
            GameObject control = DefaultControls.CreateSlider(GetLegacyResources());
            control.name = "Slider_Ashlight";
            SuppressNextAutomaticStyle(control.GetComponent<Slider>());
            SetSize(control, 360f, 52f);
            ApplySlider(control.GetComponent<Slider>());
            FinalizeCreation(control, command);
        }

        [MenuItem(MenuRoot + "Panel", false, 40)]
        private static void CreatePanel(MenuCommand command)
        {
            GameObject panel = new GameObject(
                "Panel_Ashlight",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));

            SetSize(panel, 640f, 420f);
            Image image = panel.GetComponent<Image>();
            image.sprite = LoadSprite("Panel_Frame");
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            FinalizeCreation(panel, command);
        }

        [MenuItem(MenuRoot + "Icon Button", false, 41)]
        private static void CreateIconButton(MenuCommand command)
        {
            GameObject control = new GameObject(
                "IconButton_Ashlight",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));

            SetSize(control, 96f, 96f);
            SuppressNextAutomaticStyle(control.GetComponent<Button>());
            ApplyIconButton(control.GetComponent<Button>());
            FinalizeCreation(control, command);
        }

        [MenuItem("Tools/Ashlight UI/Reapply Style To Selection", false, 100)]
        private static void ReapplySelection()
        {
            foreach (GameObject selected in Selection.gameObjects)
            {
                foreach (Selectable selectable in selected.GetComponentsInChildren<Selectable>(true))
                    ApplyAutomaticStyle(selectable);

                Image rootImage = selected.GetComponent<Image>();
                if (rootImage != null && selected.name.IndexOf("panel", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Undo.RecordObject(rootImage, "Apply Ashlight Panel Style");
                    rootImage.sprite = LoadSprite("Panel_Frame");
                    rootImage.type = Image.Type.Sliced;
                    EditorUtility.SetDirty(rootImage);
                }
            }
        }

        [MenuItem("Tools/Ashlight UI/Refresh Common Sprite Imports", false, 101)]
        private static void RefreshSpriteImports()
        {
            AshlightCommonUISpriteImporter.ConfigureExistingSprites(true);
        }

        private static void OnComponentWasAdded(Component component)
        {
            if (component == null || EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            if (!(component is Button) &&
                !(component is Toggle) &&
                !(component is TMP_Dropdown) &&
                !(component is TMP_InputField) &&
                !(component is Slider) &&
                !(component is Dropdown) &&
                !(component is InputField))
                return;

            int instanceId = component.GetInstanceID();
            KnownSelectables.Add(instanceId);
            EditorApplication.delayCall += () =>
            {
                if (SuppressedAutomaticStyles.Remove(instanceId))
                    return;

                Component resolved = EditorUtility.InstanceIDToObject(instanceId) as Component;
                if (resolved != null)
                    ApplyAutomaticStyle(resolved);
            };
        }

        private static void CacheExistingSelectables()
        {
            foreach (Selectable selectable in UnityEngine.Resources.FindObjectsOfTypeAll<Selectable>())
                KnownSelectables.Add(selectable.GetInstanceID());
        }

        private static void OnHierarchyChanged()
        {
            if (hierarchyScanPending || EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            hierarchyScanPending = true;
            EditorApplication.delayCall += ScanForNewSelectables;
        }

        private static void ScanForNewSelectables()
        {
            hierarchyScanPending = false;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            foreach (Selectable selectable in UnityEngine.Resources.FindObjectsOfTypeAll<Selectable>())
            {
                if (selectable == null ||
                    EditorUtility.IsPersistent(selectable) ||
                    !selectable.gameObject.scene.IsValid() ||
                    !KnownSelectables.Add(selectable.GetInstanceID()))
                    continue;

                ApplyAutomaticStyle(selectable);
            }
        }

        private static void SuppressNextAutomaticStyle(Component component)
        {
            if (component == null)
                return;

            int instanceId = component.GetInstanceID();
            KnownSelectables.Add(instanceId);
            SuppressedAutomaticStyles.Add(instanceId);
            EditorApplication.delayCall += () => SuppressedAutomaticStyles.Remove(instanceId);
        }

        private static void ApplyAutomaticStyle(Component component)
        {
            switch (component)
            {
                case Button button:
                    ApplyButton(button);
                    break;
                case Toggle toggle:
                    if (toggle.GetComponentInParent<TMP_Dropdown>() != null ||
                        toggle.GetComponentInParent<Dropdown>() != null)
                        break;
                    ApplyToggle(toggle, ToggleStyle.Switch);
                    break;
                case TMP_Dropdown dropdown:
                    ApplyDropdown(dropdown);
                    break;
                case TMP_InputField inputField:
                    ApplyInputField(inputField);
                    break;
                case Slider slider:
                    ApplySlider(slider);
                    break;
                case Dropdown legacyDropdown:
                    ApplyLegacyDropdown(legacyDropdown);
                    break;
                case InputField legacyInput:
                    ApplyLegacyInputField(legacyInput);
                    break;
            }
        }

        private static void ApplyButton(Button button)
        {
            if (button == null)
                return;

            Image image = button.targetGraphic as Image ?? button.GetComponent<Image>();
            if (image == null)
                return;

            Undo.RecordObjects(new Object[] { button, image }, "Apply Ashlight Button Style");
            image.sprite = LoadSprite("Button_Normal");
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            button.targetGraphic = image;
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = MakeSpriteState(
                "Button_Hover",
                "Button_Pressed",
                "Button_Hover",
                "Button_Disabled");
            EditorUtility.SetDirty(button);
            EditorUtility.SetDirty(image);
        }

        private static void ApplyIconButton(Button button)
        {
            if (button == null)
                return;

            Image image = button.GetComponent<Image>();
            if (image == null)
                return;

            image.sprite = LoadSprite("IconButton_Normal");
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            button.targetGraphic = image;
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = MakeSpriteState(
                "IconButton_Selected",
                "IconButton_Selected",
                "IconButton_Selected",
                "IconButton_Normal");
        }

        private static void ApplyToggle(Toggle toggle, ToggleStyle style)
        {
            if (toggle == null)
                return;

            Image background = toggle.targetGraphic as Image;
            Image checkmark = toggle.graphic as Image;
            if (background == null || checkmark == null)
                return;

            string offSprite;
            string onSprite;
            Vector2 size;
            Vector2 visualSize;

            switch (style)
            {
                case ToggleStyle.Checkbox:
                    offSprite = "Checkbox_Off";
                    onSprite = "Checkbox_On";
                    size = new Vector2(220f, 52f);
                    visualSize = new Vector2(48f, 48f);
                    break;
                case ToggleStyle.Tab:
                    offSprite = "Tab_Normal";
                    onSprite = "Tab_Selected";
                    size = new Vector2(240f, 72f);
                    visualSize = size;
                    break;
                default:
                    offSprite = "Toggle_Off";
                    onSprite = "Toggle_On";
                    size = new Vector2(220f, 60f);
                    visualSize = new Vector2(104f, 56f);
                    break;
            }

            Undo.RecordObjects(new Object[] { toggle, background, checkmark }, "Apply Ashlight Toggle Style");
            toggle.transition = Selectable.Transition.None;
            background.sprite = LoadSprite(offSprite);
            background.type = style == ToggleStyle.Tab ? Image.Type.Sliced : Image.Type.Simple;
            background.preserveAspect = style != ToggleStyle.Tab;
            background.color = Color.white;

            checkmark.sprite = LoadSprite(onSprite);
            checkmark.type = style == ToggleStyle.Tab ? Image.Type.Sliced : Image.Type.Simple;
            checkmark.preserveAspect = style != ToggleStyle.Tab;
            checkmark.color = Color.white;

            RectTransform rootRect = toggle.GetComponent<RectTransform>();
            rootRect.sizeDelta = size;
            ConfigureToggleVisual(background.rectTransform, visualSize, style == ToggleStyle.Tab);
            ConfigureToggleOverlay(checkmark.rectTransform);
            ConfigureToggleLabel(toggle, visualSize.x, style);

            EditorUtility.SetDirty(toggle);
            EditorUtility.SetDirty(background);
            EditorUtility.SetDirty(checkmark);
        }

        private static void ConfigureToggleOverlay(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void ConfigureToggleVisual(RectTransform rect, Vector2 size, bool stretch)
        {
            if (stretch)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                return;
            }

            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;
        }

        private static void ConfigureToggleLabel(Toggle toggle, float visualWidth, ToggleStyle style)
        {
            RectTransform labelRect = toggle.transform.Find("Label") as RectTransform;
            if (labelRect == null)
                return;

            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = style == ToggleStyle.Tab
                ? new Vector2(20f, 8f)
                : new Vector2(visualWidth + 14f, 4f);
            labelRect.offsetMax = new Vector2(-12f, -4f);

            Text legacyLabel = labelRect.GetComponent<Text>();
            if (legacyLabel != null)
            {
                legacyLabel.text = style == ToggleStyle.Tab ? "Tab" : "Toggle";
                legacyLabel.color = Color.white;
                legacyLabel.fontSize = style == ToggleStyle.Tab ? 24 : 20;
                legacyLabel.alignment = TextAnchor.MiddleCenter;
            }
        }

        private static void ApplyDropdown(TMP_Dropdown dropdown)
        {
            if (dropdown == null)
                return;

            ApplySelectableBackground(dropdown, "Dropdown_Field");
            Image arrow = FindImage(dropdown.transform, "Arrow");
            SetSimpleSprite(arrow, "Dropdown_Arrow", true);

            RectTransform template = GetDropdownTemplate(dropdown.transform, dropdown.template);
            ApplyDropdownTemplate(template);

            Toggle itemToggle = template != null
                ? template.GetComponentInChildren<Toggle>(true)
                : null;
            if (itemToggle != null)
                ApplyDropdownItemToggle(itemToggle);
        }

        private static void ApplyDropdownItemToggle(Toggle itemToggle)
        {
            Image checkmark = itemToggle.graphic as Image;
            if (checkmark == null)
                return;

            checkmark.sprite = LoadSprite("Checkbox_On");
            checkmark.type = Image.Type.Simple;
            checkmark.preserveAspect = true;
            checkmark.color = Color.white;
            EditorUtility.SetDirty(checkmark);
        }

        private static void ApplyLegacyDropdown(Dropdown dropdown)
        {
            if (dropdown == null)
                return;

            ApplySelectableBackground(dropdown, "Dropdown_Field");
            SetSimpleSprite(FindImage(dropdown.transform, "Arrow"), "Dropdown_Arrow", true);
            ApplyDropdownTemplate(GetDropdownTemplate(dropdown.transform, dropdown.template));
        }

        private static RectTransform GetDropdownTemplate(Transform dropdownRoot, RectTransform assignedTemplate)
        {
            if (assignedTemplate != null)
                return assignedTemplate;

            return dropdownRoot.Find("Template") as RectTransform;
        }

        private static void ApplyDropdownTemplate(RectTransform templateRect)
        {
            if (templateRect == null)
                return;

            Image templateImage = templateRect.GetComponent<Image>();
            if (templateImage != null)
            {
                Undo.RecordObject(templateImage, "Apply Ashlight Dropdown Template Style");
                templateImage.sprite = LoadSprite("Panel_Frame");
                templateImage.type = Image.Type.Sliced;
                templateImage.preserveAspect = false;
                templateImage.color = Color.white;
                templateImage.raycastTarget = true;
                EditorUtility.SetDirty(templateImage);
            }

            // TMP_Dropdown expects the source template to remain disabled. It clones
            // and enables the list when the dropdown is opened.
            if (templateRect.gameObject.activeSelf)
            {
                Undo.RecordObject(templateRect.gameObject, "Disable Dropdown Template");
                templateRect.gameObject.SetActive(false);
                EditorUtility.SetDirty(templateRect.gameObject);
            }
        }

        private static void RepairStyledDropdownTemplates()
        {
            Sprite dropdownSprite = LoadSprite("Dropdown_Field");
            if (dropdownSprite == null)
                return;

            foreach (TMP_Dropdown dropdown in UnityEngine.Resources.FindObjectsOfTypeAll<TMP_Dropdown>())
            {
                if (IsSceneObject(dropdown) && HasSprite(dropdown.targetGraphic, dropdownSprite))
                    ApplyDropdown(dropdown);
            }

            foreach (Dropdown dropdown in UnityEngine.Resources.FindObjectsOfTypeAll<Dropdown>())
            {
                if (IsSceneObject(dropdown) && HasSprite(dropdown.targetGraphic, dropdownSprite))
                    ApplyLegacyDropdown(dropdown);
            }
        }

        private static bool IsSceneObject(Component component)
        {
            return component != null &&
                   !EditorUtility.IsPersistent(component) &&
                   component.gameObject.scene.IsValid();
        }

        private static bool HasSprite(Graphic graphic, Sprite sprite)
        {
            Image image = graphic as Image;
            return image != null && image.sprite == sprite;
        }

        private static void ApplyInputField(TMP_InputField inputField)
        {
            if (inputField != null)
                ApplySelectableBackground(inputField, "Input_Field");
        }

        private static void ApplyLegacyInputField(InputField inputField)
        {
            if (inputField != null)
                ApplySelectableBackground(inputField, "Input_Field");
        }

        private static void ApplySlider(Slider slider)
        {
            if (slider == null)
                return;

            Image track = FindImage(slider.transform, "Background");
            Image handle = FindImage(slider.transform, "Handle");
            SetSimpleSprite(track, "Slider_Track", true);
            SetSimpleSprite(handle, "Slider_Knob", true);
        }

        private static void ApplySelectableBackground(Selectable selectable, string spriteName)
        {
            Image image = selectable.targetGraphic as Image ?? selectable.GetComponent<Image>();
            if (image == null)
                return;

            Undo.RecordObjects(new Object[] { selectable, image }, "Apply Ashlight UI Style");
            image.sprite = LoadSprite(spriteName);
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            selectable.targetGraphic = image;
            selectable.transition = Selectable.Transition.ColorTint;
            EditorUtility.SetDirty(selectable);
            EditorUtility.SetDirty(image);
        }

        private static void SetSimpleSprite(Image image, string spriteName, bool preserveAspect)
        {
            if (image == null)
                return;

            image.sprite = LoadSprite(spriteName);
            image.type = Image.Type.Simple;
            image.preserveAspect = preserveAspect;
            image.color = Color.white;
            EditorUtility.SetDirty(image);
        }

        private static Image FindImage(Transform root, string objectName)
        {
            foreach (Image image in root.GetComponentsInChildren<Image>(true))
            {
                if (string.Equals(image.name, objectName, StringComparison.OrdinalIgnoreCase))
                    return image;
            }

            return null;
        }

        private static SpriteState MakeSpriteState(
            string highlighted,
            string pressed,
            string selected,
            string disabled)
        {
            return new SpriteState
            {
                highlightedSprite = LoadSprite(highlighted),
                pressedSprite = LoadSprite(pressed),
                selectedSprite = LoadSprite(selected),
                disabledSprite = LoadSprite(disabled)
            };
        }

        private static Sprite LoadSprite(string spriteName)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>(SpriteRoot + spriteName + ".png");
        }

        private static TMP_DefaultControls.Resources GetTmpResources()
        {
            return new TMP_DefaultControls.Resources
            {
                standard = LoadSprite("Button_Normal"),
                background = LoadSprite("Panel_Frame"),
                inputField = LoadSprite("Input_Field"),
                knob = LoadSprite("Slider_Knob"),
                checkmark = LoadSprite("Checkbox_On"),
                dropdown = LoadSprite("Dropdown_Arrow"),
                mask = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd")
            };
        }

        private static DefaultControls.Resources GetLegacyResources()
        {
            return new DefaultControls.Resources
            {
                standard = LoadSprite("Button_Normal"),
                background = LoadSprite("Panel_Frame"),
                inputField = LoadSprite("Input_Field"),
                knob = LoadSprite("Slider_Knob"),
                checkmark = LoadSprite("Checkbox_On"),
                dropdown = LoadSprite("Dropdown_Arrow"),
                mask = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd")
            };
        }

        private static void SetSize(GameObject gameObject, float width, float height)
        {
            RectTransform rect = gameObject.GetComponent<RectTransform>();
            if (rect != null)
                rect.sizeDelta = new Vector2(width, height);
        }

        private static void FinalizeCreation(GameObject control, MenuCommand command)
        {
            GameObject parent = command.context as GameObject;
            if (parent == null || parent.GetComponentInParent<Canvas>() == null)
                parent = GetOrCreateCanvas();

            GameObjectUtility.SetParentAndAlign(control, parent);
            Undo.RegisterCreatedObjectUndo(control, "Create " + control.name);
            Selection.activeGameObject = control;
        }

        private static GameObject GetOrCreateCanvas()
        {
            Canvas canvas = Object.FindObjectOfType<Canvas>();
            if (canvas != null)
                return canvas.gameObject;

            GameObject canvasObject = new GameObject(
                "Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.layer = LayerMask.NameToLayer("UI");
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            Undo.RegisterCreatedObjectUndo(canvasObject, "Create Canvas");

            if (Object.FindObjectOfType<EventSystem>() == null)
            {
                GameObject eventSystem = new GameObject(
                    "EventSystem",
                    typeof(EventSystem),
                    typeof(StandaloneInputModule));
                Undo.RegisterCreatedObjectUndo(eventSystem, "Create EventSystem");
            }

            return canvasObject;
        }
    }

    public sealed class AshlightCommonUISpriteImporter : AssetPostprocessor
    {
        private const string SpriteRoot = "Assets/Resources/UI/CommonGUI/Sprites/";
        private const string ImportMarker = "AshlightCommonGUI:v1";

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(SpriteRoot, StringComparison.OrdinalIgnoreCase))
                return;

            TextureImporter importer = (TextureImporter)assetImporter;
            ApplyImporterSettings(importer, assetPath);
        }

        public static void ConfigureExistingSprites(bool force = false)
        {
            if (!AssetDatabase.IsValidFolder(SpriteRoot.TrimEnd('/')))
                return;

            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { SpriteRoot.TrimEnd('/') });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                    continue;

                bool needsUpdate = force ||
                                   importer.textureType != TextureImporterType.Sprite ||
                                   importer.spriteImportMode != SpriteImportMode.Single ||
                                   importer.mipmapEnabled ||
                                   importer.spriteBorder != GetBorder(path) ||
                                   importer.userData.IndexOf(ImportMarker, StringComparison.Ordinal) < 0;
                if (!needsUpdate)
                    continue;

                ApplyImporterSettings(importer, path);
                importer.SaveAndReimport();
            }
        }

        private static void ApplyImporterSettings(TextureImporter importer, string path)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.spriteBorder = GetBorder(path);

            if (importer.userData.IndexOf(ImportMarker, StringComparison.Ordinal) < 0)
                importer.userData = string.IsNullOrEmpty(importer.userData)
                    ? ImportMarker
                    : importer.userData + ";" + ImportMarker;
        }

        private static Vector4 GetBorder(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (name.StartsWith("Button_", StringComparison.OrdinalIgnoreCase))
                return new Vector4(55f, 42f, 55f, 42f);
            if (name.StartsWith("Tab_", StringComparison.OrdinalIgnoreCase))
                return new Vector4(42f, 34f, 42f, 34f);
            if (name.StartsWith("IconButton_", StringComparison.OrdinalIgnoreCase))
                return new Vector4(34f, 34f, 34f, 34f);
            if (name == "Dropdown_Field" || name == "Input_Field")
                return new Vector4(45f, 34f, 45f, 34f);
            if (name == "Panel_Frame")
                return new Vector4(58f, 58f, 58f, 58f);
            return Vector4.zero;
        }
    }
}
