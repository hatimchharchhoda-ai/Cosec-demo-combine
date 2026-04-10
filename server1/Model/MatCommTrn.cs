namespace MatGenServer.Models;

public class Mat_CommTrn
{
    public decimal TrnID { get; set; }
    public string? MsgStr { get; set; }
    public int RetryCnt { get; set; }

    /// <summary>
    /// 0 = Pending (send to client)
    /// 1 = Dispatched (sent, waiting ACK)
    /// 2 = Acknowledged (client confirmed)
    /// 9 = Failed
    /// </summary>
    public int TrnStat { get; set; }
    public DateTime? DispatchedAt { get; set; }
    public string? DispatchedToDeviceID { get; set; }
}