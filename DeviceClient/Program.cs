class Program
{
    static async Task Main()
    {
        var api = new ApiClient();

        Console.Write("DeviceID: ");
        int deviceId = int.Parse(Console.ReadLine()!);

        Console.Write("MAC Address: ");
        string mac = Console.ReadLine()!;

        Console.Write("IP Address: ");
        string ip = Console.ReadLine()!;

        Console.Write("Connect to server? (y/n): ");
        if (Console.ReadLine()?.ToLower() != "y")
            return;

        await api.Login(deviceId, mac, ip);

        await api.Restore();

        while (true)
        {
            await api.PollAndProcess();
            await Task.Delay(8000);
        }
    }
}