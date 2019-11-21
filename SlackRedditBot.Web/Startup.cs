namespace SlackRedditBot.Web
{
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Reflection;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Options;
    using Newtonsoft.Json.Linq;
    using SlackRedditBot.Web.Models;
    using SlackRedditBot.Web.Processors;
    using SlackRedditBot.Web.Services;

    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            this.Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            var connectionString = this.Configuration.GetConnectionString("AppDbContext");

            services.Configure<AppSettings>(this.Configuration.GetSection("AppSettings"));
            services.AddDbContext<AppDbContext>(options => options.UseMySql(connectionString));
            services.AddSingleton<ObservableQueue<JObject>>();
            services.AddScoped(options => new HttpClient
            {
                DefaultRequestHeaders =
                {
                    UserAgent =
                    {
                        new ProductInfoHeaderValue(
                            options.GetService<IOptions<AppSettings>>().Value.ProductName,
                            Assembly.GetExecutingAssembly().GetName().Version.ToString()),
                    },
                },
            });
            services.AddScoped<RedditProcessor>();
            services.AddHostedService<RedditService>();
            services.AddControllersWithViews();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseHsts();
            }

            app.UseRouting();
            app.UseEndpoints(endpoints => endpoints.MapControllers());
        }
    }
}
