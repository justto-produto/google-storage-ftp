using System;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using GoogleStorageFtp.FileSystem.Gcs;
using FubarDev.FtpServer;
using FubarDev.FtpServer.AccountManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using System.Threading.Tasks;

namespace GoogleStorageFtp
{
    class Program
    {
        private static readonly AutoResetEvent _closingEvent = new AutoResetEvent(false);

        static void Main(string[] args)
        {
            var services = new ServiceCollection().AddLogging(config => config.SetMinimumLevel(LogLevel.Trace));

#if !DEBUG
            var disableTls = Environment.GetEnvironmentVariable("DISABLE_TLS") ?? "False";
            if (bool.TryParse(disableTls, out var disable))
            {
                if (!disable)
                {
                    var cert = new X509Certificate2("ftp.pfx", Environment.GetEnvironmentVariable("PFX_PASSWORD"));
                    services.Configure<AuthTlsOptions>(cfg => cfg.ServerCertificate = cert);
                }
            }
#endif

            services.Configure<FtpConnectionOptions>(options => options.DefaultEncoding = System.Text.Encoding.UTF8);
            services.Configure<SimplePasvOptions>(options =>
            {
                options.PasvMinPort = 10000;
                options.PasvMaxPort = 10009;
                options.PublicAddress = IPAddress.Parse(Environment.GetEnvironmentVariable("PUBLIC_IP") ?? "127.0.0.1");
            });

            services.AddFtpServer(builder =>
            {
                builder.Services.AddSingleton<IMembershipProvider, CustomMembershipProvider>();
                builder.UseGcsFileSystem();
            });

            // Build the service provider
            using (var serviceProvider = services.BuildServiceProvider())
            {
                var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
                loggerFactory.AddNLog(new NLogProviderOptions { CaptureMessageTemplates = true, CaptureMessageProperties = true });
                NLog.LogManager.LoadConfiguration("NLog.config");

                try
                {
                    var running = True;
                    // Initialize the FTP server
                    var ftpServerHost = serviceProvider.GetRequiredService<IFtpServerHost>();

                    // Start the FTP server
                    ftpServerHost.StartAsync(CancellationToken.None).ConfigureAwait(false);

                    Console.WriteLine("Press Ctrl + C to exit");
                    Console.CancelKeyPress += ((s, a) => {
                        ftpServerHost.StopAsync(CancellationToken.None).ConfigureAwait(false);
                        Console.WriteLine("Bye!");
                        _closingEvent.Set();
                        running = False
                    });

                    _closingEvent.WaitOne();
                    while (running) {
                        Task.Delay(30000).Wait();
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
                }
            }
        }
    }
}
