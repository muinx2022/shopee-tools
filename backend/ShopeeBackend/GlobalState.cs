using System.ComponentModel;
using ShopeeBackend.Models;

namespace ShopeeBackend
{
    public static class GlobalState
    {
        public static BindingList<AccountInfo> Accounts { get; } = new BindingList<AccountInfo>();
        
        public static BindingList<ProxyInfo> Proxies { get; } = new BindingList<ProxyInfo>
        {
            new ProxyInfo { Id = "none", Name = "None", ApiKey = "" }
        };
        
        public static BindingList<CategoryInfo> Categories { get; } = new BindingList<CategoryInfo>();
        public static BindingList<KeywordInfo> Keywords { get; } = new BindingList<KeywordInfo>();

        public static BindingList<ScrapedProduct> ScrapedProducts { get; } = new BindingList<ScrapedProduct>();

        public static int AutoRerunDays { get; set; } = 7;

        public static string ImageStorageFolder { get; set; } = @"e:\images\phan-tich-shopee";
        public static bool AutoDownloadImages { get; set; } = true;

        public static int MaxConcurrentFlows { get; set; } = 3;
    }
}
