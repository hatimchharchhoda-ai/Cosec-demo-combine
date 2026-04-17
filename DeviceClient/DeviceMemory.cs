public static class DeviceMemory
{
    public static List<decimal> LastIds { get; set; } = new();
    public static DateTime?     LastT4  { get; set; }  // ← add this
}