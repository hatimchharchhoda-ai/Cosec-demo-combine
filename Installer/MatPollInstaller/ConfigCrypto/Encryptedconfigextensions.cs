using System;
using System.Collections.Generic;
using System.Linq;
using ConfigCrypto;
using Microsoft.Extensions.Configuration;

namespace ConfigCrypto
{
    public static class EncryptedConfigExtensions
    {
        public static IConfigurationBuilder DecryptEncryptedValues(
            this IConfigurationBuilder builder)
        {
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
                        throw new InvalidOperationException(
                            $"Failed to decrypt configuration key '{kvp.Key}'. " +
                            "Ensure the application is running on the machine it was installed on " +
                            "and the ConfigCrypto pepper has not changed.", ex);
                    }
                }
            }

            if (overrides.Count > 0)
            {
                builder.AddInMemoryCollection(overrides);
            }

            return builder;
        }

        public static string GetDecrypted(this IConfiguration config, string key)
        {
            string raw = config[key];
            return ConfigCryptoHelper.IsEncrypted(raw)
                ? ConfigCryptoHelper.Decrypt(raw)
                : raw;
        }
    }
}