namespace COSEC_demo.DTOs
{
    public class CommTrnResponseDto
    {
        public int TrnID { get; set; }
        public string MsgStr { get; set; }
        public int RetryCnt { get; set; }
        public int TrnStat { get; set; }
    }
}
