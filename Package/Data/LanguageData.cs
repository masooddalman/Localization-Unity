using System;
using System.Collections.Generic;

namespace PicoShot.Localization.Data
{
    /// <summary>
    /// Multi-language data structure for editor use.
    /// Represents the full localization data inside the Editor.
    /// At runtime, each language is stored separately in LocaleData/CSV files.
    /// </summary>
    [Serializable]
    public sealed class LanguageData
    {
        /// <summary>
        /// Version of the language file format.
        /// </summary>
        public int Version { get; set; } = 1;

        /// <summary>
        /// Timestamp when the file was generated.
        /// </summary>
        public long Timestamp { get; set; }

        /// <summary>
        /// The translation data.
        /// Key: Translation key (e.g., "ui.play_button")
        /// Value: Dictionary of language code -> translation value
        /// </summary>
        public Dictionary<string, Dictionary<string, string>> Translations { get; set; } = new();



        /// <summary>
        /// Gets all unique language codes in this data.
        /// </summary>
        public HashSet<string> GetAllLanguageCodes()
        {
            var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var translation in Translations.Values)
            {
                foreach (var langCode in translation.Keys)
                {
                    codes.Add(langCode);
                }
            }
            return codes;
        }
    }
}
