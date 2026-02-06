using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PicoShot.Localization.Data
{
    /// <summary>
    /// BLOC (Binary Localization Container) format serializer for single-language files.
    /// Optimized binary format with embedded language code and deduplicated strings.
    /// 
    /// - Header (32 bytes): Magic, Version, Flags, LanguageCode, EntryCount, StringCount, Offsets
    /// - Entry Table: 8 bytes per entry (KeyId: 4 + ValueId: 4)
    /// - String Pool: Length-prefixed UTF-8 strings
    /// - Footer (16 bytes): CRC32 checksum + reserved
    /// </summary>
    public static class LocaleBlocSerializer
    {
        // Magic and version
        private static readonly byte[] Magic = { 0x42, 0x4C, 0x4F, 0x43 }; // "BLOC"
        private const int Version = 1;
        private const int HeaderSize = 32;
        private const int FooterSize = 16;

        // Flags
        private const uint FlagHasArrays = 0x01;
        private const uint FlagHasMetadata = 0x02;

        /// <summary>
        /// Serializes locale data to BLOC format.
        /// </summary>
        public static byte[] Serialize(LocaleData data)
        {
            if (data?.Translations == null)
                throw new ArgumentNullException(nameof(data));

            var entries = BuildEntries(data.Translations);
            var stringPool = BuildStringPool(entries);
            var stringToId = BuildStringToIdMap(stringPool);

            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms, Encoding.UTF8);

            uint flags = CalculateFlags(entries);
            int entryCount = entries.Count;
            int stringCount = stringPool.Count;

            int entryTableSize = entryCount * 8;
            int stringPoolSize = CalculateStringPoolSize(stringPool);
            int metadataSize = CalculateMetadataSize(data);

            long entryTableOffset = HeaderSize;
            long stringPoolOffset = entryTableOffset + entryTableSize;
            long metadataOffset = stringPoolOffset + stringPoolSize;
            long footerOffset = metadataOffset + metadataSize;

            // Write header
            WriteHeader(writer, flags, data.LanguageCode, (uint)entryCount, (uint)stringCount,
                (uint)entryTableOffset, (uint)stringPoolOffset, (uint)metadataOffset);

            // Write entry table
            WriteEntryTable(writer, entries, stringToId);

            // Write string pool
            WriteStringPool(writer, stringPool);

            // Write metadata
            if ((flags & FlagHasMetadata) != 0)
            {
                WriteMetadata(writer, data);
            }

            // Write footer with checksum
            uint checksum = ComputeCrc32(ms.GetBuffer(), 0, (int)ms.Position);
            writer.Write(checksum);
            writer.Write(0u); // Reserved
            writer.Write(0u); // Reserved
            writer.Write(0u); // Reserved

            return ms.ToArray();
        }

        /// <summary>
        /// Deserializes BLOC format data.
        /// </summary>
        public static LocaleData Deserialize(byte[] data)
        {
            if (data == null || data.Length < HeaderSize + FooterSize)
                throw new ArgumentException("Data too short to be valid BLOC file", nameof(data));

            // Verify magic
            if (data.Length < 4 ||
                data[0] != Magic[0] || data[1] != Magic[1] ||
                data[2] != Magic[2] || data[3] != Magic[3])
            {
                throw new InvalidDataException("Invalid BLOC magic number");
            }

            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms, Encoding.UTF8);

            // Read header
            var header = ReadHeader(reader);

            if (header.Version != Version)
                throw new InvalidDataException($"Unsupported BLOC version: {header.Version}");

            // Read string pool
            ms.Position = header.StringPoolOffset;
            var stringPool = ReadStringPool(reader, (int)header.StringCount);

            // Read entry table
            ms.Position = header.EntryTableOffset;
            var translations = ReadEntryTable(reader, header.EntryCount, stringPool);

            // Read metadata if present
            long timestamp = 0;
            if ((header.Flags & FlagHasMetadata) != 0 && header.MetadataOffset > 0)
            {
                ms.Position = header.MetadataOffset;
                timestamp = reader.ReadInt64();
            }

            return new LocaleData
            {
                Version = (int)header.Version,
                LanguageCode = header.LanguageCode,
                Timestamp = timestamp,
                Translations = translations
            };
        }

        /// <summary>
        /// Deserializes BLOC data from a file.
        /// </summary>
        public static LocaleData DeserializeFromFile(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("BLOC file not found", path);

            byte[] data = File.ReadAllBytes(path);
            return Deserialize(data);
        }

        /// <summary>
        /// Saves locale data to a BLOC file.
        /// </summary>
        public static void SaveToFile(string path, LocaleData data)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            byte[] bytes = Serialize(data);
            File.WriteAllBytes(path, bytes);
        }

        #region Private Methods

        private sealed class Entry
        {
            public string Key;
            public object Value;
            public bool IsArray;
        }

        private static void WriteHeader(BinaryWriter writer, uint flags, string languageCode,
            uint entryCount, uint stringCount, uint entryTableOffset, uint stringPoolOffset, uint metadataOffset)
        {
            writer.Write(Magic);
            writer.Write((ushort)Version);
            writer.Write((ushort)flags);

            byte[] langBytes = Encoding.ASCII.GetBytes(languageCode ?? "en");
            byte[] paddedLang = new byte[4];
            int copyLen = Math.Min(langBytes.Length, 4);
            Array.Copy(langBytes, paddedLang, copyLen);
            writer.Write(paddedLang);

            writer.Write(entryCount);
            writer.Write(stringCount);
            writer.Write(entryTableOffset);
            writer.Write(stringPoolOffset);
            writer.Write(metadataOffset);
        }

        private static (ushort Version, ushort Flags, string LanguageCode, uint EntryCount,
            uint StringCount, uint EntryTableOffset, uint StringPoolOffset, uint MetadataOffset) ReadHeader(BinaryReader reader)
        {
            reader.ReadBytes(4); // Magic (already verified)
            ushort version = reader.ReadUInt16();
            ushort flags = reader.ReadUInt16();

            byte[] langBytes = reader.ReadBytes(4);
            int langLen = 0;
            while (langLen < 4 && langBytes[langLen] != 0)
                langLen++;
            string languageCode = Encoding.ASCII.GetString(langBytes, 0, langLen);

            uint entryCount = reader.ReadUInt32();
            uint stringCount = reader.ReadUInt32();
            uint entryTableOffset = reader.ReadUInt32();
            uint stringPoolOffset = reader.ReadUInt32();
            uint metadataOffset = reader.ReadUInt32();

            return (version, flags, languageCode, entryCount, stringCount,
                entryTableOffset, stringPoolOffset, metadataOffset);
        }

        private static List<Entry> BuildEntries(Dictionary<string, object> translations)
        {
            var entries = new List<Entry>(translations.Count);
            foreach (var kvp in translations)
            {
                entries.Add(new Entry
                {
                    Key = kvp.Key,
                    Value = kvp.Value,
                    IsArray = kvp.Value is List<string> or string[]
                });
            }
            return entries;
        }

        private static List<string> BuildStringPool(List<Entry> entries)
        {
            var pool = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                pool.Add(entry.Key);
                if (entry.IsArray)
                {
                    var items = entry.Value is List<string> list ? list : (IEnumerable<string>)(string[])entry.Value;
                    // Add the encoded array string itself
                    pool.Add(EncodeArray(items));
                    // Also add individual items for deduplication
                    foreach (var item in items)
                    {
                        if (item != null)
                            pool.Add(item);
                    }
                }
                else if (entry.Value != null)
                {
                    pool.Add(entry.Value.ToString());
                }
            }
            return pool.ToList();
        }

        private static Dictionary<string, uint> BuildStringToIdMap(List<string> pool)
        {
            var map = new Dictionary<string, uint>(pool.Count, StringComparer.Ordinal);
            for (uint i = 0; i < pool.Count; i++)
                map[pool[(int)i]] = i;
            return map;
        }

        private static uint CalculateFlags(List<Entry> entries)
        {
            uint flags = FlagHasMetadata; // Always include metadata
            if (entries.Any(e => e.IsArray))
                flags |= FlagHasArrays;
            return flags;
        }

        private static int CalculateStringPoolSize(List<string> pool)
        {
            int size = 0;
            foreach (var str in pool)
            {
                size += 4; // Length prefix
                size += Encoding.UTF8.GetByteCount(str);
            }
            return size;
        }

        private static int CalculateMetadataSize(LocaleData data)
        {
            return 8; // Timestamp only
        }

        private static void WriteEntryTable(BinaryWriter writer, List<Entry> entries, Dictionary<string, uint> stringToId)
        {
            foreach (var entry in entries)
            {
                writer.Write(stringToId[entry.Key]);

                if (entry.IsArray)
                {
                    var items = entry.Value is List<string> list ? list : (IEnumerable<string>)(string[])entry.Value;
                    var arrayString = EncodeArray(items);
                    writer.Write(stringToId[arrayString]);
                }
                else
                {
                    writer.Write(stringToId[entry.Value?.ToString() ?? ""]);
                }
            }
        }

        private static string EncodeArray(IEnumerable<string> items)
        {
            var sb = new StringBuilder();
            sb.Append('\u0001'); // Array marker
            var itemList = items.ToList();
            sb.Append(itemList.Count);
            foreach (var item in itemList)
            {
                sb.Append('\u0002'); // Item separator
                sb.Append(item?.Replace("\u0002", "\u0003") ?? "");
            }
            return sb.ToString();
        }

        private static List<string> DecodeArray(string encoded)
        {
            if (string.IsNullOrEmpty(encoded) || encoded[0] != '\u0001')
                return new List<string>();

            var result = new List<string>();
            int pos = 1;

            // Read count
            int count = 0;
            while (pos < encoded.Length && char.IsDigit(encoded[pos]))
            {
                count = count * 10 + (encoded[pos] - '0');
                pos++;
            }

            // Read items
            for (int i = 0; i < count && pos < encoded.Length; i++)
            {
                if (encoded[pos] == '\u0002')
                    pos++;

                int start = pos;
                while (pos < encoded.Length && encoded[pos] != '\u0002')
                    pos++;

                string item = encoded.Substring(start, pos - start).Replace("\u0003", "\u0002");
                result.Add(item);
            }

            return result;
        }

        private static void WriteStringPool(BinaryWriter writer, List<string> pool)
        {
            foreach (var str in pool)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(str);
                writer.Write(bytes.Length);
                writer.Write(bytes);
            }
        }

        private static string[] ReadStringPool(BinaryReader reader, int count)
        {
            var pool = new string[count];
            for (int i = 0; i < count; i++)
            {
                int length = reader.ReadInt32();
                byte[] bytes = reader.ReadBytes(length);
                pool[i] = Encoding.UTF8.GetString(bytes);
            }
            return pool;
        }

        private static Dictionary<string, object> ReadEntryTable(BinaryReader reader, uint entryCount, string[] stringPool)
        {
            var translations = new Dictionary<string, object>((int)entryCount, StringComparer.Ordinal);

            for (int i = 0; i < entryCount; i++)
            {
                uint keyId = reader.ReadUInt32();
                uint valueId = reader.ReadUInt32();

                string key = stringPool[keyId];
                string value = stringPool[valueId];

                if (!string.IsNullOrEmpty(value) && value[0] == '\u0001')
                {
                    translations[key] = DecodeArray(value);
                }
                else
                {
                    translations[key] = value;
                }
            }

            return translations;
        }

        private static void WriteMetadata(BinaryWriter writer, LocaleData data)
        {
            writer.Write(data.Timestamp);
        }

        private static uint ComputeCrc32(byte[] data, int offset, int count)
        {
            const uint polynomial = 0xEDB88320;
            uint crc = 0xFFFFFFFF;

            for (int i = offset; i < offset + count; i++)
            {
                crc ^= data[i];
                for (int j = 0; j < 8; j++)
                {
                    crc = (crc >> 1) ^ (polynomial & ~(crc & 1));
                }
            }

            return ~crc;
        }

        #endregion
    }
}
