using UnityEngine.UI;
using TMPro;

namespace PicoShot.Localization
{
    public static class Extensions
    {
        public static string Localized(this string key, params object[] args)
        {
            return LocalizationManager.GetText(key, args);
        }



        /// <summary>
        /// Automatically sets the localized text on a TMP_Text component and binds it to the LocalizationTextComponent.
        /// This ensures the text updates automatically with the correct arguments when the language changes.
        /// </summary>
        public static void SetLocalizedText(this TMP_Text textComponent, string key, params object[] args)
        {
            if (textComponent == null) return;
            var loc = textComponent.GetComponent<LocalizationTextComponent>();
            if (loc == null) loc = textComponent.gameObject.AddComponent<LocalizationTextComponent>();
            
            loc.TranslationKey = key;
            if (args != null && args.Length > 0)
                loc.UpdateFormatArgs(args);
        }

        /// <summary>
        /// Automatically sets the localized text on a UI Text component and binds it to the LocalizationTextComponent.
        /// This ensures the text updates automatically with the correct arguments when the language changes.
        /// </summary>
        public static void SetLocalizedText(this Text textComponent, string key, params object[] args)
        {
            if (textComponent == null) return;
            var loc = textComponent.GetComponent<LocalizationTextComponent>();
            if (loc == null) loc = textComponent.gameObject.AddComponent<LocalizationTextComponent>();
            
            loc.TranslationKey = key;
            if (args != null && args.Length > 0)
                loc.UpdateFormatArgs(args);
        }
    }
}
