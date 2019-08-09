using System.Threading.Tasks;

using Autofac.Extensions.DependencyInjection;

using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;

using NuClear.VStore.Configuration;
using NuClear.VStore.Http.Core.Extensions;

using Serilog;

namespace NuClear.VStore.Renderer
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var webHost = CreateWebHostBuilder(args).Build();
            webHost.ConfigureThreadPool();
            await webHost.RunAsync();
        }

        public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                   .UseSockets(options => options.IOQueueCount = 0)
                   .ConfigureServices(services => services.AddAutofac())
                   .ConfigureAppConfiguration((hostingContext, config) =>
                                                  {
                                                      var env = hostingContext.HostingEnvironment;
                                                      config.UseDefaultConfiguration(env.ContentRootPath, env.EnvironmentName);
                                                  })
                   .UseStartup<Startup>()
                   .UseSerilog((context, configuration) => configuration.ReadFrom.Configuration(context.Configuration));
    }
}