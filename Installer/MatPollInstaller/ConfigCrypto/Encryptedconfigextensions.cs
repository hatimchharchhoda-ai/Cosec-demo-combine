using System;
using System.Collections.Generic;
using System.Linq;
using ConfigCrypto;
using Microsoft.Extensions.Configuration;

namespace ConfigCrypto
{
    // ====================================================================== //
    //  IConfiguration extension                                               //
    //  Usage in Program.cs / Startup.cs:                                      //
    //                                                                          //
    //    builder.Configuration.DecryptEncryptedValues();                       //
    //                                                                          //
    //  This iterates every key in the configuration, detects "ENC:…" tokens,  //
    //  decrypts them in-place, and reloads the in-memory provider so the       //
    //  rest of the application sees plain values via IConfiguration and         //
    //  IOptions<T> as normal.  No application code needs to know about         //
    //  encryption at all.                                                       //
    // ====================================================================== //
    public static class EncryptedConfigExtensions
    {
        /// <summary>
        /// Walks every configuration key; decrypts values that start with "ENC:".
        /// Should be called once, early in Program.cs, before any services that
        /// consume IConfiguration are built.
        /// </summary>
        public static IConfigurationBuilder DecryptEncryptedValues(
            this IConfigurationBuilder builder)
        {
            // Build a temporary snapshot to enumerate keys
            IConfiguration snapshot = builder.Build();

            var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in snapshot.AsEnumerable())
            {
                if (ConfigCryptoHelper.IsEncrypted(kvp.Value))
                {
                    try
                    {
                        overrides[kvp.Key] = ConfigCryptoHelper.Decrypt(kvp.Value);
                    }
                    catch (Exception ex)
                    {
                        // Log and rethrow – a bad decrypt on startup should be fatal.
                        throw new InvalidOperationException(
                            $"Failed to decrypt configuration key '{kvp.Key}'. " +
                            "Ensure the application is running on the machine it was installed on " +
                            "and the ConfigCrypto pepper has not changed.", ex);
                    }
                }
            }

            if (overrides.Count > 0)
            {
                // Add an in-memory provider that shadows the encrypted values.
                // Because AddInMemoryCollection appended last wins, this cleanly
                // overrides the encrypted file values.
                builder.AddInMemoryCollection(overrides);
            }

            return builder;
        }

        // Convenience overload for IConfiguration (used after host is built,
        // e.g. for libraries that receive IConfiguration directly).
        public static string GetDecrypted(this IConfiguration config, string key)
        {
            string raw = config[key];
            return ConfigCryptoHelper.IsEncrypted(raw)
                ? ConfigCryptoHelper.Decrypt(raw)
                : raw;
        }
    }
}