using System.Security.Cryptography;
using System.Text;

namespace MatPoll.Services;

// TypeMID = a short unique ID derived from MAC + IP
// Example: MAC=AA:BB:CC:DD:EE:01, IP=192.168.1.101
//          → MD5("AA:BB:CC:DD:EE:01|192.168.1.101") → first 12 chars → "a3f9bc12d8e1"
//
// WHY:
//   - Tells you WHICH DEVICE a CommTrn row was dispatched to
//   - Stable — same device always gets the same TypeMID
//   - Compact — 12 chars stored in the column
//   - Used for: filtering by device, restore, logging
//
// NOTE: TypeMID is NOT secret. It is just an identifier.

public static class TypeMidService
{
    // Generate TypeMID from MAC + IP
    public static string Generate(string mac, string ip)
    {
        var raw   = $"{mac.ToUpperInvariant()}|{ip.Trim()}";
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(raw));
        // Take first 6 bytes → 12 hex chars
        return Convert.ToHexString(bytes).Substring(0, 12).ToLowerInvariant();
    }
}
