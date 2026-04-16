using Microsoft.Extensions.Configuration;

public static class DeviceConfigLoader
{
    public static DeviceConfig Load()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("deviceconfig.json", optional: false)
            .Build();

        return config.Get<DeviceConfig>()!;
    }
}