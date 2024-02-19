using System;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SkuV.Function
{
    public class SkuVault
    {
        private readonly ILogger _logger;
        private static bool isInitTriggered = false;
        private DateTime initTime = DateTimeOffset.Parse("1970-01-01T00:00:00.0000000Z").UtcDateTime;

        public SkuVault(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<SkuVault>();
        }

        [Function("SkuVault")]
        public async Task Run([TimerTrigger("* * */12 * * *", RunOnStartup = true)] TimerInfo myTimer)
        {
            _logger.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");

            //*-------------------Define Constants for API credential and Endpoints------------------------*//
            
            const string skuV_api = "https://app.skuvault.com/api/inventory/getAvailableQuantities";             
            const string tenant_token = "";
            const string user_token = "";

            const int page_size = 1000;

            const string mid_auth_api = "https://sandboxapi.midwayplus.com/v1/Auth";
            const string mid_client_id = "";
            const string mid_password = "";

            const string mid_invUp_api =  "https://sandboxapi.midwayplus.com/v1/Inventory/Update";

            //*------------------Data Cracking from SkuVault via its API: /getAvailableQuantities--------------------*//

            var client = new HttpClient();

            var loop_flag_pages = true;
            List<Item> all_items = new List<Item>();

            DateTime curTime = DateTime.UtcNow;
            DateTime twlAgoTime = curTime.AddHours(-12);
            
            if(!isInitTriggered) {
                twlAgoTime = initTime;
                isInitTriggered = true;
            }

            SkuVBody skuV_data = new SkuVBody {
                ExpandAlternateSkus = false,
                ModifiedAfterDateTimeUtc = twlAgoTime,
                ModifiedBeforeDateTimeUtc = curTime,
                PageNumber = 0,
                PageSize = page_size,
                TenantToken = tenant_token,
                UserToken = user_token
            };

            while(loop_flag_pages) {
                var skuV_Msg = new HttpRequestMessage(HttpMethod.Post, skuV_api);
                skuV_Msg.Headers.Add("Accept", "application/json");
                
                var skuV_data_json = JsonConvert.SerializeObject(skuV_data);
                skuV_Msg.Content = new StringContent(skuV_data_json, Encoding.UTF8, "application/json");

                var res_skuV = await client.SendAsync(skuV_Msg);
                var temp_skuV_data = await res_skuV.Content.ReadAsStringAsync();
                var items_from_skuV = JsonConvert.DeserializeObject<SkuVRes>(temp_skuV_data);
                if(items_from_skuV != null) {
                    if(items_from_skuV.Items.Count() > page_size - 1)
                        skuV_data.PageNumber += 1;
                    else
                        loop_flag_pages = false;    
                    if(items_from_skuV.Items.Count()>0)
                    all_items.AddRange(items_from_skuV.Items);                        
                }
            }

            //*------------------Authentication Token from Sandbox API: /v1/Auth--------------------*//
            
            var auth_token_Msg = new HttpRequestMessage(HttpMethod.Post, mid_auth_api);
            auth_token_Msg.Headers.Add("Accept", "application/json");

            AuthBody authBody = new AuthBody {
                clientId = mid_client_id,
                password = mid_password
            };
            
            var auth_body_json = JsonConvert.SerializeObject(authBody);
            auth_token_Msg.Content = new StringContent(auth_body_json, Encoding.UTF8, "application/json");

            var res_auth = await client.SendAsync(auth_token_Msg);
            var temp_auth_token = await res_auth.Content.ReadAsStringAsync();
            var auth_token = JsonConvert.DeserializeObject<AuthRes>(temp_auth_token);   
            var token = "Bearer ";
            if(auth_token != null){
                token += auth_token.token;
            }

            //*------------------Post Data to Sandbox API: /v1/Inventory/Update--------------------*//

            var post_data_Msg = new HttpRequestMessage(HttpMethod.Post, mid_invUp_api);
            post_data_Msg.Headers.Add("Authorization", token);

            List<PostItem> post_items = new List<PostItem>();
            foreach(Item i in all_items) {
                PostItem item = new PostItem{
                    partNumber = i.Sku,
                    stockLevel = i.AvailableQuantity,
                    buildTimeDays = null,
                    backInStockDate = null
                };
                post_items.Add(item);
            }
            
            var post_data_json = JsonConvert.SerializeObject(post_items);
            post_data_Msg.Content = new StringContent(post_data_json, Encoding.UTF8, "application/json");

            var res_post = await client.SendAsync(post_data_Msg);
            var temp_post_data = await res_post.Content.ReadAsStringAsync();

            Console.WriteLine(temp_post_data);

            if (myTimer.ScheduleStatus is not null)
            {
                _logger.LogInformation($"Next timer schedule at: {myTimer.ScheduleStatus.Next}");
            }
        }
    }

    public class SkuVBody
    {
        public bool ExpandAlternateSkus { get; set; }
        public DateTime ModifiedAfterDateTimeUtc { get; set; }
        public DateTime ModifiedBeforeDateTimeUtc { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public required string TenantToken { get; set; }
        public required string UserToken { get; set; }
    }
    public class Item
    {
        public bool IsAlternateSku { get; set; }
        public required string Sku { get; set; }
        public int AvailableQuantity { get; set; }
        public DateTime LastModifiedDateTimeUtc { get; set; }
    }
    public class SkuVRes {
        public required List<Item> Items {get; set;}
    }
    public class AuthBody
    {
        public required string clientId { get; set; }
        public required string password { get; set; }
    }
    public class AuthRes {
        public required string token {get; set;}
    }
    public class PostItem
    {
        public required string partNumber { get; set; }
        public int stockLevel { get; set; }
        public int? buildTimeDays { get; set; }
        public DateTime? backInStockDate { get; set; }
    }

}
