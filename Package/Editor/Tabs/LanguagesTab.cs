using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using TMPro;
using PicoShot.Localization.Config;
using PicoShot.Localization.Data;
using PicoShot.Localization.Editor.Data;

namespace PicoShot.Localization.Editor.Tabs
{
    /// <summary>
    /// Tab for managing available languages and language-specific fonts.
    /// </summary>
    public sealed class LanguagesTab : LocalizationEditorTabBase
    {
        private enum LanguagesSubTab
        {
            Languages,
            Fonts
        }

        private static readonly string[] SubTabNames = { "Languages", "Fonts" };
        private LanguagesSubTab _activeSubTab = LanguagesSubTab.Languages;

        private Vector2 _scrollPos;
        private Vector2 _fontsScrollPos;

        public LanguagesTab(LocalizationEditor editor, LanguageEditorData data) : base(editor, data) { }

        public override string TabName => "Languages";

        public override void Draw()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));
            {
                DrawSectionHeader("Languages");
                DrawSubTabToolbar();

                EditorGUILayout.Space(5);

                using (BeginBox())
                {
                    switch (_activeSubTab)
                    {
                        case LanguagesSubTab.Languages:
                            DrawLanguagesSubTab();
                            break;
                        case LanguagesSubTab.Fonts:
                            DrawFontsSubTab();
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
                bool isActive = _activeSubTab == (LanguagesSubTab)i;
                GUI.backgroundColor = isActive ? new Color(0.7f, 0.7f, 0.7f) : Color.white;
                if (GUILayout.Button(SubTabNames[i], EditorStyles.toolbarButton))
                {
                    _activeSubTab = (LanguagesSubTab)i;
                    GUI.FocusControl(null);
                }
                GUI.backgroundColor = Color.white;
            }
            EditorGUILayout.EndHorizontal();
        }

        #region Languages Sub-Tab

        private void DrawLanguagesSubTab()
        {
            DrawSearchField();
            EditorGUILayout.Space();
            DrawLanguageList();
            DrawSummary();
        }

        private void DrawSearchField()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Search:", GUILayout.Width(50));
            Data.LanguageFilter = EditorGUILayout.TextField(Data.LanguageFilter);

            if (GUILayout.Button("Clear", GUILayout.Width(60)))
            {
                Data.LanguageFilter = "";
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawLanguageList()
        {
            EditorGUILayout.LabelField("Available Languages:", EditorStyles.boldLabel);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.ExpandHeight(true));

            var filteredLanguages = LanguageDefinitions.LanguageNames
                .Where(lang => string.IsNullOrEmpty(Data.LanguageFilter) ||
                               lang.Value.ToLower().Contains(Data.LanguageFilter.ToLower()) ||
                               lang.Key.ToLower().Contains(Data.LanguageFilter.ToLower()))
                .OrderBy(lang => lang.Value);

            string defaultLang = LocalizationConfigProvider.Config.DefaultLanguage;

            foreach (var lang in filteredLanguages)
            {
                DrawLanguageItem(lang.Key, lang.Value, defaultLang);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawLanguageItem(string code, string name, string defaultLang)
        {
            EditorGUILayout.BeginHorizontal("box");

            bool isDefault = code == defaultLang;
            bool isSelected = Data.LanguageCodes.Contains(code);

            if (isDefault)
            {
                EditorGUI.BeginDisabledGroup(true);
                _ = EditorGUILayout.Toggle(true, GUILayout.Width(20));
                EditorGUI.EndDisabledGroup();
            }
            else
            {
                bool newSelection = EditorGUILayout.Toggle(isSelected, GUILayout.Width(20));
                if (newSelection != isSelected)
                {
                    if (newSelection)
                        OnLanguageAdded(code);
                    else
                        OnLanguageRemoved(code);
                }
            }

            string label = isDefault
                ? $"{name} ({code}) - Default"
                : $"{name} ({code})";

            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSummary()
        {
            EditorGUILayout.LabelField($"Selected Languages: {Data.LanguageCodes.Count}", EditorStyles.boldLabel);
        }

        private void OnLanguageAdded(string language)
        {
            if (Data.AddLanguage(language))
            {
                var config = LocalizationConfigProvider.Config;
                config.AddSelectedLanguage(language);
                LocalizationConfigProvider.SaveConfig();
                Editor.Repaint();
            }
        }

        private void OnLanguageRemoved(string language)
        {
            if (Data.RemoveLanguage(language))
            {
                string filePath = LocalizationManager.GetLanguageFilePath(language);
                if (System.IO.File.Exists(filePath))
                    System.IO.File.Delete(filePath);

                var config = LocalizationConfigProvider.Config;
                config.RemoveSelectedLanguage(language);
                LocalizationConfigProvider.SaveConfig();

                Editor.Repaint();
            }
        }

        #endregion

        #region Fonts Sub-Tab

        private void DrawFontsSubTab()
        {
            var config = LocalizationConfigProvider.Config;

            if (!config.IsFontSystemEnabled)
            {
                DrawDisabledFontState(config);
                return;
            }

            _fontsScrollPos = EditorGUILayout.BeginScrollView(_fontsScrollPos, GUILayout.ExpandHeight(true));

            DrawSectionHeader("Font System");

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Disable Font System", GUILayout.Width(150)))
            {
                config.SetFontSystemEnabled(false);
                LocalizationConfigProvider.SaveConfig();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            DrawDefaultFontsSection(config);

            EditorGUILayout.Space();
            DrawLanguageFontsSection(config);

            EditorGUILayout.EndScrollView();
        }

        private void DrawDisabledFontState(LocalizationConfig config)
        {
            GUILayout.FlexibleSpace();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            EditorGUILayout.BeginVertical(GUILayout.Width(300));
            EditorGUILayout.LabelField("Language-Specific Fonts", EditorStyles.boldLabel, GUILayout.Height(30));
            EditorGUILayout.HelpBox("The Font System is currently disabled. Enable it to assign specific fonts to different languages, which will automatically update UI text components when the language changes.", MessageType.Info);

            EditorGUILayout.Space();

            GUI.backgroundColor = new Color(0.2f, 0.8f, 0.2f);
            if (GUILayout.Button("Enable Font System", GUILayout.Height(40)))
            {
                config.SetFontSystemEnabled(true);
                LocalizationConfigProvider.SaveConfig();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndVertical();

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();
        }

        private void DrawDefaultFontsSection(LocalizationConfig config)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Default Fonts (Fallback)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("These fonts will be used if a specific language does not have an override.", MessageType.None);

            EditorGUI.BeginChangeCheck();

            var newTMP = (TMP_FontAsset)EditorGUILayout.ObjectField("Default TMP Font", config.DefaultTMPFont, typeof(TMP_FontAsset), false);
            var newLegacy = (Font)EditorGUILayout.ObjectField("Default Legacy Font", config.DefaultLegacyFont, typeof(Font), false);

            if (EditorGUI.EndChangeCheck())
            {
                config.SetDefaultFonts(newTMP, newLegacy);
                LocalizationConfigProvider.SaveConfig();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawLanguageFontsSection(LocalizationConfig config)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Language Overrides", EditorStyles.boldLabel);

            var activeLanguages = Data.LanguageCodes;

            if (activeLanguages.Count == 0)
            {
                EditorGUILayout.HelpBox("No languages found. Add languages in the Languages sub-tab.", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            var mappings = config.FontMappings.ToList();

            foreach (var langCode in activeLanguages)
            {
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField($"Language: {langCode}", EditorStyles.boldLabel);

                var mapping = mappings.FirstOrDefault(m => m.languageCode.Equals(langCode, StringComparison.OrdinalIgnoreCase));

                EditorGUI.BeginChangeCheck();

                var newTMP = (TMP_FontAsset)EditorGUILayout.ObjectField("TMP Font", mapping.tmpFont, typeof(TMP_FontAsset), false);
                var newLegacy = (Font)EditorGUILayout.ObjectField("Legacy Font", mapping.legacyFont, typeof(Font), false);

                if (EditorGUI.EndChangeCheck())
                {
                    config.SetFontMapping(langCode, newTMP, newLegacy);
                    LocalizationConfigProvider.SaveConfig();
                }

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndVertical();
        }

        #endregion
    }
}
