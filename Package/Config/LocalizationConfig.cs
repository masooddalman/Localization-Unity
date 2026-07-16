using System;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

namespace PicoShot.Localization.Config
{


    /// <summary>
    /// Configuration asset for localization system.
    /// </summary>
    public class LocalizationConfig : ScriptableObject
    {
        [Tooltip("Default language code to use when system language is not available")]
        [SerializeField]
        private string _defaultLanguage = "en";

        [Header("Text Processing")]
        [Tooltip("Enable dynamic reshaping and bi-directional support for mixed LTR/RTL text")]
        [SerializeField]
        private bool _supportMixedText = false;



        [Header("Font System")]
        [Tooltip("Enable language-specific fonts system")]
        [SerializeField]
        private bool _isFontSystemEnabled = false;

        [Tooltip("Default TextMeshPro font asset")]
        [SerializeField]
        private TMP_FontAsset _defaultTMPFont;

        [Tooltip("Default Legacy Text font")]
        [SerializeField]
        private Font _defaultLegacyFont;

        [Tooltip("Font overrides per language")]
        [SerializeField]
        private List<LanguageFontMapping> _fontMappings = new();

        /// <summary>
        /// Default language code.
        /// </summary>
        public string DefaultLanguage => _defaultLanguage;

        /// <summary>
        /// Whether mixed LTR/RTL text parsing and bi-directional reshaping is enabled.
        /// </summary>
        public bool SupportMixedText => _supportMixedText;



        /// <summary>
        /// Whether the font system is enabled.
        /// </summary>
        public bool IsFontSystemEnabled => _isFontSystemEnabled;

        /// <summary>
        /// Gets the default TMP Font.
        /// </summary>
        public TMP_FontAsset DefaultTMPFont => _defaultTMPFont;

        /// <summary>
        /// Gets the default Legacy Font.
        /// </summary>
        public Font DefaultLegacyFont => _defaultLegacyFont;

        /// <summary>
        /// Gets font mappings per language.
        /// </summary>
        public IReadOnlyList<LanguageFontMapping> FontMappings => _fontMappings;



        #region Editor Only

#if UNITY_EDITOR

        /// <summary>
        /// Sets the default language (Editor only).
        /// </summary>
        public void SetDefaultLanguage(string langCode)
        {
            if (string.IsNullOrEmpty(langCode) || _defaultLanguage == langCode) return;
            _defaultLanguage = langCode;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        public void SetSupportMixedText(bool support)
        {
            if (_supportMixedText == support) return;
            _supportMixedText = support;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }



        public void SetFontSystemEnabled(bool enabled)
        {
            _isFontSystemEnabled = enabled;
        }

        public void SetDefaultFonts(TMP_FontAsset tmpFont, Font legacyFont)
        {
            _defaultTMPFont = tmpFont;
            _defaultLegacyFont = legacyFont;
        }

        public void SetFontMapping(string languageCode, TMP_FontAsset tmpFont, Font legacyFont)
        {
            for (int i = 0; i < _fontMappings.Count; i++)
            {
                if (_fontMappings[i].languageCode.Equals(languageCode, StringComparison.OrdinalIgnoreCase))
                {
                    _fontMappings[i] = new LanguageFontMapping
                    {
                        languageCode = languageCode,
                        tmpFont = tmpFont,
                        legacyFont = legacyFont
                    };
                    return;
                }
            }
            
            _fontMappings.Add(new LanguageFontMapping
            {
                languageCode = languageCode,
                tmpFont = tmpFont,
                legacyFont = legacyFont
            });
        }

        public void RemoveFontMapping(string languageCode)
        {
            _fontMappings.RemoveAll(m => m.languageCode.Equals(languageCode, StringComparison.OrdinalIgnoreCase));
        }

#endif

        #endregion
    }

    /// <summary>
    /// Serializable mapping of language to specific fonts.
    /// </summary>
    [Serializable]
    public struct LanguageFontMapping
    {
        public string languageCode;
        public TMP_FontAsset tmpFont;
        public Font legacyFont;
    }
}
