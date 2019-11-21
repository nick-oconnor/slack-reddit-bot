namespace SlackRedditBot.Web.Processors
{
    using System;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Options;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using SlackRedditBot.Web.Models;

    public class RedditProcessor
    {
        private readonly AppDbContext db;
        private readonly AppSettings settings;
        private readonly HttpClient httpClient;

        public RedditProcessor(AppDbContext db, IOptions<AppSettings> options, HttpClient httpClient)
        {
            this.db = db;
            this.settings = options.Value;
            this.httpClient = httpClient;
        }

        public async Task ProcessRequest(JObject requestObj, CancellationToken stoppingToken)
        {
            var teamId = (string)requestObj["team_id"];
            var instance = await this.db.Instances.SingleOrDefaultAsync(i => i.TeamId == teamId, stoppingToken);
            var eventObj = (JObject)requestObj["event"];
            var type = (string)eventObj["type"];

            if (instance == null)
            {
                throw new Exception($"App not installed for team '{teamId}'.");
            }

            switch (type)
            {
                case "app_uninstalled":
                    this.db.Instances.Remove(instance);
                    await this.db.SaveChangesAsync(stoppingToken);
                    return;

                case "message":
                    await this.ProcessMessage(eventObj, instance, stoppingToken);
                    return;

                default:
                    throw new Exception($"Unsupported event callback type '{type}'.");
            }
        }

        private async Task ProcessMessage(JObject eventObj, Instance instance, CancellationToken stoppingToken)
        {
            var subtype = (string)eventObj["subtype"];
            var text = (string)eventObj["text"];

            if (subtype != null || !this.settings.Triggers.Any(w =>
                    text.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return;
            }

            var channel = (string)eventObj["channel"];
            var birdUrl = await this.GetRandomBirdUrl(stoppingToken);

            await this.PostResponse(channel, instance.AccessToken, birdUrl, stoppingToken);
        }

        private async Task PostResponse(string channel, string bearerToken, string text, CancellationToken cancellationToken)
        {
            var jsonContent = JsonConvert.SerializeObject(new { channel, text });

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://slack.com/api/chat.postMessage")
            {
                Headers = { Authorization = new AuthenticationHeaderValue("Bearer", bearerToken) },
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json"),
            };
            using var response = await this.httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync();
            var responseObj = (JObject)JsonConvert.DeserializeObject(responseBody);

            if (!(bool)responseObj["ok"])
            {
                throw new Exception($"Error posting response to slack: {responseBody}");
            }
        }

        private async Task<string> GetRandomBirdUrl(CancellationToken cancellationToken)
        {
            string imageUrl;

            do
            {
                using var response = await this.httpClient.GetAsync($"https://www.reddit.com/r/{this.settings.Subreddit}/random.json", cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    throw new Exception($"Error retrieving random post for subreddit: {responseBody}");
                }

                var responseObj = (JArray)JsonConvert.DeserializeObject(responseBody);

                imageUrl = (string)responseObj[0]["data"]["children"][0]["data"]["url"];
            }
            while (!this.settings.ImageExtensions.Any(e => imageUrl.EndsWith($".{e}")));

            return imageUrl;
        }
    }
}
