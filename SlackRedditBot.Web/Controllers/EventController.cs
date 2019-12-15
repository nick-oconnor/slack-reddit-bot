using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SlackRedditBot.Web.Models;

namespace SlackRedditBot.Web.Controllers
{
    public class EventController : Controller
    {
        private readonly AppSettings _settings;
        private readonly Channel<JObject> _requestChannel;
        private readonly ILogger<EventController> _logger;

        public EventController(IOptions<AppSettings> options, Channel<JObject> requestChannel,
            ILogger<EventController> logger)
        {
            _settings = options.Value;
            _requestChannel = requestChannel;
            _logger = logger;
        }

        [HttpPost("event")]
        public async Task<IActionResult> Event()
        {
            try
            {
                Request.Body.Position = 0;

                using var bodyReader = new StreamReader(Request.Body);
                var body = await bodyReader.ReadToEndAsync();

                try
                {
                    ValidateEventRequest(body);
                }
                catch (Exception ex)
                {
                    return GetBadRequestText(ex.Message);
                }

                var requestObj = (JObject)JsonConvert.DeserializeObject(body);
                var type = (string)requestObj["type"];

                switch (type)
                {
                    case "url_verification":
                        return new JsonResult(new { challenge = (string)requestObj["challenge"] });

                    case "event_callback":
                        await _requestChannel.Writer.WriteAsync(requestObj);
                        return Ok();

                    default:
                        return GetBadRequestText($"Unsupported event type '{type}'.");
                }
            }
            catch (Exception e)
            {
                return GetErrorText(e);
            }
        }

        private ContentResult GetBadRequestText(string message)
        {
            _logger.LogError(message);

            return new ContentResult
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Content = message
            };
        }

        private ContentResult GetErrorText(Exception e)
        {
            _logger.LogError(e.ToString());

            return new ContentResult
            {
                StatusCode = StatusCodes.Status500InternalServerError,
                Content = e.Message
            };
        }

        private void ValidateEventRequest(string body)
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

            var stringToHash = $"v0:{timestamp}:{body}";

            using var algo = new HMACSHA256(Encoding.UTF8.GetBytes(_settings.SigningSecret));
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
