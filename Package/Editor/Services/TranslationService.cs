using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using PicoShot.Localization.Config;
using PicoShot.Localization.Editor.Data;

namespace PicoShot.Localization.Editor.Services
{
    /// <summary>
    /// Service for translating text using DeepL API.
    /// </summary>
    public sealed class TranslationService
    {
        private readonly LanguageEditorData _data;
        private readonly HttpClient _httpClient;

        public TranslationService(LanguageEditorData data)
        {
            _data = data;
            _httpClient = new HttpClient();
        }

        /// <summary>
        /// Translates a key into all missing target languages.
        /// </summary>
        public async Task TranslateAndFill(string key)
        {
            if (!_data.LanguageData.TryGetValue(key, out var keyData))
                return;

            string defaultLang = LocalizationConfigProvider.Config.DefaultLanguage;

            await TranslateAndFillString(key, keyData, defaultLang);
        }

        /// <summary>
        /// Translates a string value for the given key.
        /// </summary>
        private async Task TranslateAndFillString(string key, Dictionary<string, string> keyData, string defaultLang)
        {
            string sourceText = null;
            string sourceLang = defaultLang;

            if (keyData.TryGetValue(defaultLang, out var defaultValue) && !string.IsNullOrWhiteSpace(defaultValue))
            {
                sourceText = defaultValue;
            }
            else
            {
                foreach (var kvp in keyData)
                {
                    if (!string.IsNullOrWhiteSpace(kvp.Value))
                    {
                        sourceText = kvp.Value;
                        sourceLang = kvp.Key;
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(sourceText))
            {
                Debug.LogWarning($"The source text is empty for key '{key}', source text must be set to translate.");
                return;
            }

            var targetLanguages = _data.LanguageCodes.Where(l => l != sourceLang && string.IsNullOrWhiteSpace(keyData[l])).ToList();

            if (targetLanguages.Count == 0)
                return;

            if (_data.ActiveTranslationProvider == TranslationProvider.Gemini)
            {
                await TranslateWithGeminiBatchAsync(sourceText, sourceLang, targetLanguages, keyData);
                return;
            }

            foreach (var lang in targetLanguages)
            {
                if (!string.IsNullOrWhiteSpace(keyData[lang]))
                    continue;

                try
                {
                    var translated = await TranslateText(sourceText, sourceLang, lang);
                    if (!string.IsNullOrEmpty(translated))
                    {
                        keyData[lang] = translated;
                        _data.HasUnsavedChanges = true;
                    }

                    await Task.Delay(LanguageEditorData.DeeplRequestDelayMs);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Translation error for {lang}: {ex.Message}");
                }
            }
        }



        /// <summary>
        /// Translates text using DeepL API.
        /// </summary>
        private async Task<string> TranslateText(string text, string sourceLang, string targetLang)
        {
            string deeplSourceLang = sourceLang.ToUpperInvariant();
            string deeplTargetLang = targetLang.ToUpperInvariant();

            var requestBody = new DeepLTranslateRequest
            {
                text = new[] { text },
                source_lang = deeplSourceLang,
                target_lang = deeplTargetLang,
                context = _data.DeeplContext
            };

            string jsonBody = JsonUtility.ToJson(requestBody);
            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, _data.DeeplApiUrl);
            request.Content = content;
            request.Headers.Add("Authorization", $"DeepL-Auth-Key {_data.DeeplApiKey}");

            try
            {
                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync();
                return ParseDeepLResponse(responseJson);
            }
            catch (Exception ex)
            {
                Debug.LogError($"DeepL translation failed: {ex.Message}");
                Debug.LogError($"Request Body: {jsonBody}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Parses DeepL JSON response to extract translated text.
        /// </summary>
        private static string ParseDeepLResponse(string json)
        {
            try
            {
                var wrapper = JsonUtility.FromJson<DeepLResponseWrapper>(json);
                if (wrapper?.translations != null && wrapper.translations.Length > 0)
                {
                    return wrapper.translations[0].text;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to parse DeepL response: {ex.Message}");
            }
            return null;
        }

        [Serializable]
        private class DeepLResponseWrapper
        {
            public DeepLTranslation[] translations;
        }

        [Serializable]
        private class DeepLTranslation
        {
            public string detected_source_language;
            public string text;
        }

        [Serializable]
        private class DeepLTranslateRequest
        {
            public string[] text;
            public string source_lang;
            public string target_lang;
            public string context;
        }

        // --- Gemini Implementation ---

        private async Task TranslateWithGeminiBatchAsync(string sourceText, string sourceLang, List<string> targetLanguages, Dictionary<string, string> keyData)
        {
            string apiKey = _data.GeminiApiKey;
            if (string.IsNullOrEmpty(apiKey))
            {
                Debug.LogError("Gemini API Key is missing.");
                return;
            }

            string model = _data.GeminiModel == "custom" ? _data.GeminiCustomModel : _data.GeminiModel;
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

            string systemPrompt = _data.GeminiContext;
            string userPrompt = $"Source Language: {sourceLang}\nTarget Languages (keys): {string.Join(", ", targetLanguages)}\nSource Text:\n{sourceText}";
            var requestBody = new GeminiRequest
            {
                system_instruction = new GeminiContent { parts = new[] { new GeminiPart { text = systemPrompt } } },
                contents = new[]
                {
                    new GeminiContent { parts = new[] { new GeminiPart { text = userPrompt } } }
                },
                generationConfig = new GeminiGenerationConfig
                {
                    response_mime_type = "application/json"
                }
            };

            string jsonBody = JsonUtility.ToJson(requestBody);
            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync(url, content);
                string responseJson = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Debug.LogError($"Gemini translation failed: {response.StatusCode} - {responseJson}");
                    return;
                }

                var parsedResult = ParseGeminiResponse(responseJson);
                if (parsedResult.Count > 0)
                {
                    foreach (var lang in targetLanguages)
                    {
                        if (parsedResult.TryGetValue(lang, out string translation))
                        {
                            keyData[lang] = translation;
                        }
                    }
                    _data.HasUnsavedChanges = true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Gemini translation error: {ex.Message}");
            }
        }

        private Dictionary<string, string> ParseGeminiResponse(string json)
        {
            var result = new Dictionary<string, string>();
            try
            {
                var response = JsonUtility.FromJson<GeminiResponse>(json);
                var parts = response?.candidates?[0]?.content?.parts;
                if (parts != null && parts.Length > 0)
                {
                    string text = parts[0].text;
                    // Simple JSON parse for {"lang1":"text"} mapping since Unity's JsonUtility doesn't natively support Dictionary serialization.
                    // To handle this simply without external libraries, we use some minimal string parsing.
                    return ParseSimpleJsonToDictionary(text);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to parse Gemini response: {ex.Message}");
            }
            return result;
        }

        private Dictionary<string, string> ParseSimpleJsonToDictionary(string jsonText)
        {
            var dict = new Dictionary<string, string>();
            try
            {
                jsonText = jsonText.Trim();
                if (jsonText.StartsWith("{") && jsonText.EndsWith("}"))
                {
                    jsonText = jsonText.Substring(1, jsonText.Length - 2);

                    int i = 0;
                    while (i < jsonText.Length)
                    {
                        int keyStart = jsonText.IndexOf('"', i);
                        if (keyStart == -1)
                            break;
                        int keyEnd = jsonText.IndexOf('"', keyStart + 1);
                        if (keyEnd == -1)
                            break;

                        string key = jsonText.Substring(keyStart + 1, keyEnd - keyStart - 1);

                        int colonIndex = jsonText.IndexOf(':', keyEnd + 1);
                        if (colonIndex == -1)
                            break;

                        int valStart = jsonText.IndexOf('"', colonIndex + 1);
                        if (valStart == -1)
                            break;

                        int valEnd = valStart + 1;
                        while (valEnd < jsonText.Length)
                        {
                            if (jsonText[valEnd] == '"' && jsonText[valEnd - 1] != '\\')
                                break;
                            valEnd++;
                        }

                        if (valEnd >= jsonText.Length)
                            break;

                        string val = jsonText.Substring(valStart + 1, valEnd - valStart - 1);
                        val = val.Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t").Replace("\\\\", "\\");

                        dict[key] = val;
                        i = valEnd + 1;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error strictly parsing translation JSON: {ex.Message}");
            }
            return dict;
        }

        [Serializable]
        private class GeminiRequest
        {
            public GeminiContent system_instruction;
            public GeminiContent[] contents;
            public GeminiGenerationConfig generationConfig;
        }

        [Serializable]
        private class GeminiContent
        {
            public GeminiPart[] parts;
            public string role;
        }

        [Serializable]
        private class GeminiPart
        {
            public string text;
        }

        [Serializable]
        private class GeminiGenerationConfig
        {
            public string response_mime_type;
        }

        [Serializable]
        private class GeminiResponse
        {
            public GeminiCandidate[] candidates;
        }

        [Serializable]
        private class GeminiCandidate
        {
            public GeminiContent content;
        }
    }
}
