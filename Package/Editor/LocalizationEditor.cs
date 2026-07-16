using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using PicoShot.Localization.Config;
using PicoShot.Localization.Data;
using PicoShot.Localization.Editor.Data;
using PicoShot.Localization.Editor.Tabs;

namespace PicoShot.Localization
{
    /// <summary>
    /// Main editor window for managing localization data.
    /// Uses a tab-based architecture for clean separation of concerns.
    /// </summary>
    public sealed class LocalizationEditor : EditorWindow
    {
        // Data
        private LanguageEditorData _data;

        // Tabs
        private Dictionary<EditorTab, ILocalizationEditorTab> _tabs;
        private EditorTab _currentTab = EditorTab.Localization;

        private enum EditorTab
        {
            Localization,
            Keys,
            Components,
            Tools,
            Config
        }

        #region Unity Entry Points

        [MenuItem("Tools/Localization/Language Editor")]
        public static void OpenWindow()
        {
            GetWindow<LocalizationEditor>("Language Editor");
        }

        [MenuItem("Tools/Localization/Switch Language")]
        public static void SwitchLanguage()
        {
            var languages = LocalizationManager.GetAvailableLanguageCodes().OrderBy(l => l).ToList();
            if (languages.Count == 0) return;

            LocalizationManager.Initialize();

            string current = LocalizationManager.CurrentLanguage;
            int currentIndex = languages.FindIndex(l => string.Equals(l, current, System.StringComparison.OrdinalIgnoreCase));
            
            int nextIndex = (currentIndex + 1) % languages.Count;
            LocalizationManager.SetLanguage(languages[nextIndex]);
            
            if (!Application.isPlaying)
            {
                var texts = GameObject.FindObjectsOfType<LocalizationTextComponent>(true);
                foreach (var text in texts)
                {
                    text.UpdateText();
                    EditorUtility.SetDirty(text);
                }
                UnityEditor.SceneView.RepaintAll();
            }
        }

        private void OnEnable()
        {
            _data = new LanguageEditorData();
            InitializeTabs();

            LoadLanguages();

            CompilationPipeline.compilationStarted += OnBeforeCompile;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void OnDisable()
        {
            UnregisterEventHandlers();
            CompilationPipeline.compilationStarted -= OnBeforeCompile;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }

        private void OnBeforeCompile(object _)
        {
            PromptAutoSave("before compiling");
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                    PromptAutoSave("before entering Play Mode");
                    break;
                case PlayModeStateChange.ExitingPlayMode:
                    PromptAutoSave("before exiting Play Mode");
                    break;
            }
        }

        private void OnDestroy()
        {
            PromptAutoSave("before closing the editor");
        }

        private void OnGUI()
        {
            HandleKeyboardInput();

            EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));
            {
                DrawHeader();
                DrawTabs();

                EditorGUILayout.Space();

                if (_tabs.TryGetValue(_currentTab, out var tab))
                {
                    tab.Draw();
                }

                EditorGUILayout.Space();
                DrawSaveButton();
            }
            EditorGUILayout.EndVertical();
        }

        #endregion

        #region UI Components

        private static void DrawHeader()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Language Manager", EditorStyles.boldLabel);
            EditorGUILayout.Space();
        }

        private void DrawTabs()
        {
            EditorGUILayout.BeginHorizontal();

            foreach (EditorTab tab in Enum.GetValues(typeof(EditorTab)))
            {
                bool isActive = _currentTab == tab;
                GUI.backgroundColor = isActive ? Color.gray : Color.white;

                if (GUILayout.Button(GetTabDisplayName(tab), EditorStyles.toolbarButton))
                {
                    SwitchToTab(tab);
                }
            }

            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSaveButton()
        {
            EditorGUILayout.Space();
            GUI.backgroundColor = _data.HasUnsavedChanges ? Color.red : Color.white;
            if (GUILayout.Button("Save Changes", GUILayout.Height(30)))
            {
                SaveLanguages();
            }
            GUI.backgroundColor = Color.white;
        }

        #endregion

        #region Tab Management

        private void InitializeTabs()
        {
            _tabs = new Dictionary<EditorTab, ILocalizationEditorTab>
            {
                { EditorTab.Localization, new LocalizationTab(this, _data) },
                { EditorTab.Keys, new KeysTab(this, _data) },
                { EditorTab.Components, new ComponentsTab(this, _data) },
                { EditorTab.Tools, new ToolsTab(this, _data) },
                { EditorTab.Config, new ConfigTab(this, _data) }
            };
        }

        private void SwitchToTab(EditorTab newTab)
        {
            if (_currentTab == newTab) return;

            if (_tabs.TryGetValue(_currentTab, out var currentTabInstance))
            {
                currentTabInstance.OnExit();
            }

            _currentTab = newTab;

            if (_tabs.TryGetValue(newTab, out var newTabInstance))
            {
                newTabInstance.OnEnter();
            }

            GUI.FocusControl(null);
            Repaint();
        }

        private static string GetTabDisplayName(EditorTab tab)
        {
            return tab switch
            {
                EditorTab.Localization => "Localization",
                EditorTab.Keys => "Keys",
                EditorTab.Components => "Components",
                EditorTab.Tools => "Tools",
                EditorTab.Config => "Settings",
                _ => tab.ToString()
            };
        }

        #endregion

        #region Input Handling

        private void HandleKeyboardInput()
        {
            if (Event.current.type != EventType.KeyDown) return;
            if (GUIUtility.keyboardControl != 0) return;

            bool ctrlPressed = (Event.current.modifiers & EventModifiers.Control) != 0;

            if (ctrlPressed && Event.current.keyCode == KeyCode.S)
            {
                SaveLanguages();
                Event.current.Use();
                return;
            }

            if (_tabs.TryGetValue(_currentTab, out var tab))
            {
                if (tab.HandleKeyboardInput(Event.current))
                {
                    return;
                }
            }
        }

        #endregion

        #region Data Management

        /// <summary>
        /// Loads all language data from CSV files.
        /// </summary>
        private void LoadLanguages()
        {
            try
            {
                string defaultLang = LocalizationConfigProvider.Config.DefaultLanguage;

                _data.Reset();

                string filePath = Path.Combine(LocalizationManager.LanguagesPath, "translations.csv");

                if (!File.Exists(filePath))
                {
                    Debug.Log("[LocalizationEditor] translations.csv not found. Creating new data.");
                    return;
                }

                try
                {
                    var loadedData = LocaleCsvSerializer.LoadTranslations(filePath);

                    foreach (var langCode in loadedData.GetAllLanguageCodes())
                    {
                        if (!_data.LanguageCodes.Contains(langCode))
                        {
                            _data.LanguageCodes.Add(langCode);
                        }
                    }

                    foreach (var entry in loadedData.Translations)
                    {
                        string key = entry.Key;
                        var keyDict = entry.Value;

                        if (string.IsNullOrEmpty(key))
                            continue;

                        if (!_data.LanguageData.TryGetValue(key, out var editorKeyDict))
                        {
                            editorKeyDict = new Dictionary<string, string>(_data.LanguageCodes.Count);
                            _data.LanguageData[key] = editorKeyDict;
                            _data.Keys.Add(key);
                        }

                        foreach (var kvp in keyDict)
                        {
                            editorKeyDict[kvp.Key] = kvp.Value;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[LocalizationEditor] Error loading translations.csv: {ex}");
                }

                SyncMissingLanguageEntries();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LocalizationEditor] Error loading language data: {ex}");
                _data.Reset();
            }
        }

        /// <summary>
        /// Fills in missing language entries for all keys.
        /// </summary>
        private void SyncMissingLanguageEntries()
        {
            foreach (var key in _data.Keys)
            {
                var firstValue = LanguageEditorData.GetFirstValue(_data.LanguageData[key]);
                bool isArray = firstValue is List<string>;

                foreach (var lang in _data.LanguageCodes)
                {
                    if (!_data.LanguageData[key].ContainsKey(lang))
                    {
                        if (!_data.LanguageData[key].ContainsKey(lang))
                        {
                            _data.LanguageData[key][lang] = "";
                        }
                    }
                }
            }
        }
        /// <summary>
        /// Saves all language data to CSV files.
        /// </summary>
        public void SaveLanguages()
        {
            try
            {
                if (!Directory.Exists(LocalizationManager.LanguagesPath))
                {
                    Directory.CreateDirectory(LocalizationManager.LanguagesPath);
                }

                long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                var exportData = new LanguageData
                {
                    Version = 1,
                    Timestamp = timestamp,
                    Translations = new Dictionary<string, Dictionary<string, string>>()
                };

                foreach (var key in _data.Keys)
                {
                    exportData.Translations[key] = new Dictionary<string, string>();
                    foreach (var lang in _data.LanguageCodes)
                    {
                        if (_data.LanguageData[key].TryGetValue(lang, out var value))
                        {
                            exportData.Translations[key][lang] = value;
                        }
                        else
                        {
                            exportData.Translations[key][lang] = "";
                        }
                    }
                }

                string filePath = Path.Combine(LocalizationManager.LanguagesPath, "translations.csv");
                LocaleCsvSerializer.SaveTranslations(filePath, exportData);

                LocalizationConfigProvider.SaveConfig();

                _data.HasUnsavedChanges = false;
                ShowNotification(new GUIContent("Language data saved successfully!"));

                if (LocalizationManager.IsInitialized)
                {
                    LocalizationManager.Dispose();
                    LocalizationManager.Initialize();
                }
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Error", $"Failed to save language data: {ex.Message}", "OK");
                Debug.LogError($"[LocalizationEditor] Error saving language data: {ex}");
            }
        }

        /// <summary>
        /// Shows a dialog to save unsaved changes.
        /// </summary>
        private void PromptAutoSave(string context)
        {
            if (!_data.HasUnsavedChanges) return;

            if (EditorUtility.DisplayDialog("Unsaved Changes",
                    $"You have unsaved changes. Would you like to save them {context}?",
                    "Save", "Don't Save"))
            {
                SaveLanguages();
            }
            else
            {
                _data.HasUnsavedChanges = false;
            }
        }

        /// <summary>
        /// Deletes all language data permanently.
        /// </summary>
        public void PurgeAllData()
        {
            if (!EditorUtility.DisplayDialog("Purge All Data",
                    "Are you sure you want to delete all language data?\n\n" +
                    "This action cannot be undone!",
                    "Yes, Delete All", "Cancel")) return;

            var config = LocalizationConfigProvider.Config;
            string defaultLang = config.DefaultLanguage;

            _data.Reset();
            _data.LanguageCodes.Add(defaultLang);

            if (Directory.Exists(LocalizationManager.LanguagesPath))
            {
                var files = Directory.GetFiles(LocalizationManager.LanguagesPath, "*.csv");
                foreach (var file in files)
                {
                    File.Delete(file);
                }
            }

            LocalizationConfigProvider.SaveConfig();

            SaveLanguages();
            Repaint();
        }

        private static void UnregisterEventHandlers()
        {
            LocalizationManager.Dispose();
        }

        #endregion

        #region Public API for Tabs

        /// <summary>
        /// Gets the current editor data. Used by tabs.
        /// </summary>
        public LanguageEditorData GetData() => _data;

        #endregion
    }
}
