using System;
using System.IO;

namespace Avalonia.BuildServices.Protocol;

/// <summary>
/// Provides a reusable mechanism to retrieve or generate a persistent <see cref="Guid"/> 
/// stored in the user’s Application Data folder.
/// </summary>
public class UniqueIdentifierProvider
{
    public static Guid GetOrCreateIdentifier()
    {
        var idFilePath = BuildServicesPaths.IdPath;

        if (TryReadGuidFromFile(idFilePath, out var existingId))
        {
            return existingId;
        }

        var freshId = Guid.NewGuid();
        File.WriteAllBytes(idFilePath, freshId.ToByteArray());
        return freshId;
    }

    private static bool TryReadGuidFromFile(string path, out Guid result)
    {
        result = Guid.Empty;

        if (!File.Exists(path))
        {
            return false;
        }

        byte[] data;
        try
        {
            data = File.ReadAllBytes(path);
        }
        catch
        {
            // If reading fails for any reason, overwrite with a new GUID
            return false;
        }

        if (data.Length != 16)
        {
            // Corrupt or tampered file detected
            return false;
        }

        result = new Guid(data);
        return true;
    }
}