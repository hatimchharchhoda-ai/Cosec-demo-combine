namespace COSEC_demo.DTOs
{
    public class UserDto
    {
        public string UserId { get; set; } = string.Empty;
        public string? UserName { get; set; }
        public bool IsActive { get; set; }
        public string? UserShortName { get; set; }
        public decimal? UserIDN { get; set; }
    }

    public class UserListResponseDto
    {
        public List<UserDto> Users { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
    }
}
