using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using bilibili_music_player_windows.Models;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;
using Windows.Storage;

namespace bilibili_music_player_windows.Services;

public sealed class SupabaseAccountService : SupabaseAuthClient
{
    public SupabaseAccountService() : base(new ProtectedAccountSessionStore()) { }
}

internal sealed class ProtectedAccountSessionStore : IAccountSessionStore
{
    private readonly string _path = Path.Combine(ApplicationData.Current.LocalFolder.Path, "account.session");
    public async Task<AccountSession?> ReadAsync()
    {
        if (!File.Exists(_path)) return null;
        var encrypted = CryptographicBuffer.CreateFromByteArray(await File.ReadAllBytesAsync(_path));
        var clear = await new DataProtectionProvider().UnprotectAsync(encrypted);
        return JsonSerializer.Deserialize<AccountSession>(CryptographicBuffer.ConvertBinaryToString(BinaryStringEncoding.Utf8, clear), LibraryJson.Options);
    }
    public async Task WriteAsync(AccountSession? session)
    {
        if (session is null) { if (File.Exists(_path)) File.Delete(_path); return; }
        var json = JsonSerializer.Serialize(session, LibraryJson.Options);
        var clear = CryptographicBuffer.ConvertStringToBinary(json, BinaryStringEncoding.Utf8);
        var encrypted = await new DataProtectionProvider("LOCAL=user").ProtectAsync(clear);
        CryptographicBuffer.CopyToByteArray(encrypted, out var bytes);
        await File.WriteAllBytesAsync(_path + ".tmp", bytes);
        File.Move(_path + ".tmp", _path, overwrite: true);
    }
}
