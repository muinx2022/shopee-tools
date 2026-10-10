namespace ShopeeBackend.Models
{
    public class ProxyInfo
    {
        public string Id { get; set; } = System.Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty; // KiotProxy API Key
    }
}
