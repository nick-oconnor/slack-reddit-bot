namespace SlackRedditBot.Web.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Models;
    using Newtonsoft.Json.Linq;
    using Processors;

    public class RedditService : BackgroundService
    {
        private readonly IServiceProvider serviceProvider;
        private readonly ObservableQueue<JObject> requestQueue;

        public RedditService(IServiceProvider serviceProvider, ObservableQueue<JObject> requestQueue)
        {
            this.serviceProvider = serviceProvider;
            this.requestQueue = requestQueue;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var tasks = new List<Task>();

            while (!stoppingToken.IsCancellationRequested)
            {
                var requestObj = await requestQueue.Dequeue(stoppingToken);

                tasks.Add(ProcessEvent(requestObj, stoppingToken));
                tasks.RemoveAll(t => t.IsCompleted);
            }

            await Task.WhenAll(tasks);
        }

        private async Task ProcessEvent(JObject requestObj, CancellationToken stoppingToken)
        {
            try
            {
                using (var scope = serviceProvider.CreateScope())
                {
                    await scope.ServiceProvider.GetRequiredService<RedditProcessor>()
                        .ProcessRequest(requestObj, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                await Console.Error.WriteLineAsync(e.ToString());
            }
        }
    }
}
