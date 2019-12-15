using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using SlackRedditBot.Web.Processors;

namespace SlackRedditBot.Web.Services
{
    public class RedditService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly Channel<JObject> _requestChannel;
        private readonly ILogger<RedditService> _logger;

        public RedditService(IServiceProvider serviceProvider, Channel<JObject> requestChannel,
            ILogger<RedditService> logger)
        {
            _serviceProvider = serviceProvider;
            _requestChannel = requestChannel;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var tasks = new List<Task>();

            while (!stoppingToken.IsCancellationRequested)
            {
                var requestObj = await _requestChannel.Reader.ReadAsync(stoppingToken);

                tasks.Add(ProcessEvent(requestObj, stoppingToken));
                tasks.RemoveAll(t => t.IsCompleted);
            }

            await Task.WhenAll(tasks);
        }

        private async Task ProcessEvent(JObject requestObj, CancellationToken stoppingToken)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                await scope.ServiceProvider.GetRequiredService<RedditProcessor>()
                    .ProcessRequest(requestObj, stoppingToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                _logger.LogError(e.ToString());
            }
        }
    }
}
