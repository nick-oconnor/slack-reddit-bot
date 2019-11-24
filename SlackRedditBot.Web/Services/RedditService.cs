namespace SlackRedditBot.Web.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json.Linq;
    using SlackRedditBot.Web.Models;
    using SlackRedditBot.Web.Processors;

    public class RedditService : BackgroundService
    {
        private readonly IServiceProvider serviceProvider;
        private readonly ObservableQueue<JObject> requestQueue;
        private readonly ILogger<RedditService> logger;

        public RedditService(IServiceProvider serviceProvider, ObservableQueue<JObject> requestQueue, ILogger<RedditService> logger)
        {
            this.serviceProvider = serviceProvider;
            this.requestQueue = requestQueue;
            this.logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var tasks = new List<Task>();

            while (!stoppingToken.IsCancellationRequested)
            {
                var requestObj = await this.requestQueue.Dequeue(stoppingToken);

                tasks.Add(this.ProcessEvent(requestObj, stoppingToken));
                tasks.RemoveAll(t => t.IsCompleted);
            }

            await Task.WhenAll(tasks);
        }

        private async Task ProcessEvent(JObject requestObj, CancellationToken stoppingToken)
        {
            try
            {
                using var scope = this.serviceProvider.CreateScope();
                await scope.ServiceProvider.GetRequiredService<RedditProcessor>()
.ProcessRequest(requestObj, stoppingToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                this.logger.LogError(e.ToString());
            }
        }
    }
}
