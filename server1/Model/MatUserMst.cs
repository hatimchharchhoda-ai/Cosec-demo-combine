namespace MatGenServer.Models;

public class Mat_UserMst
{
    public string UserID { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public int IsActive { get; set; }
    public string? UserShortName { get; set; }
    public decimal UserIDN { get; set; }
}
