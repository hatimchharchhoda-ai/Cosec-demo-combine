namespace MatGenServer.Models;

public class Mat_DeviceMst
{
    public int DeviceID { get; set; }
    public string? DeviceName { get; set; }
    public string? MACAddr { get; set; }
    public string? IPAddr { get; set; }
    public bool IsActive { get; set; }
    public string? DeviceType { get; set; }
}
