namespace ShopeeBackend.Models
{
    public class AccountInfo
    {
        public string Id { get; set; } = System.Guid.NewGuid().ToString();
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Cookie { get; set; } = string.Empty; // Holds SPC_F=...
        
        public string ProfileName { get; set; } = string.Empty; // e.g. Profile_Username
        public string Status { get; set; } = "Ready"; // "Ready", "Captcha", "Error", "Done"
    }
}
