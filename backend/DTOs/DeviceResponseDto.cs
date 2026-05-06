namespace COSEC_demo.DTOs
{
    public class DeviceResponseDto
    {
        public int DeviceID { get; set; }
        public string DeviceName { get; set; }
        public string MACAddr { get; set; }
        public string IPAddr { get; set; }
        public int DeviceType { get; set; }
        public bool IsActive { get; set; }
    }
}
