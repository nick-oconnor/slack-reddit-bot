namespace SlackRedditBot.Web.Controllers
{
    using System;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Options;
    using Models;
    using Newtonsoft.Json.Linq;

    public class EventController : Controller
    {
        private readonly AppSettings settings;
        private readonly ObservableQueue<JObject> requestQueue;

        public EventController(IOptions<AppSettings> options, ObservableQueue<JObject> requestQueue)
        {
            settings = options.Value;
            this.requestQueue = requestQueue;
        }

        [Route("event")]
        [HttpPost]
        public async Task<IActionResult> Event([FromBody] JObject requestObj)
        {
            try
            {
                try
                {
                    var timestmap = GetEventTimestamp();

                    await ValidateEventRequest(timestmap);
                }
                catch (Exception ex)
                {
                    return await GetBadRequestText(ex.Message);
                }

                var type = (string)requestObj["type"];

                switch (type)
                {
                    case "url_verification":
                        return new JsonResult(new { challenge = (string)requestObj["challenge"] });

                    case "event_callback":
                        requestQueue.Enqueue(requestObj);
                        return Ok();

                    default:
                        return await GetBadRequestText($"Unsupported event type '{type}'.");
                }
            }
            catch (Exception e)
            {
                return await GetErrorText(e);
            }
        }

        private static async Task<ContentResult> GetBadRequestText(string message)
        {
            await Console.Error.WriteLineAsync(message);

            return new ContentResult
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Content = message,
            };
        }

        private static async Task<ContentResult> GetErrorText(Exception e)
        {
            await Console.Error.WriteLineAsync(e.ToString());

            return new ContentResult
            {
                StatusCode = StatusCodes.Status500InternalServerError,
                Content = e.Message,
            };
        }

        private string GetEventTimestamp()
        {
            if (!Request.Headers.TryGetValue("X-Slack-Request-Timestamp", out var timestamp))
            {
                throw new Exception("'X-Slack-Request-Timestamp' header not present.");
            }

            var parsedTimestamp = DateTimeOffset.FromUnixTimeSeconds(int.Parse(timestamp)).UtcDateTime;
            var nowUtc = DateTime.UtcNow;

            if (nowUtc.Subtract(parsedTimestamp).TotalMinutes > 5)
            {
                throw new Exception("Message is older than 5 minutes.");
            }

            return timestamp;
        }

        private async Task ValidateEventRequest(string timestamp)
        {
            Request.Body.Position = 0;

            using (var bodyReader = new StreamReader(Request.Body))
            {
                var body = await bodyReader.ReadToEndAsync();
                var stringToHash = $"v0:{timestamp}:{body}";

                using (var algo = new HMACSHA256(Encoding.UTF8.GetBytes(settings.SigningSecret)))
                {
                    var hashBytes = algo.ComputeHash(Encoding.UTF8.GetBytes(stringToHash));
                    var hashHex = BitConverter.ToString(hashBytes).Replace("-", string.Empty).ToLower();
                    var calculatedSignature = $"v0={hashHex}";

                    if (!Request.Headers.TryGetValue("X-Slack-Signature", out var requestSignature))
                    {
                        throw new Exception("'X-Slack-Signature' header not present.");
                    }

                    if (calculatedSignature != requestSignature)
                    {
                        throw new Exception("Invalid message signature.");
                    }
                }
            }
        }
    }
}
