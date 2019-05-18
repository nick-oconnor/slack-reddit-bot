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
    using Microsoft.Extensions.Options;
    using Models;
    using Newtonsoft.Json.Linq;
    using Services;

    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            var connectionString = Configuration.GetConnectionString("AppDbContext");

            services.Configure<AppSettings>(Configuration.GetSection("AppSettings"));
            services.AddDbContext<AppDbContext>(options => options.UseMySql(connectionString));
            services.AddSingleton(options => new HttpClient
            {
                DefaultRequestHeaders =
                {
                    UserAgent =
                    {
                        new ProductInfoHeaderValue(
                            options.GetService<IOptionsMonitor<AppSettings>>().CurrentValue.ProductName,
                            Assembly.GetExecutingAssembly().GetName().Version.ToString())
                    }
                }
            });
            services.AddSingleton<ObservableQueue<JObject>>();
            services.AddHostedService<RedditService>();
            services.AddMvc(options =>
            {
                options.Filters.Add(new ResponseCacheAttribute
                {
                    NoStore = true,
                    Location = ResponseCacheLocation.None
                });
            }).SetCompatibilityVersion(CompatibilityVersion.Version_2_1);
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseHsts();
            }

            app.UseMvc();
        }
    }
}
