namespace SlackRedditBot.Web.Controllers
{
    using System;
    using System.Collections.Generic;
    using System.IdentityModel.Tokens.Jwt;
    using System.IO;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Options;
    using Microsoft.IdentityModel.Tokens;
    using Models;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;

    public class AppController : Controller
    {
        private readonly AppSettings settings;
        private readonly AppDbContext db;
        private readonly HttpClient httpClient;
        private readonly ObservableQueue<JObject> requestQueue;

        public AppController(
            AppDbContext db, IOptions<AppSettings> options, HttpClient httpClient, ObservableQueue<JObject> requestQueue)
        {
            this.db = db;
            settings = options.Value;
            this.httpClient = httpClient;
            this.requestQueue = requestQueue;
        }

        [Route("")]
        [HttpGet]
        public IActionResult About()
        {
            return View("Home", settings);
        }

        [Route("install")]
        [HttpGet]
        public async Task<IActionResult> Install()
        {
            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.ClientSecret));
                var state = tokenHandler.CreateEncodedJwt(new SecurityTokenDescriptor
                {
                    Expires = DateTime.UtcNow + TimeSpan.FromMinutes(10),
                    SigningCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256Signature)
                });

                return Redirect($"https://slack.com/oauth/authorize?client_id={settings.ClientId}&scope={settings.Scopes}&state={state}");
            }
            catch (Exception e)
            {
                return await GetErrorView(e);
            }
        }

        [Route("authorize")]
        [HttpGet]
        public async Task<IActionResult> Authorize(string code, string state, string error, CancellationToken cancellationToken)
        {
            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();

                if (!tokenHandler.CanReadToken(state))
                {
                    throw new Exception("Invalid request.");
                }

                var validationParams = new TokenValidationParameters
                {
                    ValidateAudience = false,
                    ValidateIssuer = false,
                    ClockSkew = TimeSpan.Zero,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.ClientSecret))
                };

                try
                {
                    tokenHandler.ValidateToken(state, validationParams, out _);
                }
                catch (SecurityTokenInvalidSignatureException)
                {
                    throw new Exception("Invalid signature.");
                }
                catch (SecurityTokenExpiredException)
                {
                    throw new Exception("Expired signature.");
                }

                if (error == "access_denied")
                {
                    throw new Exception("Permissions not accepted.");
                }

                var basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.ClientId}:{settings.ClientSecret}"));
                var formValues = new Dictionary<string, string> { { "code", code } };

                using (var request = new HttpRequestMessage(HttpMethod.Post, "https://slack.com/api/oauth.access")
                {
                    Headers = { Authorization = new AuthenticationHeaderValue("Basic", basicAuth) },
                    Content = new FormUrlEncodedContent(formValues)
                })
                using (var response = await httpClient.SendAsync(request, cancellationToken))
                {
                    var responseBody = await response.Content.ReadAsStringAsync();
                    var responseObj = (JObject)JsonConvert.DeserializeObject(responseBody);

                    if (!(bool)responseObj["ok"])
                    {
                        throw new Exception($"Error retrieving access token from slack: {responseBody}");
                    }

                    var teamId = (string)responseObj["team_id"];
                    var instance = await db.Instances.SingleOrDefaultAsync(i => i.TeamId == teamId, cancellationToken);

                    if (instance == null)
                    {
                        instance = new Instance { TeamId = teamId };
                        db.Instances.Add(instance);
                    }

                    instance.AccessToken = (string)responseObj["access_token"];
                    await db.SaveChangesAsync(cancellationToken);
                }

                return Redirect($"https://slack.com/app_redirect?app={settings.AppId}");
            }
            catch (Exception e)
            {
                return await GetErrorView(e);
            }
        }

        [Route("event")]
        [HttpPost]
        public async Task<IActionResult> Event([FromBody] JObject requestObj, CancellationToken cancellationToken)
        {
            try
            {
                if (!Request.Headers.TryGetValue("X-Slack-Request-Timestamp", out var timestamp))
                {
                    return await GetBadRequestText("'X-Slack-Request-Timestamp' header not present.");
                }

                var parsedTimestamp = DateTimeOffset.FromUnixTimeSeconds(int.Parse(timestamp)).UtcDateTime;
                var nowUtc = DateTime.UtcNow;

                if (nowUtc.Subtract(parsedTimestamp).TotalMinutes > 5)
                {
                    return await GetBadRequestText("Message is older than 5 minutes.");
                }

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
                            return await GetBadRequestText("'X-Slack-Signature' header not present.");
                        }

                        if (calculatedSignature != requestSignature)
                        {
                            return await GetBadRequestText("Invalid message signature.");
                        }
                    }
                }

                var type = (string)requestObj["type"];

                switch (type)
                {
                    case "url_verification":
                        {
                            var challenge = (string)requestObj["challenge"];

                            return new JsonResult(new { challenge });
                        }

                    case "event_callback":
                        {
                            requestQueue.Enqueue(requestObj);

                            return Ok();
                        }

                    default:
                        {
                            return await GetBadRequestText($"Unsupported event type '{type}'.");
                        }
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

        private async Task<ViewResult> GetErrorView(Exception e)
        {
            await Console.Error.WriteLineAsync(e.ToString());

            return View("Error", e);
        }
    }
}
