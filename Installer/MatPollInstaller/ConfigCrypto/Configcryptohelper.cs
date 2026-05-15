using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace ConfigCrypto
{
    /// <summary>
    /// AES-256-CBC + HMAC-SHA256 encryption/decryption helper.
    ///
    /// WHY CBC INSTEAD OF GCM?
    /// =======================
    /// The WiX custom action project targets net472.  AesGcm is a .NET Core /
    /// .NET 5+ type and does NOT exist on .NET Framework 4.x.  If we use an
    /// #if NET8_0_OR_GREATER guard, the CA (net472) writes CBC ciphertext and
    /// the runtime service (net8) tries to decrypt it as GCM — they are
    /// incompatible wire formats and decryption silently fails or throws.
    ///
    /// The safe fix is to use CBC + HMAC-SHA256 on BOTH sides.  CBC is
    /// perfectly strong when used correctly (random IV, HMAC authentication).
    /// The key is machine-bound via PBKDF2(MachineGuid + pepper), so the
    /// ciphertext is not portable across machines anyway.
    ///
    /// Wire format (binary, then Base64-encoded, prefixed with "ENC:"):
    ///   [ 16 bytes AES IV ]
    ///   [ 32 bytes HMAC-SHA256 tag over the ciphertext ]
    ///   [ N  bytes AES-256-CBC ciphertext (PKCS7 padded) ]
    ///
    /// Key derivation:
    ///   PBKDF2-SHA256( password: SHA256(MachineGuid + PEPPER), salt: APP_SALT,
    ///                  iterations: 100_000 ) → 32 bytes
    /// </summary>
    public static class ConfigCryptoHelper
    {
        // -----------------------------------------------------------------
        // Change both constants before your first production build.
        // They are baked into every encrypted blob; changing them after
        // deployment will make existing encrypted files unreadable.
        // -----------------------------------------------------------------
        private const string INSTALL_PEPPER = "MatPoll-COSEC-2026-#Kx9!zQ";
        private const string APP_SALT       = "MatPollCOSECConfigSalt-v1";

        private const string ENC_PREFIX = "ENC:";

        // -----------------------------------------------------------------
        //  Public API
        // -----------------------------------------------------------------

        /// <summary>Returns true when <paramref name="value"/> is an ENC: token.</summary>
        public static bool IsEncrypted(string value)
            => value != null && value.StartsWith(ENC_PREFIX, StringComparison.Ordinal);

        /// <summary>Encrypts <paramref name="plaintext"/> → "ENC:&lt;base64&gt;".</summary>
        public static string Encrypt(string plaintext)
        {
            if (string.IsNullOrEmpty(plaintext)) return plaintext;

            byte[] key   = DeriveKey();
            byte[] plain = Encoding.UTF8.GetBytes(plaintext);

            using (var aes = Aes.Create())
            {
                aes.KeySize  = 256;
                aes.Mode     = CipherMode.CBC;
                aes.Padding  = PaddingMode.PKCS7;
                aes.Key      = key;
                aes.GenerateIV();

                byte[] iv         = aes.IV;                           // 16 bytes
                byte[] ciphertext = EncryptBytes(aes, plain);         // N  bytes

                // Authenticate the ciphertext (Encrypt-then-MAC)
                byte[] tag = ComputeHmac(key, ciphertext);            // 32 bytes

                // Layout: [ IV (16) | TAG (32) | CIPHERTEXT (N) ]
                byte[] blob = new byte[iv.Length + tag.Length + ciphertext.Length];
                Buffer.BlockCopy(iv,         0, blob, 0,                             iv.Length);
                Buffer.BlockCopy(tag,        0, blob, iv.Length,                     tag.Length);
                Buffer.BlockCopy(ciphertext, 0, blob, iv.Length + tag.Length,        ciphertext.Length);

                return ENC_PREFIX + Convert.ToBase64String(blob);
            }
        }

        /// <summary>
        /// Decrypts an "ENC:&lt;base64&gt;" token → original plaintext.
        /// Returns the input unchanged when it is NOT an ENC: token.
        /// </summary>
        public static string Decrypt(string encryptedValue)
        {
            if (!IsEncrypted(encryptedValue)) return encryptedValue;

            byte[] blob = Convert.FromBase64String(
                encryptedValue.Substring(ENC_PREFIX.Length));

            // Minimum length: 16 (IV) + 32 (TAG) + 1 (at least one byte of ciphertext)
            if (blob.Length < 49)
                throw new CryptographicException(
                    "Encrypted blob is too short — it may have been truncated or corrupted.");

            byte[] key        = DeriveKey();
            int    ivLen      = 16;
            int    tagLen     = 32;

            byte[] iv         = new byte[ivLen];
            byte[] tag        = new byte[tagLen];
            byte[] ciphertext = new byte[blob.Length - ivLen - tagLen];

            Buffer.BlockCopy(blob, 0,              iv,         0, ivLen);
            Buffer.BlockCopy(blob, ivLen,           tag,        0, tagLen);
            Buffer.BlockCopy(blob, ivLen + tagLen,  ciphertext, 0, ciphertext.Length);

            // Verify MAC before decrypting (prevents padding-oracle attacks)
            byte[] expectedTag = ComputeHmac(key, ciphertext);
            if (!FixedTimeEquals(tag, expectedTag))
                throw new CryptographicException(
                    "HMAC validation failed. The encrypted value may have been tampered with, " +
                    "or the installer pepper/salt does not match.");

            using (var aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.Mode    = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key     = key;
                aes.IV      = iv;

                byte[] plain = DecryptBytes(aes, ciphertext);
                return Encoding.UTF8.GetString(plain);
            }
        }

        // -----------------------------------------------------------------
        //  Key derivation
        // -----------------------------------------------------------------

        private static byte[] DeriveKey()
        {
            string machineSecret = GetMachineSecret();
            byte[] password      = Encoding.UTF8.GetBytes(machineSecret);
            byte[] salt          = Encoding.UTF8.GetBytes(APP_SALT);

            using (var kdf = new Rfc2898DeriveBytes(
                password, salt,
                iterations: 100_000,
                HashAlgorithmName.SHA256))
            {
                return kdf.GetBytes(32); // 256-bit AES key
            }
        }

        /// <summary>
        /// Returns a stable hex string derived from the Windows machine GUID
        /// and a build-time pepper.  This ties every encrypted file to the
        /// specific machine the installer ran on.
        /// </summary>
        private static string GetMachineSecret()
        {
            string machineGuid = ReadMachineGuid();
            string combined    = machineGuid + INSTALL_PEPPER;

            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(combined));
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private static string ReadMachineGuid()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Cryptography", writable: false))
                {
                    return key?.GetValue("MachineGuid")?.ToString()
                           ?? Environment.MachineName;
                }
            }
            catch
            {
                return Environment.MachineName;
            }
        }

        // -----------------------------------------------------------------
        //  AES helpers (keep encrypt/decrypt byte-array logic here so the
        //  caller does not need to manage streams)
        // -----------------------------------------------------------------
        private static byte[] EncryptBytes(Aes aes, byte[] plain)
        {
            using (var ms = new MemoryStream())
            using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
            {
                cs.Write(plain, 0, plain.Length);
                cs.FlushFinalBlock();
                return ms.ToArray();
            }
        }

        private static byte[] DecryptBytes(Aes aes, byte[] cipher)
        {
            using (var ms = new MemoryStream(cipher))
            using (var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read))
            using (var result = new MemoryStream())
            {
                cs.CopyTo(result);
                return result.ToArray();
            }
        }

        // -----------------------------------------------------------------
        //  HMAC helper
        // -----------------------------------------------------------------

        private static byte[] ComputeHmac(byte[] key, byte[] data)
        {
            using (var hmac = new HMACSHA256(key))
                return hmac.ComputeHash(data);
        }

        // -----------------------------------------------------------------
        //  Constant-time comparison (avoids timing side-channels)
        // -----------------------------------------------------------------

        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}