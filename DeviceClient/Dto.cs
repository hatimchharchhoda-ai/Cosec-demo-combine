public class PollResponse
{
    public bool HasData { get; set; }
    public bool NeedAckFirst { get; set; }
    public int TotalPending { get; set; }
    public string? TypeMID { get; set; }
    public List<TrnRow> Rows { get; set; } = new();
}

public class TrnRow
{
    public decimal TrnID { get; set; }
    public string? MsgStr { get; set; }
    public int RetryCnt { get; set; }
    public string? TypeMID { get; set; }
}