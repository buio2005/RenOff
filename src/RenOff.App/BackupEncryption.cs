using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace RenOff.App;

public static class BackupEncryption
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("RENOFFENC1");
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 200_000;

    public static bool IsEncrypted(byte[] data)
        => data.Length >= Magic.Length && data.AsSpan(0, Magic.Length).SequenceEqual(Magic);

    public static byte[] Encrypt(string plaintextJson, string passphrase)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var key = DeriveKey(passphrase, salt);

        var plainBytes = Encoding.UTF8.GetBytes(plaintextJson);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        using var ms = new MemoryStream();
        ms.Write(Magic);
        ms.Write(salt);
        ms.Write(nonce);
        ms.Write(tag);
        ms.Write(cipherBytes);
        return ms.ToArray();
    }

    public static string Decrypt(byte[] data, string passphrase)
    {
        var offset = Magic.Length;
        if (data.Length < offset + SaltSize + NonceSize + TagSize)
        {
            throw new InvalidDataException("File di backup non valido o corrotto.");
        }

        var salt = data[offset..(offset += SaltSize)];
        var nonce = data[offset..(offset += NonceSize)];
        var tag = data[offset..(offset += TagSize)];
        var cipherBytes = data[offset..];

        var key = DeriveKey(passphrase, salt);
        var plainBytes = new byte[cipherBytes.Length];

        using var aes = new AesGcm(key, TagSize);
        try
        {
            aes.Decrypt(nonce, cipherBytes, tag, plainBytes);
        }
        catch (CryptographicException)
        {
            throw new InvalidDataException("Password errata o file corrotto.");
        }

        return Encoding.UTF8.GetString(plainBytes);
    }

    private static byte[] DeriveKey(string passphrase, byte[] salt)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(passphrase, salt, Iterations, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(KeySize);
    }
}
