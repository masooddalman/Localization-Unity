using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PicoShot.Localization.Data
{
    internal static class LocaleCsvSerializer
    {
        public static LanguageData LoadTranslations(string path)
        {
            var data = new LanguageData();

            if (!File.Exists(path))
                return data;

            using var reader = new StreamReader(path, Encoding.UTF8);
            return LoadTranslationsFromReader(reader);
        }

        public static LanguageData LoadTranslationsFromString(string csvContent)
        {
            var data = new LanguageData();
            if (string.IsNullOrWhiteSpace(csvContent))
                return data;

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return LoadTranslationsFromReader(reader);
        }

        private static LanguageData LoadTranslationsFromReader(StreamReader reader)
        {
            var data = new LanguageData();

            // Read header
            if (reader.EndOfStream)
                return data;

            string headerLine = ReadCsvLine(reader);
            var headerColumns = ParseCsvLine(headerLine);
            if (headerColumns.Count < 2)
                return data; // Needs at least Key and one language

            // Extract languages from header (skip 'Key')
            var languages = new List<string>();
            for (int i = 1; i < headerColumns.Count; i++)
            {
                languages.Add(headerColumns[i].Trim());
            }

            while (!reader.EndOfStream)
            {
                string line = ReadCsvLine(reader);
                if (string.IsNullOrWhiteSpace(line)) continue;

                var columns = ParseCsvLine(line);
                if (columns.Count == 0 || string.IsNullOrWhiteSpace(columns[0])) continue;

                string key = columns[0];
                var keyDict = new Dictionary<string, string>();

                for (int i = 0; i < languages.Count; i++)
                {
                    string lang = languages[i];
                    string value = (i + 1 < columns.Count) ? columns[i + 1] : "";
                    keyDict[lang] = value;
                }

                data.Translations[key] = keyDict;
            }

            return data;
        }

        public static void SaveTranslations(string path, LanguageData data)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var tempFile = $"{path}.tmp";
            try
            {
                var languages = new List<string>(data.GetAllLanguageCodes());
                languages.Sort();

                using (var writer = new StreamWriter(tempFile, false, new UTF8Encoding(true)))
                {
                    // Write Header
                    writer.Write("Key");
                    foreach (var lang in languages)
                    {
                        writer.Write($",{EscapeCsv(lang)}");
                    }
                    writer.WriteLine();

                    // Write rows
                    foreach (var kvp in data.Translations)
                    {
                        string key = kvp.Key;
                        writer.Write(EscapeCsv(key));

                        var keyDict = kvp.Value;
                        foreach (var lang in languages)
                        {
                            string val = keyDict.TryGetValue(lang, out var txt) ? txt : "";
                            writer.Write($",{EscapeCsv(val)}");
                        }
                        writer.WriteLine();
                    }
                }

                if (File.Exists(path))
                    File.Replace(tempFile, path, $"{path}.bak");
                else
                    File.Move(tempFile, path);
            }
            catch (Exception)
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
                throw;
            }
        }

        private static string ReadCsvLine(StreamReader reader)
        {
            var sb = new StringBuilder();
            bool inQuotes = false;

            while (!reader.EndOfStream)
            {
                int c = reader.Read();
                if (c == -1) break;

                char ch = (char)c;
                if (ch == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (ch == '\n' && !inQuotes)
                {
                    // If carriage return was added before newline, remove it
                    if (sb.Length > 0 && sb[sb.Length - 1] == '\r')
                    {
                        sb.Length--;
                    }
                    break;
                }

                sb.Append(ch);
            }
            return sb.ToString();
        }

        private static List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            var currentField = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (ch == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        currentField.Append('"');
                        i++; // Skip escaped quote
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (ch == ',' && !inQuotes)
                {
                    result.Add(currentField.ToString());
                    currentField.Clear();
                }
                else
                {
                    currentField.Append(ch);
                }
            }

            result.Add(currentField.ToString()); // Add last field
            return result;
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";

            bool needsQuotes = value.Contains(",") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r");
            if (needsQuotes)
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }
            return value;
        }
    }
}
