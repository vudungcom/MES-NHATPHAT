using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MES.Web.Services
{
    public class LicenseBackgroundWorker : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Check ngay khi server start.
            // Sau đó check lại mỗi 24 giờ.
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ServerLicenseHelper.CheckLicenseAsync(true);
                }
                catch (Exception ex)
                {
                    ServerLicenseHelper.WriteLog(
                        "LicenseBackgroundWorker error", ex);
                }

                try
                {
                    await Task.Delay(
                        TimeSpan.FromDays(1),
                        stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
