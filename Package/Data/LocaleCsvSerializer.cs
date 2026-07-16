using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PicoShot.Localization.Data
{
    internal static class LocaleCsvSerializer
    {
        public static LocaleData LoadFile(string path, out string languageCode)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("CSV file not found", path);
            
            var data = new LocaleData();
            languageCode = Path.GetFileNameWithoutExtension(path);
            data.LanguageCode = languageCode;
            
            using var reader = new StreamReader(path, Encoding.UTF8);
            
            // Read header
            if (!reader.EndOfStream)
            {
                reader.ReadLine(); // Skip header
            }
            
            while (!reader.EndOfStream)
            {
                string line = ReadCsvLine(reader);
                if (string.IsNullOrWhiteSpace(line)) continue;
                
                var columns = ParseCsvLine(line);
                if (columns.Count < 3) continue;
                
                string key = columns[0];
                string type = columns[1];
                
                if (type.Equals("String", StringComparison.OrdinalIgnoreCase))
                {
                    data.SetString(key, columns[2]);
                }
                else if (type.Equals("Array", StringComparison.OrdinalIgnoreCase))
                {
                    var list = new List<string>();
                    for (int i = 2; i < columns.Count; i++)
                    {
                        list.Add(columns[i]);
                    }
                    data.SetArray(key, list);
                }
            }
            
            return data;
        }

        public static void SaveFile(string path, LocaleData data)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var tempFile = $"{path}.tmp";
            try
            {
                using (var writer = new StreamWriter(tempFile, false, new UTF8Encoding(true)))
                {
                    writer.WriteLine("Key,Type,Value...");
                    
                    foreach (var kvp in data.Translations)
                    {
                        var key = EscapeCsv(kvp.Key);
                        if (kvp.Value is List<string> list)
                        {
                            writer.Write($"{key},Array");
                            foreach (var item in list)
                            {
                                writer.Write($",{EscapeCsv(item)}");
                            }
                            writer.WriteLine();
                        }
                        else if (kvp.Value is string[] arr)
                        {
                            writer.Write($"{key},Array");
                            foreach (var item in arr)
                            {
                                writer.Write($",{EscapeCsv(item)}");
                            }
                            writer.WriteLine();
                        }
                        else
                        {
                            writer.WriteLine($"{key},String,{EscapeCsv(kvp.Value?.ToString())}");
                        }
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
