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
        private class MissingTranslation
        {
            public string Key { get; set; }
            public string SourceLang { get; set; }
            public string TargetLang { get; set; }
            public string SourceText { get; set; }
        }

        /// <summary>
        /// Translates all missing translations across all keys using batching.
        /// </summary>
        public async Task TranslateAllMissingAsync(Action<float, string> onProgress, Func<bool> isCancelled)
        {
            var missingList = new List<MissingTranslation>();
            string defaultLang = LocalizationConfigProvider.Config.DefaultLanguage;

            foreach (var kvp in _data.LanguageData)
            {
                string key = kvp.Key;
                var keyData = kvp.Value;

                string sourceText = null;
                string sourceLang = defaultLang;

                if (keyData.TryGetValue(defaultLang, out var defaultValue) && !string.IsNullOrWhiteSpace(defaultValue))
                {
                    sourceText = defaultValue;
                }
                else
                {
                    foreach (var langKvp in keyData)
                    {
                        if (!string.IsNullOrWhiteSpace(langKvp.Value))
                        {
                            sourceText = langKvp.Value;
                            sourceLang = langKvp.Key;
                            break;
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(sourceText))
                    continue;

                foreach (var targetLang in _data.LanguageCodes)
                {
                    if (targetLang == sourceLang) continue;
                    
                    if (!keyData.TryGetValue(targetLang, out var targetValue) || string.IsNullOrWhiteSpace(targetValue))
                    {
                        missingList.Add(new MissingTranslation
                        {
                            Key = key,
                            SourceLang = sourceLang,
                            TargetLang = targetLang,
                            SourceText = sourceText
                        });
                    }
                }
            }

            if (missingList.Count == 0)
                return;

            if (_data.ActiveTranslationProvider == TranslationProvider.Gemini)
            {
                await BatchTranslateGeminiAsync(missingList, onProgress, isCancelled);
            }
            else
            {
                await BatchTranslateDeepLAsync(missingList, onProgress, isCancelled);
            }
        }

        private async Task BatchTranslateDeepLAsync(List<MissingTranslation> missingList, Action<float, string> onProgress, Func<bool> isCancelled)
        {
            var groups = missingList.GroupBy(m => new { m.SourceLang, m.TargetLang }).ToList();
            int totalItems = missingList.Count;
            int processedItems = 0;

            foreach (var group in groups)
            {
                if (isCancelled != null && isCancelled()) break;

                var items = group.ToList();
                int batchSize = 50;

                for (int i = 0; i < items.Count; i += batchSize)
                {
                    if (isCancelled != null && isCancelled()) break;

                    var batch = items.Skip(i).Take(batchSize).ToList();
                    string sourceLang = group.Key.SourceLang;
                    string targetLang = group.Key.TargetLang;

                    onProgress?.Invoke((float)processedItems / totalItems, $"DeepL: Translating {batch.Count} items to {targetLang}...");

                    var texts = batch.Select(b => b.SourceText).ToArray();
                    var requestBody = new DeepLTranslateRequest
                    {
                        text = texts,
                        source_lang = sourceLang.ToUpperInvariant(),
                        target_lang = targetLang.ToUpperInvariant(),
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
                        var wrapper = JsonUtility.FromJson<DeepLResponseWrapper>(responseJson);
                        
                        if (wrapper?.translations != null && wrapper.translations.Length == batch.Count)
                        {
                            for (int j = 0; j < batch.Count; j++)
                            {
                                string translatedText = wrapper.translations[j].text;
                                _data.LanguageData[batch[j].Key][targetLang] = translatedText;
                            }
                            _data.HasUnsavedChanges = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"DeepL batch translation failed: {ex.Message}");
                    }

                    processedItems += batch.Count;
                    onProgress?.Invoke((float)processedItems / totalItems, $"Translated {processedItems} / {totalItems}...");
                    await Task.Delay(LanguageEditorData.DeeplRequestDelayMs);
                }
            }
        }

        private async Task BatchTranslateGeminiAsync(List<MissingTranslation> missingList, Action<float, string> onProgress, Func<bool> isCancelled)
        {
            int totalItems = missingList.Count;
            int processedItems = 0;
            int batchSize = 20;

            string apiKey = _data.GeminiApiKey;
            if (string.IsNullOrEmpty(apiKey))
            {
                Debug.LogError("Gemini API Key is missing.");
                return;
            }

            string model = _data.GeminiModel == "custom" ? _data.GeminiCustomModel : _data.GeminiModel;
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

            for (int i = 0; i < totalItems; i += batchSize)
            {
                if (isCancelled != null && isCancelled()) break;

                var batch = missingList.Skip(i).Take(batchSize).ToList();
                onProgress?.Invoke((float)processedItems / totalItems, $"Gemini: Translating batch {i/batchSize + 1} ({batch.Count} items)...");

                var sb = new StringBuilder();
                sb.AppendLine("Translate the following texts to their respective target languages.");
                sb.AppendLine("Return ONLY a valid flat JSON object without any markdown block. The keys of the JSON must exactly match the 'ID' provided.");
                sb.AppendLine("Data to translate:");

                foreach (var item in batch)
                {
                    string compositeId = $"{item.Key}:::{item.TargetLang}";
                    string safeText = item.SourceText.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
                    sb.AppendLine($"ID: \"{compositeId}\", Target Language: \"{item.TargetLang}\", Text: \"{safeText}\"");
                }

                string systemPrompt = _data.GeminiContext;
                string userPrompt = sb.ToString();

                var requestBody = new GeminiRequest
                {
                    system_instruction = new GeminiContent { parts = new[] { new GeminiPart { text = systemPrompt } } },
                    contents = new[] { new GeminiContent { parts = new[] { new GeminiPart { text = userPrompt } } } },
                    generationConfig = new GeminiGenerationConfig { response_mime_type = "application/json" }
                };

                string jsonBody = JsonUtility.ToJson(requestBody);
                var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                try
                {
                    var response = await _httpClient.PostAsync(url, content);
                    string responseJson = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        Debug.LogError($"Gemini batch translation failed: {response.StatusCode} - {responseJson}");
                        continue;
                    }

                    var parsedResult = ParseGeminiResponse(responseJson);
                    if (parsedResult.Count > 0)
                    {
                        foreach (var kvp in parsedResult)
                        {
                            string compositeId = kvp.Key;
                            string translatedText = kvp.Value;

                            int separatorIndex = compositeId.LastIndexOf(":::", StringComparison.Ordinal);
                            if (separatorIndex >= 0)
                            {
                                string originalKey = compositeId.Substring(0, separatorIndex);
                                string targetLang = compositeId.Substring(separatorIndex + 3);

                                if (_data.LanguageData.TryGetValue(originalKey, out var dict))
                                {
                                    dict[targetLang] = translatedText;
                                    _data.HasUnsavedChanges = true;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Gemini batch error: {ex.Message}");
                }

                processedItems += batch.Count;
                onProgress?.Invoke((float)processedItems / totalItems, $"Translated {processedItems} / {totalItems}...");
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

        public async Task RephraseWithConstraintAsync(string key, string targetLang, int maxCharacters)
        {
            if (!_data.LanguageData.TryGetValue(key, out var keyData))
            {
                Debug.LogError($"Key '{key}' not found in LanguageData.");
                return;
            }

            string defaultLang = LocalizationConfigProvider.Config.DefaultLanguage;
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
                Debug.LogWarning("No source text available to rephrase.");
                return;
            }

            string apiKey = _data.GeminiApiKey;
            if (string.IsNullOrEmpty(apiKey))
            {
                Debug.LogError("Gemini API Key is missing.");
                return;
            }

            string model = _data.GeminiModel == "custom" ? _data.GeminiCustomModel : _data.GeminiModel;
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

            string systemPrompt = "You are a specialized game localization translator. Your task is to accurately translate and rephrase text for game UI.";
            
            var sb = new StringBuilder();
            sb.AppendLine($"Translate/Rephrase the following text into the target language.");
            sb.AppendLine($"STRICT CONSTRAINT: The final translated text MUST be extremely concise and STRICTLY UNDER {maxCharacters} characters.");
            sb.AppendLine("It is acceptable to use common abbreviations, shorten words, or adapt the literal meaning slightly to preserve this strict length limit.");
            sb.AppendLine("Return ONLY a valid flat JSON object mapping the target language code to the translated text. No markdown blocks.");
            sb.AppendLine($"Target Language: \"{targetLang}\"");
            sb.AppendLine($"Source Language: \"{sourceLang}\"");
            sb.AppendLine($"Source Text: \"{sourceText.Replace("\"", "\\\"")}\"");

            var requestBody = new GeminiRequest
            {
                system_instruction = new GeminiContent { parts = new[] { new GeminiPart { text = systemPrompt } } },
                contents = new[] { new GeminiContent { parts = new[] { new GeminiPart { text = sb.ToString() } } } },
                generationConfig = new GeminiGenerationConfig { response_mime_type = "application/json" }
            };

            string jsonBody = JsonUtility.ToJson(requestBody);
            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync(url, content);
                string responseJson = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Debug.LogError($"Gemini rephrase failed: {response.StatusCode} - {responseJson}");
                    return;
                }

                var parsedResult = ParseGeminiResponse(responseJson);
                if (parsedResult.Count > 0)
                {
                    // It should return {"targetLang": "text"} or similar. Just take the first valid value.
                    string rephrasedText = null;
                    if (parsedResult.TryGetValue(targetLang, out var exactMatch))
                    {
                        rephrasedText = exactMatch;
                    }
                    else if (parsedResult.Count == 1)
                    {
                        rephrasedText = parsedResult.Values.First();
                    }

                    if (!string.IsNullOrEmpty(rephrasedText))
                    {
                        keyData[targetLang] = rephrasedText;
                        _data.HasUnsavedChanges = true;
                    }
                    else
                    {
                        Debug.LogError("Gemini rephrase failed to extract text from JSON.");
                    }
                }
                else
                {
                    Debug.LogError("Gemini rephrase returned empty or invalid JSON.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Gemini rephrase error: {ex.Message}");
            }
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
