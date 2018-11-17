using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Hermes.Hubs;
using Hermes.Models;
using Hermes.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.CognitiveServices.Language.LUIS.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Hermes
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<CookiePolicyOptions>(options =>
            {
                // This lambda determines whether user consent for non-essential cookies is needed for a given request.
                options.CheckConsentNeeded = context => true;
                options.MinimumSameSitePolicy = SameSiteMode.None;
            });

            var luisSettings = new LuisSettings();
            Configuration.GetSection("luisSettings").Bind(luisSettings);
            services.AddSingleton(luisSettings);
            
            services.AddSingleton<ILUISRuntimeClient>(
                new LUISRuntimeClient(
                    new ApiKeyServiceClientCredentials(luisSettings.ApiKey))
                {
                    Endpoint = luisSettings.Endpoint
                });

            using (var fs = new FileStream("VoiceConfig.json", FileMode.Open))
            {
                using (var ss = new StreamReader(fs))
                {
                    var configStr = ss.ReadToEnd();
                    var config = JsonConvert.DeserializeObject<Settings.VoiceConfig>(configStr);
                    services.AddSingleton(config);
                }
            }

            services.AddSingleton<IDictionary<string, CallState>>(new Dictionary<string, CallState>());

            services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_2_1);

            services.AddSignalR();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

//            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseCookiePolicy();

            app.UseSignalR(routes =>
            {
                routes.MapHub<CallActivityHub>("/callActivityHub");
            });
            app.UseMvc(routes =>
            {
                routes.MapRoute(
                    name: "default",
                    template: "{controller=Home}/{action=Index}/{id?}");
            });
        }
    }
}