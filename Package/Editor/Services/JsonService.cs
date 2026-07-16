using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using PicoShot.Localization.Data;
using PicoShot.Localization.Editor.Data;

namespace PicoShot.Localization.Editor.Services
{
    /// <summary>
    /// Service for importing/exporting localization data to/from JSON.
    /// </summary>
    public sealed class JsonService
    {
        private readonly LanguageEditorData _data;

        public JsonService(LanguageEditorData data)
        {
            _data = data;
        }

        /// <summary>
        /// Copies the selected key's translations as JSON to the clipboard.
        /// </summary>
        public void CopyKeyAsJson(string key, LocalizationEditor editor)
        {
            if (string.IsNullOrEmpty(key) || !_data.LanguageData.TryGetValue(key, out var keyData))
            {
                editor.ShowNotification(new GUIContent("No data to copy!"));
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("{");

            var langs = keyData.Keys.ToList();
            for (int i = 0; i < langs.Count; i++)
            {
                string lang = langs[i];
                var value = keyData[lang];

                sb.Append($"  \"{lang}\": \"{EscapeJsonString(value)}\"");

                if (i < langs.Count - 1) sb.Append(",");
                sb.AppendLine();
            }

            sb.Append("}");

            EditorGUIUtility.systemCopyBuffer = sb.ToString();
            editor.ShowNotification(new GUIContent($"Key '{key}' copied as JSON!"));
        }

        /// <summary>
        /// Pastes JSON content from clipboard into the selected key.
        /// </summary>
        public void PasteKeyFromJson(string key, LocalizationEditor editor)
        {
            if (string.IsNullOrEmpty(key))
            {
                editor.ShowNotification(new GUIContent("No key selected!"));
                return;
            }

            string json = EditorGUIUtility.systemCopyBuffer;
            if (string.IsNullOrWhiteSpace(json))
            {
                editor.ShowNotification(new GUIContent("Clipboard is empty!"));
                return;
            }

            try
            {
                var parsedData = ParseKeyJson(json);
                if (parsedData == null || parsedData.Count == 0)
                {
                    editor.ShowNotification(new GUIContent("Invalid JSON format!"));
                    return;
                }

                int importedCount = 0;

                foreach (var kvp in parsedData)
                {
                    string lang = kvp.Key;
                    string value = kvp.Value;

                    if (_data.LanguageCodes.Contains(lang))
                    {
                        _data.LanguageData[key][lang] = value;
                        importedCount++;
                    }
                }

                _data.HasUnsavedChanges = true;
                editor.ShowNotification(new GUIContent($"Imported {importedCount} translations!"));
                editor.Repaint();
            }
            catch (Exception ex)
            {
                editor.ShowNotification(new GUIContent($"Failed to parse JSON: {ex.Message}"));
                Debug.LogError($"[LocalizationEditor] Failed to paste JSON: {ex}");
            }
        }

        /// <summary>
        /// Exports all localization data to a JSON file.
        /// </summary>
        public void ExportToJson()
        {
            string path = EditorUtility.SaveFilePanel(
                "Export Localization Data to JSON",
                "",
                $"Localization_Export_{DateTime.Now:yyyyMMdd_HHmmss}.json",
                "json");

            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("{");

                for (int i = 0; i < _data.Keys.Count; i++)
                {
                    string key = _data.Keys[i];
                    sb.Append($"  \"{EscapeJsonString(key)}\": {{\n");

                    if (_data.LanguageData.TryGetValue(key, out var keyData))
                    {
                        var langs = keyData.Keys.ToList();
                        for (int j = 0; j < langs.Count; j++)
                        {
                            string lang = langs[j];
                            var value = keyData[lang];

                            sb.Append($"    \"{lang}\": \"{EscapeJsonString(value)}\"");

                            if (j < langs.Count - 1) sb.Append(",");
                            sb.Append("\n");
                        }
                    }

                    sb.Append("  }");
                    if (i < _data.Keys.Count - 1) sb.Append(",");
                    sb.Append("\n");
                }

                sb.AppendLine("}");
                File.WriteAllText(path, sb.ToString());

                EditorUtility.DisplayDialog("Export Successful",
                    $"Exported {_data.Keys.Count} keys across {_data.LanguageCodes.Count} languages to:\n{path}", "OK");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Export Failed", $"Failed to export data: {ex.Message}", "OK");
                Debug.LogError($"[LocalizationEditor] JSON export failed: {ex}");
            }
        }

        /// <summary>
        /// Exports a specific view to a JSON file.
        /// </summary>
        public void ExportViewToJson(string view)
        {
            if (string.IsNullOrEmpty(view)) return;
            string prefix = view + _data.CurrentViewDelimiter;

            string path = EditorUtility.SaveFilePanel(
                $"Export View '{view}' to JSON",
                "",
                $"View_{view}_{DateTime.Now:yyyyMMdd_HHmmss}.json",
                "json");

            if (string.IsNullOrEmpty(path)) return;

            var viewKeys = _data.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("{");

                for (int i = 0; i < viewKeys.Count; i++)
                {
                    string fullKey = viewKeys[i];
                    string localKey = _data.GetLocalKeyName(fullKey);
                    sb.Append($"  \"{EscapeJsonString(localKey)}\": {{\n");

                    if (_data.LanguageData.TryGetValue(fullKey, out var keyData))
                    {
                        var langs = keyData.Keys.ToList();
                        for (int j = 0; j < langs.Count; j++)
                        {
                            string lang = langs[j];
                            var value = keyData[lang];

                            sb.Append($"    \"{lang}\": \"{EscapeJsonString(value)}\"");

                            if (j < langs.Count - 1) sb.Append(",");
                            sb.Append("\n");
                        }
                    }

                    sb.Append("  }");
                    if (i < viewKeys.Count - 1) sb.Append(",");
                    sb.Append("\n");
                }

                sb.AppendLine("}");
                File.WriteAllText(path, sb.ToString());

                EditorUtility.DisplayDialog("Export Successful",
                    $"Exported {viewKeys.Count} keys from view '{view}' to:\n{path}", "OK");
                Debug.Log($"[LocalizationEditor] Exported view to JSON: {path}");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Export Failed", $"Failed to export view: {ex.Message}", "OK");
                Debug.LogError($"[LocalizationEditor] JSON view export failed: {ex}");
            }
        }

        /// <summary>
        /// Imports localization data from a JSON file.
        /// </summary>
        public void ImportFromJson()
        {
            string path = EditorUtility.OpenFilePanel(
                "Import Localization Data from JSON",
                "",
                "json");

            if (string.IsNullOrEmpty(path)) return;

            try
            {
                string json = File.ReadAllText(path);
                var importData = ParseLocalizationJson(json);

                if (importData.Count == 0)
                {
                    EditorUtility.DisplayDialog("Import Failed", "Invalid or empty JSON file.", "OK");
                    return;
                }

                string fileName = System.IO.Path.GetFileNameWithoutExtension(path);
                string prefix = "";
                
                if (fileName.StartsWith("View_", StringComparison.OrdinalIgnoreCase))
                {
                    int nextUnderscore = fileName.IndexOf('_', 5);
                    if (nextUnderscore > 5)
                        prefix = fileName.Substring(5, nextUnderscore - 5) + _data.CurrentViewDelimiter;
                    else
                        prefix = fileName.Substring(5) + _data.CurrentViewDelimiter;
                }
                else if (!fileName.StartsWith("Localization_Export", StringComparison.OrdinalIgnoreCase))
                {
                    bool hasDelimiters = importData.Keys.Any(k => k.Contains(_data.CurrentViewDelimiter));
                    if (!hasDelimiters)
                    {
                        prefix = fileName + _data.CurrentViewDelimiter;
                    }
                }

                if (!string.IsNullOrEmpty(prefix))
                {
                    string viewName = prefix.TrimEnd(_data.CurrentViewDelimiter);
                    bool confirm = EditorUtility.DisplayDialog("Import Table", 
                        $"It looks like this file belongs to the table '{viewName}' based on its filename.\n\n" +
                        $"Do you want to import these keys into the '{viewName}' table?\n\n" +
                        $"(If No, keys will be imported exactly as they appear in the file)",
                        "Yes, use table", "No, keep exact keys");
                    
                    if (!confirm)
                    {
                        prefix = "";
                    }
                }

                int totalKeys = importData.Count;
                int totalLangs = importData.Values.SelectMany(d => d.Keys).Distinct().Count();

                bool merge = EditorUtility.DisplayDialog("Import JSON Data",
                    $"Found {totalKeys} keys across {totalLangs} languages.\n\n" +
                    "Do you want to merge with existing data?\n\n" +
                    "Yes = Merge (keep existing keys, add/update imported)\n" +
                    "No = Replace (clear all existing data)",
                    "Merge", "Replace");

                if (!merge)
                {
                    if (!EditorUtility.DisplayDialog("Confirm Replace",
                            "This will DELETE all existing language data. Are you sure?", "Yes, Replace All", "Cancel"))
                    {
                        return;
                    }

                    _data.Reset();
                }

                int importedKeys = 0;
                int importedLangs = 0;
                int skippedLangs = 0;

                foreach (var kvp in importData)
                {
                    string importKey = kvp.Key.Trim();
                    if (!string.IsNullOrEmpty(prefix) && !importKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        importKey = prefix + importKey;
                    }
                    var langData = kvp.Value;

                    string key = _data.Keys.FirstOrDefault(k => k.Equals(importKey, StringComparison.OrdinalIgnoreCase)) ?? importKey;

                    if (!_data.LanguageData.TryGetValue(key, out var keyData))
                    {
                        keyData = new Dictionary<string, string>();
                        _data.LanguageData[key] = keyData;
                        _data.Keys.Add(key);
                        importedKeys++;
                    }

                    foreach (var langKvp in langData)
                    {
                        string lang = langKvp.Key;
                        keyData[lang] = langKvp.Value ?? "";
                    }
                }

                _data.Keys = _data.Keys.Distinct().ToList();
                _data.Keys.Sort();
                _data.LanguageCodes.Sort();

                _data.HasUnsavedChanges = true;

                string message = $"Imported {importedKeys} new keys and {importedLangs} new languages.\n" +
                                 $"Total: {_data.Keys.Count} keys across {_data.LanguageCodes.Count} languages.";
                if (skippedLangs > 0)
                {
                    message += $"\n\nSkipped {skippedLangs} unsupported language entries.";
                    Debug.LogWarning($"[LocalizationEditor] Import skipped {skippedLangs} unsupported language entries.");
                }
                EditorUtility.DisplayDialog("Import Successful", message, "OK");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Import Failed", $"Failed to import data: {ex.Message}", "OK");
                Debug.LogError($"[LocalizationEditor] JSON import failed: {ex}");
            }
        }

        /// <summary>
        /// Imports localization data into a specific view from a JSON file.
        /// </summary>
        public void ImportViewFromJson(string view)
        {
            if (string.IsNullOrEmpty(view)) return;
            string prefix = view + _data.CurrentViewDelimiter;

            string path = EditorUtility.OpenFilePanel(
                $"Import JSON into View '{view}'",
                "",
                "json");

            if (string.IsNullOrEmpty(path)) return;

            try
            {
                string json = File.ReadAllText(path);
                var importData = ParseLocalizationJson(json);

                if (importData.Count == 0)
                {
                    EditorUtility.DisplayDialog("Import Failed", "Invalid or empty JSON file.", "OK");
                    return;
                }

                int importedKeys = 0;
                int importedLangs = 0;

                foreach (var kvp in importData)
                {
                    string importKey = prefix + kvp.Key.Trim();
                    var langData = kvp.Value;

                    string key = _data.Keys.FirstOrDefault(k => k.Equals(importKey, StringComparison.OrdinalIgnoreCase)) ?? importKey;

                    if (!_data.LanguageData.TryGetValue(key, out var keyData))
                    {
                        keyData = new Dictionary<string, string>();
                        _data.LanguageData[key] = keyData;
                        _data.Keys.Add(key);
                        importedKeys++;
                    }

                    foreach (var langKvp in langData)
                    {
                        string lang = langKvp.Key;
                        keyData[lang] = langKvp.Value ?? "";
                    }
                }

                _data.Keys = _data.Keys.Distinct().ToList();
                _data.Keys.Sort();
                _data.LanguageCodes.Sort();
                _data.HasUnsavedChanges = true;

                EditorUtility.DisplayDialog("Import Successful",
                    $"Imported {importedKeys} new keys and {importedLangs} new languages into view '{view}'.", "OK");
                Debug.Log($"[LocalizationEditor] Imported JSON into view '{view}': {path}");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Import Failed", $"Failed to import view data: {ex.Message}", "OK");
                Debug.LogError($"[LocalizationEditor] JSON view import failed: {ex}");
            }
        }

        /// <summary>
        /// Parses JSON for a single key's translations.
        /// </summary>
        private Dictionary<string, string> ParseKeyJson(string json)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrWhiteSpace(json)) return result;

            int pos = 0;
            SkipWhitespace(json, ref pos);

            if (pos >= json.Length || json[pos] != '{')
                throw new Exception("JSON must start with {");

            pos++;
            SkipWhitespace(json, ref pos);

            if (pos < json.Length && json[pos] == '}')
                return result;

            while (pos < json.Length)
            {
                SkipWhitespace(json, ref pos);
                if (pos >= json.Length) break;

                if (json[pos] == '}')
                {
                    pos++;
                    break;
                }

                string lang = ParseJsonString(json, ref pos);
                SkipWhitespace(json, ref pos);

                if (pos >= json.Length || json[pos] != ':')
                    throw new Exception("Expected ':' after language code");
                pos++;

                SkipWhitespace(json, ref pos);

                result[lang] = ParseJsonString(json, ref pos);

                SkipWhitespace(json, ref pos);

                if (pos < json.Length && json[pos] == ',')
                {
                    pos++;
                    continue;
                }

                if (pos < json.Length && json[pos] == '}')
                {
                    pos++;
                    break;
                }
            }

            return result;
        }

        /// <summary>
        /// Parses full localization JSON.
        /// </summary>
        private Dictionary<string, Dictionary<string, string>> ParseLocalizationJson(string json)
        {
            var result = new Dictionary<string, Dictionary<string, string>>();
            if (string.IsNullOrWhiteSpace(json)) return result;

            int pos = 0;
            SkipWhitespace(json, ref pos);

            if (pos >= json.Length || json[pos] != '{')
                throw new Exception("JSON must start with {");

            pos++;
            SkipWhitespace(json, ref pos);

            if (pos < json.Length && json[pos] == '}')
                return result;

            while (pos < json.Length)
            {
                SkipWhitespace(json, ref pos);
                if (pos >= json.Length) break;

                if (json[pos] == '}')
                {
                    pos++;
                    break;
                }

                string key = ParseJsonString(json, ref pos);
                SkipWhitespace(json, ref pos);

                if (pos >= json.Length || json[pos] != ':')
                    throw new Exception("Expected ':' after key");
                pos++;

                SkipWhitespace(json, ref pos);

                if (pos >= json.Length || json[pos] != '{')
                    throw new Exception("Expected '{' for language object");
                pos++;

                var langDict = new Dictionary<string, string>();
                SkipWhitespace(json, ref pos);

                if (pos < json.Length && json[pos] != '}')
                {
                    while (pos < json.Length)
                    {
                        SkipWhitespace(json, ref pos);
                        if (pos >= json.Length) break;

                        if (json[pos] == '}')
                        {
                            pos++;
                            break;
                        }

                        string lang = ParseJsonString(json, ref pos);
                        SkipWhitespace(json, ref pos);

                        if (pos >= json.Length || json[pos] != ':')
                            throw new Exception("Expected ':' after language code");
                        pos++;

                        SkipWhitespace(json, ref pos);

                        string value = ParseJsonString(json, ref pos);
                        langDict[lang] = value;

                        SkipWhitespace(json, ref pos);
                        if (pos < json.Length && json[pos] == ',')
                        {
                            pos++;
                            continue;
                        }
                        else if (pos < json.Length && json[pos] == '}')
                        {
                            pos++;
                            break;
                        }
                    }
                }
                else if (pos < json.Length && json[pos] == '}')
                {
                    pos++;
                }

                result[key] = langDict;

                SkipWhitespace(json, ref pos);
                if (pos < json.Length && json[pos] == ',')
                {
                    pos++;
                    continue;
                }
                else if (pos < json.Length && json[pos] == '}')
                {
                    pos++;
                    break;
                }
            }

            return result;
        }



        /// <summary>
        /// Parses a JSON string value.
        /// </summary>
        private string ParseJsonString(string json, ref int pos)
        {
            SkipWhitespace(json, ref pos);

            if (pos >= json.Length || json[pos] != '"')
                throw new Exception("Expected string to start with quote");

            pos++;
            var sb = new StringBuilder();

            while (pos < json.Length)
            {
                char c = json[pos];

                if (c == '"')
                {
                    pos++;
                    return sb.ToString();
                }

                if (c == '\\' && pos + 1 < json.Length)
                {
                    pos++;
                    char next = json[pos];
                    switch (next)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        default: sb.Append(next); break;
                    }
                    pos++;
                }
                else
                {
                    sb.Append(c);
                    pos++;
                }
            }

            throw new Exception("Unterminated string");
        }

        /// <summary>
        /// Skips whitespace characters in JSON.
        /// </summary>
        private void SkipWhitespace(string json, ref int pos)
        {
            while (pos < json.Length && char.IsWhiteSpace(json[pos]))
                pos++;
        }

        /// <summary>
        /// Escapes a string for JSON output.
        /// </summary>
        private static string EscapeJsonString(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            return str
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }
    }
}
