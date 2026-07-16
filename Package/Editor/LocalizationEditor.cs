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

                if (!Directory.Exists(LocalizationManager.LanguagesPath))
                {
                    Debug.Log("[LocalizationEditor] Languages directory not found. Creating new data.");
                    return;
                }

                var csvFiles = Directory.GetFiles(LocalizationManager.LanguagesPath, "*.csv", SearchOption.TopDirectoryOnly);

                foreach (var file in csvFiles)
                {
                    try
                    {
                        var localeData = LocaleCsvSerializer.LoadFile(file, out var langCode);

                        if (string.IsNullOrEmpty(langCode))
                        {
                            continue;
                        }

                        if (!LanguageDefinitions.IsValidLanguage(langCode))
                        {
                            Debug.LogError($"[LocalizationEditor] Rejecting file '{Path.GetFileName(file)}' - unsupported language code: '{langCode}'");
                            continue;
                        }

                        if (!_data.LanguageCodes.Contains(langCode))
                        {
                            _data.LanguageCodes.Add(langCode);
                        }

                        foreach (var entry in localeData.Translations)
                        {
                            string key = entry.Key;
                            object value = entry.Value;

                            if (string.IsNullOrEmpty(key))
                                continue;

                            if (!_data.LanguageData.TryGetValue(key, out var keyData))
                            {
                                keyData = new Dictionary<string, object>(_data.LanguageCodes.Count);
                                _data.LanguageData[key] = keyData;
                                _data.Keys.Add(key);
                            }

                            keyData[langCode] = value;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[LocalizationEditor] Error loading file '{file}': {ex}");
                    }
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
                        if (isArray && firstValue is List<string> list)
                        {
                            _data.LanguageData[key][lang] = new List<string>(new string[list.Count]);
                        }
                        else
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

                foreach (var lang in _data.LanguageCodes)
                {
                    var localeData = new LocaleData
                    {
                        Version = 1,
                        LanguageCode = lang,
                        Timestamp = timestamp,
                        Translations = new Dictionary<string, object>(),
                    };

                    foreach (var key in _data.Keys)
                    {
                        if (_data.LanguageData[key].TryGetValue(lang, out var value))
                        {
                            localeData.Translations[key] = value;
                        }
                    }

                    string filePath = LocalizationManager.GetLanguageFilePath(lang);
                    LocaleCsvSerializer.SaveFile(filePath, localeData);
                }

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
