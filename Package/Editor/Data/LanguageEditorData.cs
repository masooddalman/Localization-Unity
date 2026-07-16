using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PicoShot.Localization.Config;

namespace PicoShot.Localization.Editor.Data
{
    public enum TranslationProvider
    {
        DeepL,
        Gemini
    }

    public enum ViewDelimiter
    {
        Dot,
        Underscore
    }

    /// <summary>
    /// Centralized data model for the Localization Editor.
    /// Holds all state, keys, languages and editor preferences.
    /// </summary>
    [Serializable]
    public sealed class LanguageEditorData
    {
        // State
        public bool HasUnsavedChanges { get; set; }
        public string SelectedKey { get; set; }
        public string SelectedView { get; set; } = "";

        // Search & Filters
        public string KeySearchFilter { get; set; } = "";
        public string LanguageFilter { get; set; } = "";
        public string ComponentSearchFilter { get; set; } = "";
        public bool SortKeysByName { get; set; }

        // Foldouts
        public bool ShowStatusSection { get; set; } = true;
        public bool ShowTestingTools { get; set; } = true;
        public bool ShowParameterList { get; set; }
        public bool ShowExistingComponents { get; set; } = true;

        // UI State
        public float KeysListPanelWidth { get; set; } = 200f;
        public Vector2 KeysListScroll { get; set; }
        public Vector2 KeyDetailsScroll { get; set; }
        public Vector2 LanguageScrollPos { get; set; }
        public Vector2 ComponentsScrollPos { get; set; }
        public Vector2 ExistingComponentsScrollPos { get; set; }
        public Vector2 MainScrollPosition { get; set; }
        public Vector2 ComponentsScrollPosition { get; set; }
        public Vector2 ToolsScrollPosition { get; set; }
        public Vector2 CharsetLanguageScrollPos { get; set; }

        // Test Data
        public string TestKey { get; set; } = "";
        public string TestRtl { get; set; } = "";
        public string TestKeyWithParams { get; set; } = "";
        public string TestResult { get; set; } = "";
        public List<string> ParameterList { get; set; } = new();

        // Component Management
        public GameObject SelectedGameObject { get; set; }
        public LocalizationTextComponent PendingKeySelection { get; set; }

        // Core Data
        public List<string> LanguageCodes { get; } = new() { "en" };
        public List<string> Keys { get; set; } = new();
        public Dictionary<string, Dictionary<string, string>> LanguageData { get; private set; } = new();
        public Dictionary<string, bool> KeyFoldouts { get; } = new();
        public Dictionary<string, bool> LanguageSelectionForCharset { get; } = new();
        public string GeneratedCharset { get; private set; } = "";
        public bool HasGeneratedCharset { get; private set; }

        // Translation Provider Settings
        public const string TranslationProviderPref = "PicoShot_Localization_TranslationProvider";
        public TranslationProvider ActiveTranslationProvider
        {
            get => (TranslationProvider)PlayerPrefs.GetInt(TranslationProviderPref, (int)TranslationProvider.DeepL);
            set => PlayerPrefs.SetInt(TranslationProviderPref, (int)value);
        }

        // Key View Settings
        public const string ViewDelimiterPref = "PicoShot_Localization_ViewDelimiter";
        public ViewDelimiter ActiveViewDelimiter
        {
            get => (ViewDelimiter)PlayerPrefs.GetInt(ViewDelimiterPref, (int)ViewDelimiter.Dot);
            set => PlayerPrefs.SetInt(ViewDelimiterPref, (int)value);
        }

        public char CurrentViewDelimiter => GetCurrentViewDelimiter();

        public static char GetCurrentViewDelimiter()
        {
            return (ViewDelimiter)PlayerPrefs.GetInt(ViewDelimiterPref, (int)ViewDelimiter.Dot) == ViewDelimiter.Underscore
                ? '_'
                : '.';
        }

        // DeepL Settings (stored in preferences, not this data)
        public const string DefaultDeeplApiUrl = "https://api-free.deepl.com/v2/translate";
        public const string DeeplApiUrlPref = "PicoShot_Localization_DeepLApiUrl";
        public const string DeeplApiKeyPref = "PicoShot_Localization_DeepLApiKey";
        public const string DeeplContextPref = "PicoShot_Localization_DeepLContext";
        public const int DeeplRequestDelayMs = 350;
        public const string DefaultDeepLContext = "This is a game localization text. The translation should be concise and suitable for game UI.";

        public string DeeplApiUrl
        {
            get => PlayerPrefs.GetString(DeeplApiUrlPref, DefaultDeeplApiUrl);
            set => PlayerPrefs.SetString(DeeplApiUrlPref, value);
        }

        public string DeeplApiKey
        {
            get => UnityEditor.EditorPrefs.GetString(DeeplApiKeyPref, "");
            set => UnityEditor.EditorPrefs.SetString(DeeplApiKeyPref, value);
        }

        public string DeeplContext
        {
            get => PlayerPrefs.GetString(DeeplContextPref, DefaultDeepLContext);
            set => PlayerPrefs.SetString(DeeplContextPref, value);
        }

        // Gemini Settings
        public const string GeminiApiKeyPref = "PicoShot_Localization_GeminiApiKey";
        public const string GeminiModelPref = "PicoShot_Localization_GeminiModel";
        public const string GeminiCustomModelPref = "PicoShot_Localization_GeminiCustomModel";
        public const string GeminiContextPref = "PicoShot_Localization_GeminiContext";

        public const string DefaultGeminiModel = "gemini-2.5-flash";
        public const string DefaultGeminiContext = "You are a specialized game localization translator. Your task is to accurately translate text for game UI, dialogues, and system messages while preserving tone and brevity. Translate the provided source text from the source language into all specified target languages. Return only a valid JSON object where keys are the target language codes and values are the translated text.";

        public string GeminiApiKey
        {
            get => UnityEditor.EditorPrefs.GetString(GeminiApiKeyPref, "");
            set => UnityEditor.EditorPrefs.SetString(GeminiApiKeyPref, value);
        }

        public string GeminiModel
        {
            get => PlayerPrefs.GetString(GeminiModelPref, DefaultGeminiModel);
            set => PlayerPrefs.SetString(GeminiModelPref, value);
        }

        public string GeminiCustomModel
        {
            get => PlayerPrefs.GetString(GeminiCustomModelPref, "");
            set => PlayerPrefs.SetString(GeminiCustomModelPref, value);
        }

        public string GeminiContext
        {
            get => PlayerPrefs.GetString(GeminiContextPref, DefaultGeminiContext);
            set => PlayerPrefs.SetString(GeminiContextPref, value);
        }

        // Constants
        public const float KeyItemHeight = 22f;
        public const float MinKeysListWidth = 150f;
        public const float MaxKeysListWidthRatio = 0.5f;

        /// <summary>
        /// Gets all keys filtered by current search and type filters.
        /// </summary>
        public IEnumerable<string> GetFilteredKeys()
        {
            string lowercaseFilter = KeySearchFilter?.ToLower() ?? "";
            bool hasFilter = !string.IsNullOrEmpty(lowercaseFilter);

            var query = Keys.AsEnumerable();

            // Filter by view first (case-insensitive)
            if (!string.IsNullOrEmpty(SelectedView))
            {
                string viewPrefix = SelectedView + CurrentViewDelimiter;
                query = query.Where(key => key.StartsWith(viewPrefix, StringComparison.OrdinalIgnoreCase));
            }

            if (hasFilter)
                query = query.Where(key => key.ToLower().Contains(lowercaseFilter));

            if (SortKeysByName)
                query = query.OrderBy(key => key);

            return query;
        }

        /// <summary>
        /// Gets all unique views from existing keys (case-insensitive, preserving first-seen casing).
        /// </summary>
        public IEnumerable<string> GetViews()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in Keys.Where(k => k.Contains(CurrentViewDelimiter)))
            {
                string view = key.Substring(0, key.IndexOf(CurrentViewDelimiter));
                if (seen.Add(view))
                    yield return view;
            }
        }

        /// <summary>
        /// Helper to extract the local name of a key by stripping its view prefix if present.
        /// </summary>
        public string GetLocalKeyName(string fullKey)
        {
            if (string.IsNullOrEmpty(fullKey)) return fullKey;
            int delimiterIndex = fullKey.IndexOf(CurrentViewDelimiter);
            return delimiterIndex >= 0 ? fullKey.Substring(delimiterIndex + 1) : fullKey;
        }

        /// <summary>
        /// Gets the first available value for a key, checking default language first.
        /// </summary>
        public string GetFirstValue(string key)
        {
            if (!LanguageData.TryGetValue(key, out var keyData) || keyData.Count == 0)
                return null;

            string defaultLang = LocalizationConfigProvider.Config.DefaultLanguage;
            if (keyData.TryGetValue(defaultLang, out var value))
                return value;

            return keyData.Values.FirstOrDefault();
        }

        /// <summary>
        /// Gets the first value from a key data dictionary.
        /// </summary>
        public static string GetFirstValue(Dictionary<string, string> keyData)
        {
            if (keyData == null || keyData.Count == 0)
                return null;

            string defaultLang = LocalizationConfigProvider.Config.DefaultLanguage;
            if (keyData.TryGetValue(defaultLang, out var value))
                return value;

            return keyData.Values.FirstOrDefault();
        }

        /// <summary>
        /// Resets all data to initial state.
        /// </summary>
        public void Reset()
        {
            HasUnsavedChanges = false;
            SelectedKey = null;
            Keys.Clear();
            LanguageData.Clear();
            KeyFoldouts.Clear();
            LanguageCodes.Clear();
            LanguageCodes.Add(LocalizationConfigProvider.Config.DefaultLanguage);
            GeneratedCharset = "";
            HasGeneratedCharset = false;
        }

        /// <summary>
        /// Adds a new language code.
        /// </summary>
        public bool AddLanguage(string language)
        {
            if (LanguageCodes.Contains(language))
                return false;

            LanguageCodes.Add(language);

            foreach (var key in Keys)
            {
                AddLanguageToKey(key, language);
            }

            HasUnsavedChanges = true;
            return true;
        }

        /// <summary>
        /// Adds a language to an existing key with appropriate default value.
        /// </summary>
        private void AddLanguageToKey(string key, string language)
        {
            LanguageData[key][language] = "";
        }

        /// <summary>
        /// Removes a language and all its translations.
        /// </summary>
        public bool RemoveLanguage(string language)
        {
            if (!LanguageCodes.Contains(language))
                return false;

            LanguageCodes.Remove(language);

            foreach (var key in Keys.Where(key => LanguageData[key].ContainsKey(language)))
            {
                LanguageData[key].Remove(language);
            }

            HasUnsavedChanges = true;
            return true;
        }

        /// <summary>
        /// Adds a new key to the data. Key names must be unique regardless of casing.
        /// </summary>
        public bool AddKey(string key)
        {
            key = key?.Trim();
            if (string.IsNullOrEmpty(key) || Keys.Any(k => k.Equals(key, StringComparison.OrdinalIgnoreCase)))
                return false;

            Keys.Add(key);
            LanguageData[key] = new Dictionary<string, string>();

            foreach (var lang in LanguageCodes)
            {
                LanguageData[key][lang] = "";
            }

            SelectedKey = key;
            HasUnsavedChanges = true;
            return true;
        }

        /// <summary>
        /// Removes a key and all its translations.
        /// </summary>
        public bool RemoveKey(string key)
        {
            if (!Keys.Contains(key))
                return false;

            Keys.Remove(key);
            LanguageData.Remove(key);
            KeyFoldouts.Remove(key);
            HasUnsavedChanges = true;
            return true;
        }

        /// <summary>
        /// Removes a view and all its keys.
        /// </summary>
        public void RemoveView(string view)
        {
            if (string.IsNullOrEmpty(view)) return;
            
            string prefix = view + CurrentViewDelimiter;
            var keysToRemove = Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
            
            foreach (var k in keysToRemove)
            {
                RemoveKey(k);
            }
            
            if (SelectedView == view)
            {
                SelectedView = "";
                SelectedKey = null;
            }
        }

        /// <summary>
        /// Renames a key while preserving its data. Key names must be unique regardless of casing.
        /// </summary>
        public bool RenameKey(string oldKey, string newKey)
        {
            newKey = newKey?.Trim();
            if (!Keys.Contains(oldKey))
                return false;

            if (Keys.Any(k => !k.Equals(oldKey, StringComparison.OrdinalIgnoreCase) &&
                               k.Equals(newKey, StringComparison.OrdinalIgnoreCase)))
                return false;

            int index = Keys.IndexOf(oldKey);
            Keys[index] = newKey;
            LanguageData[newKey] = LanguageData[oldKey];
            LanguageData.Remove(oldKey);
            SelectedKey = newKey;
            HasUnsavedChanges = true;
            return true;
        }

        /// <summary>
        /// Clears all translations for a key.
        /// </summary>
        public void ClearKeyTranslations(string key)
        {
            if (!LanguageData.TryGetValue(key, out var keyData))
                return;

            foreach (var lang in keyData.Keys.ToList())
            {
                keyData[lang] = "";
            }

            HasUnsavedChanges = true;
        }

        /// <summary>
        /// Updates charset language selection to match current languages.
        /// </summary>
        public void SyncCharsetLanguageSelection()
        {
            // Add new languages
            foreach (var lang in LanguageCodes.Where(lang => !LanguageSelectionForCharset.ContainsKey(lang)))
            {
                LanguageSelectionForCharset[lang] = false;
            }

            // Remove old languages
            var currentLanguages = new HashSet<string>(LanguageCodes);
            foreach (var lang in LanguageSelectionForCharset.Keys.ToList().Where(lang => !currentLanguages.Contains(lang)))
            {
                LanguageSelectionForCharset.Remove(lang);
            }
        }

        /// <summary>
        /// Generates one deduplicated, consistently ordered character set for all selected languages.
        /// </summary>
        public void GenerateCharset()
        {
            var charSet = new HashSet<char>();

            foreach (var lang in LanguageCodes.Where(l => LanguageSelectionForCharset[l]))
            {
                foreach (var key in Keys)
                {
                    if (LanguageData[key].TryGetValue(lang, out var value))
                        AddValueToCharset(value, charSet);
                }
            }

            GeneratedCharset = new string(charSet.OrderBy(c => c).ToArray());
            HasGeneratedCharset = true;
        }

        /// <summary>
        /// Clears the generated charset after its language selection changes.
        /// </summary>
        public void ClearGeneratedCharset()
        {
            GeneratedCharset = "";
            HasGeneratedCharset = false;
        }

        private static void AddValueToCharset(string value, HashSet<char> charSet)
        {
            if (!string.IsNullOrEmpty(value))
            {
                foreach (var c in value)
                    charSet.Add(c);
            }
        }
    }
}
