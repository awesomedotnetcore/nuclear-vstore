using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Autofac;
using Autofac.Extensions.DependencyInjection;

using CloningTool.Auth;
using CloningTool.CloneStrategies;
using CloningTool.RestClient;

using Serilog;

namespace CloningTool
{
    public class Program
    {
        private const string Source = "Source";
        private const string Destination = "Destination";
        private const string OptionsParameterName = "options";
        private const string ApiUriParameterName = "apiUri";
        private const string AuthServiceParameterName = "authService";
        private const string ApiVersionParameterName = "apiVersion";

        private static IConfigurationRoot Configuration { get; set; }

        public static async Task<int> Main(string[] args)
        {
            var env = Environment.GetEnvironmentVariable("AMS_ENVIRONMENT") ?? "Production";
            var basePath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            Configuration = new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile("appsettings.json")
                .AddJsonFile($"appsettings.{env.ToLower()}.json", true)
                .AddEnvironmentVariables("AMS_")
                .Build();

            var container = Bootstrap();

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(Configuration)
                .CreateLogger();

            var loggerFactory = container.Resolve<ILoggerFactory>()
                                         .AddSerilog();

            Console.CancelKeyPress += (sender, eventArgs) => Log.CloseAndFlush();

            var logger = loggerFactory.CreateLogger<Program>();
            var exitCode = await RunCloning(container, logger);

            Log.CloseAndFlush();

            return exitCode;
        }

        private static async Task<int> RunCloning(IContainer container, Microsoft.Extensions.Logging.ILogger logger)
        {
            var sourceUri = new Uri(Configuration.GetConnectionString(Source));
            var destUri = new Uri(Configuration.GetConnectionString(Destination));

            var options = container.Resolve<CloningToolOptions>();

            options.AdvertisementsCreatedAtBeginDate = ConvertDateParameterToUniversalTime(logger, nameof(options.AdvertisementsCreatedAtBeginDate), options.AdvertisementsCreatedAtBeginDate);

            logger.LogInformation("Start to clone from {source} to {dest} in mode: {mode}", sourceUri, destUri, options.Mode);
            try
            {
                var cloningService = container.Resolve<CloningService>();
                if (await cloningService.CloneAsync(options.Mode))
                {
                    logger.LogInformation("Cloning from {source} to {dest} finished successfully", sourceUri, destUri);
                }
                else
                {
                    logger.LogError("Cloning from {source} to {dest} wasn't complete", sourceUri, destUri);
                    return 3;
                }
            }
            catch (Exception ex)
            {
                logger.LogCritical(default, ex, "Fatal error while cloning from {source} to {dest} in {mode} mode", sourceUri, destUri, options.Mode);
                return 2;
            }

            return 0;
        }

        private static IContainer Bootstrap()
        {
            var aimConfigSection = Configuration.GetSection("Aim");
            var services = new ServiceCollection()
                .AddOptions()
                .Configure<CloningToolOptions>(Configuration.GetSection("CloningTool"))
                .Configure<AimOptions>(Source, aimConfigSection.GetSection(Source))
                .Configure<AimOptions>(Destination, aimConfigSection.GetSection(Destination))
                .AddLogging();

            var builder = new ContainerBuilder();
            builder.Populate(services);

            builder.Register(x => x.Resolve<IOptions<CloningToolOptions>>().Value)
                   .SingleInstance();

            builder.Register(x => x.Resolve<IOptions<AimOptions>>().Value)
                   .SingleInstance();

            builder.RegisterType<AimAuthService>()
                   .Named<IAuthService>(Source)
                   .WithParameter(
                       (parameterInfo, context) => parameterInfo.Name == OptionsParameterName,
                       (parameterInfo, context) => context.Resolve<IOptionsMonitor<AimOptions>>().Get(Source))
                   .SingleInstance();

            builder.RegisterType<AimAuthService>()
                   .Named<IAuthService>(Destination)
                   .WithParameter(
                       (parameterInfo, context) => parameterInfo.Name == OptionsParameterName,
                       (parameterInfo, context) => context.Resolve<IOptionsMonitor<AimOptions>>().Get(Destination))
                   .SingleInstance();

            builder.RegisterType<OkapiClient>()
                   .As<IReadOnlyRestClientFacade>()
                   .WithParameter(ApiUriParameterName, new Uri(Configuration.GetConnectionString(Source)))
                   .WithParameter(
                       (parameterInfo, context) => parameterInfo.Name == ApiVersionParameterName,
                       (parameterInfo, context) => context.Resolve<CloningToolOptions>().ApiVersion)
                   .WithParameter(
                       (parameterInfo, context) => parameterInfo.Name == AuthServiceParameterName,
                       (parameterInfo, context) => context.ResolveNamed<IAuthService>(Source))
                   .SingleInstance();

            builder.RegisterType<OkapiClient>()
                   .As<IRestClientFacade>()
                   .WithParameter(ApiUriParameterName, new Uri(Configuration.GetConnectionString(Destination)))
                   .WithParameter(
                       (parameterInfo, context) => parameterInfo.Name == ApiVersionParameterName,
                       (parameterInfo, context) => context.Resolve<CloningToolOptions>().ApiVersion)
                   .WithParameter(
                       (parameterInfo, context) => parameterInfo.Name == AuthServiceParameterName,
                       (parameterInfo, context) => context.ResolveNamed<IAuthService>(Destination))
                   .SingleInstance();

            builder.RegisterType<CloneTemplates>()
                   .Keyed<ICloneStrategy>(CloneMode.CloneTemplates);

            builder.RegisterType<ClonePositionLinks>()
                   .Keyed<ICloneStrategy>(CloneMode.CloneContentPositionsLinks);

            builder.RegisterType<CloneAdvertisements>()
                   .Keyed<ICloneStrategy>(CloneMode.CloneAdvertisements);

            builder.RegisterType<TruncatedCloneAdvertisements>()
                   .Keyed<ICloneStrategy>(CloneMode.TruncatedCloneAdvertisements);

            builder.RegisterType<CloneTemplatesWithLinks>()
                   .Keyed<ICloneStrategy>(CloneMode.CloneTemplatesWithLinks);

            builder.RegisterType<CloneRemarks>()
                   .Keyed<ICloneStrategy>(CloneMode.CloneRemarks);

            builder.RegisterType<CloneRemarkCategories>()
                   .Keyed<ICloneStrategy>(CloneMode.CloneRemarkCategories);

            builder.RegisterType<CloneRemarksWithCategories>()
                   .Keyed<ICloneStrategy>(CloneMode.CloneRemarksWithCategories);

            builder.RegisterType<TruncatedCloneAll>()
                   .Keyed<ICloneStrategy>(CloneMode.TruncatedCloneAll);

            builder.RegisterType<CloneAll>()
                   .Keyed<ICloneStrategy>(CloneMode.CloneAll);

            builder.RegisterType<ReloadFiles>()
                   .Keyed<ICloneStrategy>(CloneMode.ReloadFiles);

            builder.RegisterType<CloneStrategyProvider>()
                   .As<ICloneStrategyProvider>()
                   .SingleInstance();

            builder.RegisterType<CloningService>()
                   .AsSelf();

            return builder.Build();
        }

        private static DateTime? ConvertDateParameterToUniversalTime(Microsoft.Extensions.Logging.ILogger logger, string parameterName, DateTime? date)
        {
            if (!date.HasValue)
            {
                return null;
            }

            return ConvertDateParameterToUniversalTime(logger, parameterName, date.Value);
        }

        private static DateTime ConvertDateParameterToUniversalTime(Microsoft.Extensions.Logging.ILogger logger, string parameterName, DateTime date)
        {
            if (date.Kind == DateTimeKind.Utc)
            {
                return date;
            }

            var newValue = date.ToUniversalTime();
            logger.LogWarning("{parameter} parameter value was manually converted to UTC: {newValue:o} from {value:o}", parameterName, newValue, date);
            return newValue;
        }
    }
}
