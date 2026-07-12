using System;
using System.Linq;
using PicoShot.Localization.Rtl;
using UnityEditor;
using UnityEngine;
using PicoShot.Localization.Data;
using PicoShot.Localization.Editor.Data;

namespace PicoShot.Localization.Editor.Tabs
{
    /// <summary>
    /// Tab for utility tools and debugging.
    /// </summary>
    public sealed class ToolsTab : LocalizationEditorTabBase
    {
        private enum ToolsSubTab
        {
            Tools,
            Debug
        }

        private static readonly string[] SubTabNames = { "Tools", "Debug" };
        private ToolsSubTab _activeSubTab = ToolsSubTab.Tools;

        public ToolsTab(LocalizationEditor editor, LanguageEditorData data) : base(editor, data) { }

        public override string TabName => "Tools";

        public override void Draw()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));
            {
                DrawSectionHeader("Tools");
                DrawSubTabToolbar();

                EditorGUILayout.Space(5);

                using (BeginBox())
                {
                    switch (_activeSubTab)
                    {
                        case ToolsSubTab.Tools:
                            DrawToolsSubTab();
                            break;
                        case ToolsSubTab.Debug:
                            DrawDebugSubTab();
                            break;
                    }
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawSubTabToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            for (int i = 0; i < SubTabNames.Length; i++)
            {
                bool isActive = _activeSubTab == (ToolsSubTab)i;
                GUI.backgroundColor = isActive ? new Color(0.7f, 0.7f, 0.7f) : Color.white;
                if (GUILayout.Button(SubTabNames[i], EditorStyles.toolbarButton))
                {
                    _activeSubTab = (ToolsSubTab)i;
                    GUI.FocusControl(null);
                }
                GUI.backgroundColor = Color.white;
            }
            EditorGUILayout.EndHorizontal();
        }

        #region Tools Sub-Tab

        private void DrawToolsSubTab()
        {
            Data.ToolsScrollPosition = EditorGUILayout.BeginScrollView(Data.ToolsScrollPosition, GUILayout.ExpandHeight(true));

            EditorGUILayout.LabelField("Charset Generation Tool", EditorStyles.boldLabel);

            Data.SyncCharsetLanguageSelection();

            EditorGUILayout.LabelField("Select Languages:", EditorStyles.boldLabel);
            Data.CharsetLanguageScrollPos = EditorGUILayout.BeginScrollView(Data.CharsetLanguageScrollPos, GUILayout.Height(200));
            foreach (var lang in Data.LanguageCodes)
            {
                string langName = LanguageDefinitions.GetDisplayName(lang);
                bool currentState = Data.LanguageSelectionForCharset[lang];
                bool newState = EditorGUILayout.Toggle($"{langName} ({lang})", currentState);
                if (newState != currentState)
                {
                    Data.LanguageSelectionForCharset[lang] = newState;
                    Data.HasUnsavedChanges = true;
                }
            }
            EditorGUILayout.EndScrollView();

            bool anySelected = Data.LanguageSelectionForCharset.Any(kvp => kvp.Value);
            EditorGUI.BeginDisabledGroup(!anySelected);
            if (GUILayout.Button("Generate Charsets", GUILayout.Height(30)))
            {
                Data.GenerateCharsets();
            }
            EditorGUI.EndDisabledGroup();

            if (Data.GeneratedCharsets.Count > 0)
            {
                DrawGeneratedCharsets();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawGeneratedCharsets()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Generated Charsets:", EditorStyles.boldLabel);

            foreach (var kvp in Data.GeneratedCharsets)
            {
                string lang = kvp.Key;
                string charset = kvp.Value;
                string langName = LanguageDefinitions.GetDisplayName(lang);

                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField($"Language: {langName} ({lang})", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"Unique Characters: {charset.Length}");
                EditorGUILayout.SelectableLabel(charset, EditorStyles.textArea, GUILayout.Height(50));

                if (GUILayout.Button("Copy to Clipboard", GUILayout.Width(150)))
                {
                    EditorGUIUtility.systemCopyBuffer = charset;
                    Editor.ShowNotification(new GUIContent($"Charset for {langName} copied to clipboard!"));
                }

                EditorGUILayout.EndVertical();
            }
        }

        #endregion

        #region Debug Sub-Tab

        private void DrawDebugSubTab()
        {
            Data.MainScrollPosition = EditorGUILayout.BeginScrollView(Data.MainScrollPosition, GUILayout.ExpandHeight(true));

            DrawDebugHeaderBar();
            DrawDebugStatusCard();
            DrawDebugTestingCard();

            EditorGUILayout.EndScrollView();
        }

        private void DrawDebugHeaderBar()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField("Current Language:", GUILayout.Width(110));

            var currentLang = LocalizationManager.CurrentLanguage;
            var content = new GUIContent(LanguageDefinitions.GetDisplayName(currentLang ?? string.Empty));
            var dropdownRect = EditorGUILayout.GetControlRect(GUILayout.ExpandWidth(true), GUILayout.MinWidth(120));

            if (EditorGUI.DropdownButton(dropdownRect, content, FocusType.Keyboard))
            {
                ShowLanguageDropdown(dropdownRect, currentLang);
            }

            GUILayout.FlexibleSpace();

            var systemLangCode = LanguageDefinitions.FromSystemLanguage(Application.systemLanguage);
            EditorGUILayout.LabelField("System:", GUILayout.Width(50));
            EditorGUILayout.LabelField(LanguageDefinitions.GetDisplayName(systemLangCode ?? string.Empty), EditorStyles.boldLabel, GUILayout.Width(100));

            if (GUILayout.Button("Use System", GUILayout.Width(90)))
            {
                LocalizationManager.SetLanguage(LocalizationManager.DetectSystemLanguage());
                Editor.Repaint();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void ShowLanguageDropdown(Rect dropdownRect, string currentLang)
        {
            var menu = new GenericMenu();
            foreach (var lang in LocalizationManager.GetAvailableLanguageCodes())
            {
                menu.AddItem(
                    new GUIContent(LanguageDefinitions.GetDisplayName(lang)),
                    currentLang == lang,
                    () =>
                    {
                        LocalizationManager.SetLanguage(lang);
                        GUI.FocusControl(null);
                        Editor.Repaint();
                    }
                );
            }
            menu.DropDown(dropdownRect);
        }

        private void DrawDebugStatusCard()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("System Status", EditorStyles.boldLabel);

            var statusItems = new (string label, string value)[]
            {
                ("Initialized", LocalizationManager.IsInitialized.ToString()),
                ("Current Language", LocalizationManager.CurrentLanguage),
                ("Default Language", LocalizationManager.DefaultLanguage),
                ("Is RTL", LocalizationManager.IsRightToLeft.ToString()),
                ("Available Languages", LocalizationManager.GetAvailableLanguages().Count().ToString())
            };

            int columns = GetDebugColumnCount();
            float columnWidth = Mathf.Max(140f, (WindowPosition.width - 60f) / columns);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();

            int columnIndex = 0;
            for (int i = 0; i < statusItems.Length; i++)
            {
                if (columnIndex > 0 && columnIndex % columns == 0)
                {
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                }

                EditorGUILayout.BeginHorizontal(GUILayout.Width(columnWidth));
                DrawStatusField(statusItems[i].label, statusItems[i].value);
                EditorGUILayout.EndHorizontal();

                columnIndex++;
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private int GetDebugColumnCount()
        {
            float width = EditorGUIUtility.currentViewWidth;
            if (width >= 500f) return 3;
            if (width >= 350f) return 2;
            return 1;
        }

        private void DrawDebugTestingCard()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Testing Tools", EditorStyles.boldLabel);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            DrawTestRow("Simple Lookup", Data.TestKey, (value) => Data.TestKey = value, () =>
            {
                Data.TestResult = LocalizationManager.GetText(Data.TestKey);
            });

            EditorGUILayout.Space(8);

            DrawTestRow("RTL Fix", Data.TestRtl, (value) => Data.TestRtl = value, () =>
            {
                Data.TestResult = RtlTextHandler.Fix(Data.TestRtl);
            });

            EditorGUILayout.Space(8);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Parameterized", GUILayout.Width(90));
            Data.TestKeyWithParams = EditorGUILayout.TextField(Data.TestKeyWithParams);
            EditorGUILayout.EndHorizontal();

            DrawParameterList();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUI.enabled = !string.IsNullOrEmpty(Data.TestKeyWithParams);
            if (GUILayout.Button("Test with Parameters", GUILayout.Width(150), GUILayout.Height(24)))
            {
                Data.TestResult = LocalizationManager.GetText(Data.TestKeyWithParams, Data.ParameterList.ToArray());
                GUI.FocusControl(null);
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(Data.TestResult))
            {
                DrawDebugResultCard();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawTestRow(string label, string value, System.Action<string> onValueChanged, System.Action onTest)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent(label, $"Enter the value to test {label.ToLower()}"), GUILayout.Width(90));
            var newValue = EditorGUILayout.TextField(value);
            if (newValue != value)
            {
                onValueChanged?.Invoke(newValue);
            }

            GUI.enabled = !string.IsNullOrEmpty(value);
            if (GUILayout.Button("Test", GUILayout.Width(60)))
            {
                onTest?.Invoke();
                GUI.FocusControl(null);
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        private void DrawParameterList()
        {
            EditorGUILayout.Space(4);
            Data.ShowParameterList = EditorGUILayout.Foldout(Data.ShowParameterList, $"Parameters ({Data.ParameterList.Count})", true);
            if (!Data.ShowParameterList) return;

            EditorGUI.indentLevel++;
            for (var i = 0; i < Data.ParameterList.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                Data.ParameterList[i] = EditorGUILayout.TextField($"Param {i}", Data.ParameterList[i]);
                if (GUILayout.Button("-", GUILayout.Width(24)))
                {
                    Data.ParameterList.RemoveAt(i);
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUI.indentLevel--;

            if (GUILayout.Button("Add Parameter", GUILayout.Width(120)))
            {
                Data.ParameterList.Add("");
            }
        }

        private void DrawDebugResultCard()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.SelectableLabel(Data.TestResult, EditorStyles.wordWrappedLabel, GUILayout.Height(50));

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Copy", GUILayout.Width(60)))
            {
                EditorGUIUtility.systemCopyBuffer = Data.TestResult;
                Editor.ShowNotification(new GUIContent("Copied to clipboard!"));
            }

            if (GUILayout.Button("Clear", GUILayout.Width(60)))
            {
                Data.TestResult = "";
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private static void DrawStatusField(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label + ":", EditorStyles.miniLabel, GUILayout.Width(95));

            GUI.color = Color.cyan;
            EditorGUILayout.LabelField(value ?? "-", EditorStyles.boldLabel);
            GUI.color = Color.white;
            EditorGUILayout.EndHorizontal();
        }

        #endregion
    }
}
