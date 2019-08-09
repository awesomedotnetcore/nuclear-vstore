using System.Reflection;
using System.Threading.Tasks;

using Amazon;

using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;

using Autofac.Extensions.DependencyInjection;

using NuClear.VStore.Configuration;
using NuClear.VStore.Host.Logging;
using NuClear.VStore.Http.Core.Extensions;

using Serilog;
using Serilog.Events;

namespace NuClear.VStore.Host
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var webHost = CreateWebHostBuilder(args).Build();
            webHost.ConfigureThreadPool();

            ConfigureAwsLogging();
            await webHost.RunAsync();
        }

        private static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
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

        private static void ConfigureAwsLogging()
        {
            var log4NetLevel = Log.IsEnabled(LogEventLevel.Verbose) ? "ALL"
                               : Log.IsEnabled(LogEventLevel.Debug) ? "DEBUG"
                               : Log.IsEnabled(LogEventLevel.Information) ? "INFO"
                               : Log.IsEnabled(LogEventLevel.Warning) ? "WARN"
                               : Log.IsEnabled(LogEventLevel.Error) ? "ERROR"
                               : Log.IsEnabled(LogEventLevel.Fatal) ? "FATAL" : "OFF";

            var serilogAppender = new SerilogAppender(Log.Logger);
            serilogAppender.ActivateOptions();
            var log = log4net.LogManager.GetLogger(Assembly.GetEntryAssembly(), "Amazon");
            var wrapper = (log4net.Repository.Hierarchy.Logger)log.Logger;
            wrapper.Level = wrapper.Hierarchy.LevelMap[log4NetLevel];
            wrapper.AddAppender(serilogAppender);
            wrapper.Repository.Configured = true;

            AWSConfigs.LoggingConfig.LogTo = LoggingOptions.Log4Net;
            AWSConfigs.LoggingConfig.LogMetricsFormat = LogMetricsFormatOption.Standard;
        }
    }
}
