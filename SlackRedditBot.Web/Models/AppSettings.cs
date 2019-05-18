namespace SlackRedditBot.Web.Models
{
    public class AppSettings
    {
        public string AppId { get; set; }

        public string ClientId { get; set; }

        public string ClientSecret { get; set; }

        public string SigningSecret { get; set; }

        public string Scopes { get; set; }

        public string DisplayName { get; set; }

        public string ProductName { get; set; }

        public string Subreddit { get; set; }

        public string[] ImageExtensions { get; set; }

        public string[] Triggers { get; set; }
    }
}