using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using TMPro;
using PicoShot.Localization.Editor.Data;
using PicoShot.Localization.Editor.Services;
using PicoShot.Localization.Data;
using PicoShot.Localization.Rtl;
using PicoShot.Localization.Config;

namespace PicoShot.Localization.Editor.Inspectors
{
    [CustomEditor(typeof(LocalizationTextComponent))]
    public class LocalizationTextComponentEditor : UnityEditor.Editor
    {
        private SerializedProperty _translationKeyProp;
        private SerializedProperty _arrayIndexProp;
        private SerializedProperty _arraySizeLimitProp;
        private SerializedProperty _formatParametersProp;
        private SerializedProperty _onTextUpdatedProp;
        private SerializedProperty _styleOverridesProp;

        private string _selectedView = "All Keys";
        private List<string> _availableViews = new List<string>();
        private List<string> _availableKeys = new List<string>();
        private List<string> _displayKeys = new List<string>();
        
        private bool _isDataInitialized = false;
        private char _lastViewDelimiter;

        // AI PREVIEW FIELDS
        private bool _showPreviewSection = true;
        private int _selectedLanguageIndex = 0;
        private int _previewLength = -1;
        private string[] _availableLanguages;
        private bool _isTranslating = false;
        private string _lastPreviewLang = "";

        private static char GetViewDelimiter() => LanguageEditorData.GetCurrentViewDelimiter();

        private void OnEnable()
        {
            _translationKeyProp = serializedObject.FindProperty("translationKey");
            _arrayIndexProp = serializedObject.FindProperty("arrayIndex");
            _arraySizeLimitProp = serializedObject.FindProperty("arraySizeLimit");
            _formatParametersProp = serializedObject.FindProperty("formatParameters");
            _onTextUpdatedProp = serializedObject.FindProperty("onTextUpdated");
            _styleOverridesProp = serializedObject.FindProperty("StyleOverrides");
            
            RefreshData();
            SyncSelectedViewWithKey();
        }

        private void SyncSelectedViewWithKey()
        {
            if (_translationKeyProp == null) return;
            string fullKey = _translationKeyProp.stringValue;
            char delimiter = GetViewDelimiter();
            
            if (!string.IsNullOrEmpty(fullKey) && fullKey.Contains(delimiter))
            {
                string view = fullKey.Substring(0, fullKey.IndexOf(delimiter)).ToLowerInvariant();
                if (_availableViews.Any(v => v.Equals(view, StringComparison.OrdinalIgnoreCase)))
                {
                    _selectedView = _availableViews.First(v => v.Equals(view, StringComparison.OrdinalIgnoreCase));
                    UpdateDisplayKeys();
                }
            }
        }

        private void RefreshData()
        {
            var keys = LocalizationManager.AllTranslationKeys.ToList();
            
            // Extract views
            char delimiter = GetViewDelimiter();
            var views = keys.Where(k => k.Contains(delimiter))
                             .Select(k => k.Substring(0, k.IndexOf(delimiter)))
                             .Distinct()
                             .OrderBy(t => t)
                             .ToList();
                             
            _availableViews.Clear();
            _availableViews.Add("All Keys");
            _availableViews.AddRange(views);

            if (!_availableViews.Contains(_selectedView))
                _selectedView = "All Keys";

            _availableKeys = keys.OrderBy(k => k).ToList();
            UpdateDisplayKeys();
            _lastViewDelimiter = delimiter;
            
            _isDataInitialized = true;
        }

        private void UpdateDisplayKeys()
        {
            _displayKeys.Clear();
            if (_selectedView == "All Keys")
            {
                _displayKeys.AddRange(_availableKeys);
            }
            else
            {
                string prefix = _selectedView + GetViewDelimiter();
                var filtered = _availableKeys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
                foreach(var key in filtered)
                {
                    _displayKeys.Add(key.Substring(prefix.Length));
                }
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var component = (LocalizationTextComponent)target;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Localization Key", EditorStyles.boldLabel);
            if (GUILayout.Button("Refresh Keys", EditorStyles.miniButton, GUILayout.Width(80)))
            {
                LocalizationManager.Initialize(); 
                RefreshData();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            if (!_isDataInitialized || _lastViewDelimiter != GetViewDelimiter())
            {
                RefreshData();
                SyncSelectedViewWithKey();
            }

            // --- VIEW SELECTION ---
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("View");
            
            var viewRect = EditorGUILayout.GetControlRect(GUILayout.ExpandWidth(true));
            int viewIndex = _availableViews.IndexOf(_selectedView);
            if (viewIndex < 0) viewIndex = 0;
            
            if (EditorGUI.DropdownButton(viewRect, new GUIContent(_availableViews[viewIndex]), FocusType.Keyboard, EditorStyles.popup))
            {
                LocalizationSearchablePopup.Show(viewRect, _availableViews.ToArray(), viewIndex, (index) =>
                {
                    if (index >= 0 && index < _availableViews.Count && _selectedView != _availableViews[index])
                    {
                        _selectedView = _availableViews[index];
                        UpdateDisplayKeys();
                        
                        // Clear the selected key when view changes
                        _translationKeyProp.stringValue = "";
                        serializedObject.ApplyModifiedProperties();
                        
                        if (!Application.isPlaying)
                        {
                            component.TranslationKey = "";
                            EditorUtility.SetDirty(component);
                        }

                        Repaint();
                    }
                });
            }
            EditorGUILayout.EndHorizontal();

            // --- KEY SELECTION ---
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Translation Key");

            var keyRect = EditorGUILayout.GetControlRect(GUILayout.ExpandWidth(true));
            
            // Determine current key display
            string currentFullKey = _translationKeyProp.stringValue ?? "";
            string currentDisplayKey = currentFullKey;
            
            if (_selectedView != "All Keys" && currentFullKey.StartsWith(_selectedView + GetViewDelimiter(), StringComparison.OrdinalIgnoreCase))
            {
                currentDisplayKey = currentFullKey.Substring(_selectedView.Length + 1);
            }
            else if (_selectedView != "All Keys" && !string.IsNullOrEmpty(currentFullKey))
            {
                currentDisplayKey = $"[{currentFullKey}]";
            }
            
            if (string.IsNullOrEmpty(currentDisplayKey))
            {
                currentDisplayKey = "Select a key...";
            }

            int keyIndex = _displayKeys.IndexOf(currentDisplayKey);

            if (EditorGUI.DropdownButton(keyRect, new GUIContent(currentDisplayKey), FocusType.Keyboard, EditorStyles.popup))
            {
                LocalizationSearchablePopup.Show(keyRect, _displayKeys.ToArray(), keyIndex, (index) =>
                {
                    if (index >= 0 && index < _displayKeys.Count)
                    {
                        string newLocalKey = _displayKeys[index];
                        string newFullKey = _selectedView == "All Keys" ? newLocalKey : $"{_selectedView}{GetViewDelimiter()}{newLocalKey}";
                        
                        _translationKeyProp.stringValue = newFullKey;
                        serializedObject.ApplyModifiedProperties();
                        
                        // Force update text preview in Edit Mode.
                        if (!Application.isPlaying)
                        {
                            component.UpdateText();
                            EditorUtility.SetDirty(component);
                        }
                    }
                });
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
            
            // --- REMAINING PROPERTIES ---
            EditorGUILayout.PropertyField(_arrayIndexProp);
            EditorGUILayout.PropertyField(_arraySizeLimitProp);
            
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(_formatParametersProp, true);
            
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(_onTextUpdatedProp);

            EditorGUILayout.Space();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(_styleOverridesProp, true);
            bool styleOverridesChanged = EditorGUI.EndChangeCheck();

            DrawPreviewSection(component, styleOverridesChanged);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawPreviewSection(LocalizationTextComponent component, bool styleOverridesChanged)
        {
            if (Application.isPlaying || string.IsNullOrEmpty(component.TranslationKey))
                return;

            var tmpText = component.GetComponent<TMP_Text>();
            if (tmpText == null) return;

            EditorGUILayout.Space();
            _showPreviewSection = EditorGUILayout.Foldout(_showPreviewSection, "Preview & Fit (AI Rephrase)", true, EditorStyles.foldoutHeader);

            if (_showPreviewSection)
            {
                EditorGUILayout.BeginVertical("box");
                
                var editorWindow = Resources.FindObjectsOfTypeAll<LocalizationEditor>().FirstOrDefault();
                if (editorWindow == null)
                {
                    EditorGUILayout.HelpBox("Please keep the 'Language Editor' window open to use the AI Rephrase feature.", MessageType.Warning);
                    if (GUILayout.Button("Open Language Editor"))
                    {
                        LocalizationEditor.OpenWindow();
                    }
                    EditorGUILayout.EndVertical();
                    return;
                }

                var editorData = editorWindow.GetData();
                if (editorData == null || !editorData.LanguageData.ContainsKey(component.TranslationKey))
                {
                    EditorGUILayout.HelpBox("Key not found in localization data. Please add it in the Localization Editor first.", MessageType.Warning);
                    EditorGUILayout.EndVertical();
                    return;
                }

                _availableLanguages = editorData.LanguageCodes.ToArray();
                if (_availableLanguages == null || _availableLanguages.Length == 0)
                {
                    EditorGUILayout.EndVertical();
                    return;
                }

                _selectedLanguageIndex = EditorGUILayout.Popup("Preview Language", _selectedLanguageIndex, _availableLanguages);
                if (_selectedLanguageIndex < 0 || _selectedLanguageIndex >= _availableLanguages.Length)
                {
                    _selectedLanguageIndex = 0;
                }
                
                string targetLang = _availableLanguages[_selectedLanguageIndex];

                bool langChanged = _lastPreviewLang != targetLang;

                if (langChanged)
                {
                    _previewLength = -1;
                    _lastPreviewLang = targetLang;
                }

                editorData.LanguageData[component.TranslationKey].TryGetValue(targetLang, out string fullTranslationText);
                fullTranslationText ??= string.Empty;

                if (string.IsNullOrEmpty(fullTranslationText))
                {
                    EditorGUILayout.HelpBox($"No translation found for {targetLang}.", MessageType.Info);
                    EditorGUILayout.EndVertical();
                    return;
                }

                if (_previewLength == -1 || _previewLength > fullTranslationText.Length)
                {
                    _previewLength = fullTranslationText.Length;
                }

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Character Limit:", GUILayout.Width(100));
                
                EditorGUI.BeginChangeCheck();
                _previewLength = EditorGUILayout.IntSlider(_previewLength, 0, fullTranslationText.Length);
                bool sliderChanged = EditorGUI.EndChangeCheck();
                EditorGUILayout.EndHorizontal();

                string previewSubstring = fullTranslationText.Substring(0, _previewLength);
                
                if (LanguageDefinitions.IsRightToLeft(targetLang))
                {
                    if (LocalizationConfigProvider.Config.SupportMixedText)
                        previewSubstring = RtlTextHandler.FixMixed(previewSubstring, true);
                    else
                        previewSubstring = RtlTextHandler.Fix(previewSubstring);
                }

                if (tmpText.text != previewSubstring || styleOverridesChanged || langChanged)
                {
                    Undo.RecordObject(tmpText, "Update TMP Preview");
                    tmpText.text = previewSubstring;
                    component.ApplyStyleOverrides(targetLang);
                    EditorUtility.SetDirty(tmpText);
                }

                EditorGUILayout.Space();
                
                if (_previewLength < fullTranslationText.Length)
                {
                    EditorGUILayout.HelpBox($"Currently truncating text by {fullTranslationText.Length - _previewLength} characters.", MessageType.Warning);
                }

                EditorGUI.BeginDisabledGroup(_isTranslating || _previewLength == fullTranslationText.Length);
                if (GUILayout.Button(_isTranslating ? "Rephrasing..." : $"Rephrase '{targetLang}' to fit {_previewLength} chars limit", GUILayout.Height(30)))
                {
                    RephraseAction(editorWindow, editorData, component.TranslationKey, targetLang, _previewLength, tmpText);
                }
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.EndVertical();
            }
        }

        private async void RephraseAction(LocalizationEditor editorWindow, LanguageEditorData editorData, string key, string targetLang, int maxCharacters, TMP_Text tmpText)
        {
            _isTranslating = true;
            Repaint();

            try
            {
                var translationService = new TranslationService(editorData);
                await translationService.RephraseWithConstraintAsync(key, targetLang, maxCharacters);
                
                if (editorData.LanguageData.TryGetValue(key, out var keyDict) && keyDict.TryGetValue(targetLang, out var newText))
                {
                    string shapedText = newText;
                    if (LanguageDefinitions.IsRightToLeft(targetLang))
                    {
                        if (LocalizationConfigProvider.Config.SupportMixedText)
                            shapedText = RtlTextHandler.FixMixed(shapedText, true);
                        else
                            shapedText = RtlTextHandler.Fix(shapedText);
                    }

                    Undo.RecordObject(tmpText, "Apply AI Rephrase");
                    tmpText.text = shapedText;
                    EditorUtility.SetDirty(tmpText);
                }

                editorWindow.Repaint();
                _previewLength = -1;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Rephrase failed: {ex.Message}");
            }
            finally
            {
                _isTranslating = false;
                Repaint();
            }
        }
    }
    [CustomPropertyDrawer(typeof(MarginAttribute))]
    public class MarginAttributeDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType == SerializedPropertyType.Vector4)
            {
                EditorGUI.BeginProperty(position, label, property);
                
                position = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);

                var labels = new[] { new GUIContent("Left"), new GUIContent("Top"), new GUIContent("Right"), new GUIContent("Bottom") };
                var values = new float[] { property.vector4Value.x, property.vector4Value.y, property.vector4Value.z, property.vector4Value.w };

                EditorGUI.MultiFloatField(position, labels, values);

                property.vector4Value = new Vector4(values[0], values[1], values[2], values[3]);

                EditorGUI.EndProperty();
            }
            else
            {
                EditorGUI.PropertyField(position, property, label);
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight;
        }
    }
}
