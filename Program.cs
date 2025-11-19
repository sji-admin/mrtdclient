using cmrtd.Core.Model;
using cmrtd.Core.Service;
using cmrtd.Infrastructure.DeskoDevice;
using Desko.DDA;
using Desko.ePass;
using Desko.EPass.Types;
using System;
using System.Reflection;
using System.Text;

namespace cmrtd
{
    public class Program
    {
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            var port = builder.Configuration.GetValue<int>("Kestrel:Port");

            builder.WebHost.ConfigureKestrel(options =>
            {
                //options.ListenAnyIP(port);
                options.ListenLocalhost(port);
            });

            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSingleton<DeviceService>(); 

            var app = builder.Build();

            app.MapGet("/api/v1/devices/deviceInfo", (HttpContext context, IConfiguration config, DeviceService deviceService) =>
            {
                var query = context.Request.Query;
                query.TryGetValue("device_id", out var serialParam);
                string serial = serialParam.ToString()?.Trim() ?? "";

                var deviceSettings = config.GetSection("DeviceSettings").Get<DeviceSettings>();
                string pathToDll = deviceSettings.PathE;

                string version = "unknown";
                try
                {
                    if (!string.IsNullOrWhiteSpace(pathToDll) && File.Exists(pathToDll))
                    {
                        Assembly assembly = Assembly.LoadFrom(pathToDll);
                        version = assembly.GetName().Version?.ToString() ?? "unknown";
                    }
                }
                catch (Exception ex)
                {
                    version = $"error: {ex.Message}";
                }

                // ambil status & error
                var (isConnected, errorMessage) = deviceService.GetDeviceStatusWithError();
                var devices = deviceService.GetDeviceSerialList() ?? new List<(string Name, string Serial)>();

                if (devices.Count == 0)
                {
                    return Results.BadRequest(new { status = "error", message = "No connected device found." });
                }

                // pilih device berdasarkan serial
                (string Name, string Serial) selected;
                if (string.IsNullOrWhiteSpace(serial))
                {
                    selected = devices[0]; // kembali ke device pertama
                    Console.WriteLine($">>> [INFO] No serial provided, using first device: {selected.Name} / {selected.Serial}");
                }
                else
                {
                    var match = devices.FirstOrDefault(d => string.Equals(d.Serial, serial, StringComparison.OrdinalIgnoreCase));
                    if (match == default)
                    {
                        return Results.NotFound(new { status = "error", message = $"Device with serial '{serial}' not found." });
                    }
                    selected = match;
                    Console.WriteLine($">>> [INFO] Using device by serial: {selected.Name} / {selected.Serial}");
                }

                // daftar semua device untuk referensi
                var deviceObjects = devices.Select(d => new
                {
                    device_name = d.Name,
                    device_id = d.Serial
                });

                // hasil akhir
                return Results.Ok(new
                {
                    epass_version = version,
                    type = selected.Name, 
                    Dpi = deviceSettings.Dpi,
                    callback_enable = deviceSettings.Callback.Enable,
                    auto_scan = deviceSettings.AutoScan,
                    device_status = isConnected,
                    error_msg = errorMessage ?? "",
                    device_connected = new { selected.Name, selected.Serial },
                    //Device_connected = deviceObjects
                });
            });

            app.MapGet("/api/v1/devices/list", (IConfiguration config, DeviceService deviceService) =>
            {
                var deviceSettings = config.GetSection("DeviceSettings").Get<DeviceSettings>();
                string pathToDll = deviceSettings.PathE;

                Assembly assembly = Assembly.LoadFrom(pathToDll);
                var version = assembly.GetName().Version?.ToString();

                // ambil status & error
                //var (isConnected, errorMessage) = deviceService.GetDeviceStatusWithError();
                var devices = deviceService.GetDeviceSerialList();
                var deviceObjects = devices.Select(d => new
                {
                    device_name = d.Name,
                    device_id = d.Serial
                });

                return Results.Ok(new
                {
                    Devices = deviceObjects
                });
            });

            app.MapPost("/api/v1/devices/reconnect", (DeviceService deviceService) =>
            {
                try
                {
                    var result = deviceService.Reconnect();

                    if (result.Contains("cannot be connected", StringComparison.OrdinalIgnoreCase))
                    {
                        return Results.Json(new { status = "error", message = result }, statusCode: 500);
                    }

                    return Results.Ok(new { status = "ok", message = result });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Ok(new { status = "error", message = ex.Message });
                }
                catch (Exception ex)
                {
                    return Results.Json(new { status = "error", message = ex.Message }, statusCode: 500);
                }
            });            

            app.MapPost("/api/v1/devices/scan", async (HttpContext context, DeviceService deviceService) =>
            {
                var query = context.Request.Query;

                // === VALIDASI PARAMETER FULL (DENGAN DEFAULT) ===
                bool full = false;

                if (query.TryGetValue("full", out var fullStr))
                {
                    if (!bool.TryParse(fullStr, out full))
                    {
                        // Jika dikirim tapi tidak valid (misalnya ?full=abc)
                        return Results.BadRequest(new
                        {
                            status = "error",
                            message = "Parameter 'full' must be true or false"
                        });
                    }
                }

                query.TryGetValue("device_id", out var serialParam);
                string serial = serialParam.ToString()?.Trim() ?? "";

                // get list of connected devices
                var devices = deviceService.GetDeviceSerialList() ?? new List<(string Name, string Serial)>();

                if (devices.Count == 0)
                {
                    return Results.BadRequest(new { status = "error", message = "No connected device found." });
                }

                // select device: if serial provided -> match, otherwise fallback to index 0
                (string Name, string Serial) selected;
                if (string.IsNullOrWhiteSpace(serial))
                {
                    selected = devices[0];
                    Console.WriteLine($">>> [SCAN] No serial provided, using first device: {selected.Name} / {selected.Serial}");
                }
                else
                {
                    var match = devices.FirstOrDefault(d => string.Equals(d.Serial, serial, StringComparison.OrdinalIgnoreCase));
                    if (match == default)
                    {
                        return Results.BadRequest(new { status = "error", message = $"Device with serial '{serial}' not found." });
                    }
                    selected = match;
                    Console.WriteLine($">>> [SCAN] Using device by serial: {selected.Name} / {selected.Serial}");
                }

                try
                {
                    Pasport.ScanApiResponse scanRes;
                    Pasport.ScanApiResponse scanResult;

                    // decide which scan to run based on device name
                    if (selected.Name?.IndexOf("4X", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        selected.Name?.IndexOf("Penta 4X", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        // Penta 4X
                        scanRes = await deviceService.DoScanAsync();
                        scanResult = deviceService.LastScanResult;
                    }
                    else if (selected.Name?.IndexOf("CKI", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             selected.Name?.IndexOf("Penta CKI", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        // Penta CKI
                        scanRes = await deviceService.DoScanCki();
                        scanResult = deviceService.LastScanResultCki;
                    }
                    else
                    {
                        return Results.BadRequest(new { status = "error", message = $"Device type not supported for scanning: {selected.Name}" });
                    }

                    if (scanResult == null)
                        return Results.Json(new { status = "error", message = "No scan result yet" }, statusCode: 400);

                    if (!scanRes.Valid || scanRes.Code != 200)
                        return Results.Json(new { status = "error", data = scanRes }, statusCode: 400);

                    if (full)
                    {
                        return Results.Ok(new
                        {
                            Success = 200,
                            Code = scanResult.Code,
                            data = new
                            {
                                MRZ = scanResult.Data?.MRZ,
                                Bcbp = scanResult.Data?.Bcbp,
                                docType = scanResult.Data?.DocType,
                                rgbImage = scanResult.Data?.RgbImage,
                                uvImage = scanResult.Data?.UvImage,
                                irImage = scanResult.Data?.IrImage,
                                Valid = scanResult.Valid
                            },
                            err_msg = scanResult.Err_msg
                        });
                    }
                    else
                    {
                        return Results.Ok(new
                        {
                            Success = 200,
                            Code = scanResult.Code,
                            data = new
                            {
                                MRZ = scanResult.Data?.MRZ,
                                Bcbp = scanResult.Data?.Bcbp,
                                docType = scanResult.Data?.DocType,
                                rgbImage = scanResult.Data?.RgbImage,
                                Valid = scanResult.Valid
                            },
                            err_msg = scanResult.Err_msg
                        });
                    }
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { status = "error", message = ex.Message });
                }
                catch (Exception ex)
                {
                    return Results.Json(new { status = "error", message = ex.Message }, statusCode: 500);
                }
            });

            // Start service aplikasi 
            var deviceSvc = app.Services.GetRequiredService<DeviceService>();
            deviceSvc.Start();
            deviceSvc.startCki();

            app.Lifetime.ApplicationStopping.Register(() =>
            {
                deviceSvc.Dispose();
                Api.Terminate();
                DDALib.Terminate();
                deviceSvc.StopCki();
            });

            app.Run();
        }
    }
}