using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UniParse.Services;

/// <summary>IL2CPP metadata file validation result.</summary>
public sealed record Il2CppMetadataDiagnostic(
    string MetadataPath,
    long MetadataLength,
    string ActualMagic,
    bool HasStandardMagic,
    string? GameProfile,
    string? RepeatingXorKey)
{
    public bool IsProtectedOrUnsupported => !HasStandardMagic;
    public bool HasRecoverableXorKey => !string.IsNullOrEmpty(RepeatingXorKey);

    public string UserMessage
    {
        get
        {
            string gameName = GameProfile is null ? "このゲーム" : GameProfile;
            if (HasRecoverableXorKey)
                return $"{gameName} を検出: global-metadata.dat は繰り返し XOR で保護されています。鍵候補 {RepeatingXorKey} を検出しました。";

            return $"{gameName} を検出: global-metadata.dat は保護または独自形式です（先頭: {ActualMagic}）。アセットの閲覧は可能ですが、IL2CPP スクリプト情報は復元できません。";
        }
    }

    public static Il2CppMetadataDiagnostic? Inspect(IReadOnlyList<string> paths)
    {
        string? metadataPath = FindMetadataPath(paths);
        if (metadataPath is null)
            return null;

        byte[] header = new byte[4];
        using (FileStream stream = File.OpenRead(metadataPath))
        {
            if (stream.Read(header, 0, header.Length) != header.Length)
                return null;
        }

        bool hasStandardMagic = header[0] == 0xAF && header[1] == 0x1B && header[2] == 0xB1 && header[3] == 0xFA;
        string? profile = IsHololiveDreamsPath(metadataPath) ? "hololive Dreams" : null;
        string? xorKey = hasStandardMagic ? null : FindRepeatingXorKey(metadataPath);
        return new Il2CppMetadataDiagnostic(
            metadataPath,
            new FileInfo(metadataPath).Length,
            BitConverter.ToString(header),
            hasStandardMagic,
            profile,
            xorKey);
    }

    /// <summary>
    /// Checks the common lightweight protection scheme: a repeating XOR key of one to four bytes.
    /// The key is derived from IL2CPP's fixed magic and tested against the remaining header layout,
    /// so a match is not accepted merely because the first four bytes happen to decrypt correctly.
    /// </summary>
    private static string? FindRepeatingXorKey(string metadataPath)
    {
        const int HeaderBytesToCheck = 512;
        byte[] encrypted = new byte[HeaderBytesToCheck];
        int bytesRead;
        using (FileStream stream = File.OpenRead(metadataPath))
            bytesRead = stream.Read(encrypted, 0, encrypted.Length);
        if (bytesRead < 128)
            return null;

        long fileLength = new FileInfo(metadataPath).Length;
        for (int keyLength = 1; keyLength <= 4; keyLength++)
        {
            for (int version = 24; version <= 40; version++)
            {
                byte[] knownPlaintext = new byte[]
                {
                    0xAF, 0x1B, 0xB1, 0xFA,
                    (byte)version, 0x00, 0x00, 0x00,
                };
                int[] key = new int[keyLength];
                Array.Fill(key, -1);
                bool consistent = true;

                for (int i = 0; i < knownPlaintext.Length; i++)
                {
                    int index = i % keyLength;
                    int value = encrypted[i] ^ knownPlaintext[i];
                    if (key[index] >= 0 && key[index] != value)
                    {
                        consistent = false;
                        break;
                    }
                    key[index] = value;
                }
                if (!consistent || Array.Exists(key, value => value < 0))
                    continue;

                byte[] decrypted = new byte[bytesRead];
                for (int i = 0; i < bytesRead; i++)
                    decrypted[i] = (byte)(encrypted[i] ^ key[i % keyLength]);

                if (HasPlausibleMetadataHeader(decrypted, fileLength, version))
                    return BitConverter.ToString(key.Select(value => (byte)value).ToArray());
            }
        }
        return null;
    }

    private static bool HasPlausibleMetadataHeader(byte[] header, long fileLength, int expectedVersion)
    {
        if (ReadUInt32(header, 0) != 0xFAB11BAF || ReadUInt32(header, 4) != expectedVersion)
            return false;

        int validPairs = 0;
        for (int position = 8; position + 8 <= header.Length && position < 160; position += 8)
        {
            uint offset = ReadUInt32(header, position);
            uint size = ReadUInt32(header, position + 4);
            if (offset >= 160 && offset < fileLength && size <= fileLength - offset)
                validPairs++;
        }
        return validPairs >= 8;
    }

    private static uint ReadUInt32(byte[] data, int offset)
        => (uint)(data[offset]
            | data[offset + 1] << 8
            | data[offset + 2] << 16
            | data[offset + 3] << 24);

    private static string? FindMetadataPath(IReadOnlyList<string> paths)
    {
        foreach (string input in paths)
        {
            string? directory = Directory.Exists(input)
                ? input
                : Path.GetDirectoryName(input);
            if (string.IsNullOrEmpty(directory))
                continue;

            string direct = Path.Combine(directory, "il2cpp_data", "Metadata", "global-metadata.dat");
            if (File.Exists(direct))
                return direct;

            try
            {
                foreach (string dataDirectory in Directory.EnumerateDirectories(directory, "*_Data", SearchOption.TopDirectoryOnly))
                {
                    string candidate = Path.Combine(dataDirectory, "il2cpp_data", "Metadata", "global-metadata.dat");
                    if (File.Exists(candidate))
                        return candidate;
                }
            }
            catch (UnauthorizedAccessException)
            {
                // The normal asset import path will report its own access error.
            }
        }
        return null;
    }

    private static bool IsHololiveDreamsPath(string metadataPath)
        => metadataPath.Contains("hololiveDreams", StringComparison.OrdinalIgnoreCase)
           || metadataPath.Contains("hololive-Dreams_Data", StringComparison.OrdinalIgnoreCase);
}
