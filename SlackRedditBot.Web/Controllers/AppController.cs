using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SlackRedditBot.Web.Models;

namespace SlackRedditBot.Web.Controllers
{
    public class AppController : Controller
    {
        private readonly AppSettings _settings;
        private readonly AppDbContext _db;
        private readonly HttpClient _httpClient;
        private readonly ILogger<AppController> _logger;

        public AppController(AppDbContext db, IOptions<AppSettings> options, HttpClient httpClient,
            ILogger<AppController> logger)
        {
            _db = db;
            _settings = options.Value;
            _httpClient = httpClient;
            _logger = logger;
        }

        [HttpGet("")]
        public IActionResult Home()
        {
            return View(_settings);
        }

        [HttpGet("install")]
        public IActionResult Install()
        {
            try
            {
                return Redirect("https://slack.com/oauth/authorize?" +
                                     $"client_id={_settings.ClientId}" +
                                     $"&scope={_settings.Scopes}" +
                                     $"&state={GetAuthState()}");
            }
            catch (Exception e)
            {
                return GetErrorView(e);
            }
        }

        [HttpGet("authorize")]
        public async Task<IActionResult> Authorize(string code, string state, string error,
            CancellationToken cancellationToken)
        {
            try
            {
                ValidateAuthState(state);

                if (error == "access_denied")
                {
                    throw new Exception("Permissions not accepted.");
                }

                var instance = await GetAppInstance(code, cancellationToken);

                await SaveAppInstance(instance, cancellationToken);

                return Redirect($"https://slack.com/app_redirect?app={_settings.AppId}");
            }
            catch (Exception e)
            {
                return GetErrorView(e);
            }
        }

        private ViewResult GetErrorView(Exception e)
        {
            _logger.LogError(e.ToString());

            return View("Error", e);
        }

        private string GetAuthState()
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.ClientSecret));

            return tokenHandler.CreateEncodedJwt(new SecurityTokenDescriptor
            {
                Expires = DateTime.UtcNow + TimeSpan.FromMinutes(10),
                SigningCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256Signature)
            });
        }

        private void ValidateAuthState(string state)
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
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.ClientSecret))
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
        }

        private async Task<Instance> GetAppInstance(string code, CancellationToken cancellationToken)
        {
            var basicAuth =
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            var formValues = new Dictionary<string, string> { { "code", code } };

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://slack.com/api/oauth.access")
            {
                Headers = { Authorization = new AuthenticationHeaderValue("Basic", basicAuth) },
                Content = new FormUrlEncodedContent(formValues)
            };
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync();
            var responseObj = (JObject)JsonConvert.DeserializeObject(responseBody);

            if (!(bool)responseObj["ok"])
            {
                throw new Exception($"Error retrieving access token from slack: {responseBody}");
            }

            return new Instance
            {
                TeamId = (string)responseObj["team_id"],
                AccessToken = (string)responseObj["access_token"]
            };
        }

        private async Task SaveAppInstance(Instance instance, CancellationToken cancellationToken)
        {
            var dbInstance =
                await _db.Instances.SingleOrDefaultAsync(i => i.TeamId == instance.TeamId, cancellationToken);

            if (dbInstance == null)
            {
                _db.Instances.Add(instance);
            }
            else
            {
                dbInstance.AccessToken = instance.AccessToken;
            }

            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}
