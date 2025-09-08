using System;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Linq;
using SystemUtilizationMonitor.Models;

public class MonitoringVMs
{
    private const string VM_MONITORING_BAT_SOURCE = @"C:\SUMInstall\VM_Monitoring.bat";
    private const string PSEXEC_PATH = @"c:\SUMInstall\PsExec64.exe";

    // Configuration properties - will be loaded from config file
    private readonly string username;
    private readonly string password;

    // Constructor that accepts configuration
    public MonitoringVMs(ConfigurationModel config)
    {
        // Use configuration values instead of hardcoded constants
        username = config?.VM?.Username ?? "cc3user"; // fallback to default if config is null
        password = config?.VM?.Password ?? "sthi";     // fallback to default if config is null
    }

    // Parameterless constructor for backward compatibility
    public MonitoringVMs() : this(null)
    {
        // Uses fallback values from the main constructor
    }

    public async Task<bool> CheckVMsAsync()
    {
        bool vmInUse = false;

        for (int i = 1; i <= 4; i++)
        {
            string ipvm = $"10.0.0.{i}";

            // Ping la VM
            if (await PingVMAsync(ipvm))
            {
                Console.WriteLine($"VM {ipvm} is reachable");

                // Copiar el archivo de monitoreo si no existe
                await CopyMonitoringFileAsync(ipvm);

                // Ejecutar el script de monitoreo y capturar resultado
                string result = await ExecuteRemoteMonitoringAsync(ipvm);

                if (result == "VM_IN_USE_BY_VNC")
                {
                    Console.WriteLine($"VM {ipvm} is in use by VNC or hyperV");
                    vmInUse = true;
                    break; // Salir del loop si encontramos una VM en uso
                }
            }
            else
            {
                Console.WriteLine($"VM {ipvm} is not reachable");
            }
        }


        return vmInUse;
    }

    private async Task<bool> PingVMAsync(string ip)
    {
        try
        {
            using (var ping = new Ping())
            {
                var reply = await ping.SendPingAsync(ip, 500);
                return reply.Status == IPStatus.Success;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error pinging {ip}: {ex.Message}");
            return false;
        }
    }

    private async Task CopyMonitoringFileAsync(string ipvm)
    {
        try
        {
            string destinationPath = $@"\\{ipvm}\c$\Users\{username}\Desktop\VM_monitoring.bat";

            if (!File.Exists(destinationPath))
            {
                await Task.Run(() => File.Copy(VM_MONITORING_BAT_SOURCE, destinationPath, true));
                Console.WriteLine($"Copied monitoring file to {ipvm}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error copying file to {ipvm}: {ex.Message}");
        }
    }

    private async Task<string> ExecuteRemoteMonitoringAsync(string ipvm)
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = PSEXEC_PATH,
                Arguments = $@"\\{ipvm} -u {username} -p {password} -h -i -accepteula -nobanner  cmd /c ""c:\Users\{username}\Desktop\VM_monitoring.bat""",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (var process = new Process())
            {
                process.StartInfo = processInfo;
                process.Start();

                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync();

                if (!string.IsNullOrEmpty(error))
                {
                    Console.WriteLine($"Error executing on {ipvm}: {error}");
                }

                // Limpiar la salida y buscar el resultado esperado
                string cleanOutput = output.Trim();

                // El script batch original busca "x", pero mencionas "VM_IN_USE_BY_VNC"
                // Aquí puedes ajustar según lo que realmente devuelve tu VM_monitoring.bat
                if (cleanOutput.Contains("VM_IN_USE_BY_VNC"))
                {
                    return "VM_IN_USE_BY_VNC";
                }

                /////////// new jose veridicacion si vm esta abierta por hyper v
                ///var procesos = Process.GetProcessesByName("vmconnect");
                var procesos = Process.GetProcessesByName("vmconnect");
                if (procesos.Any())
                {
                    Console.WriteLine("VM_IN_USE_BY_HYPERV");
                    return "VM_IN_USE_BY_VNC";
                }
                else
                {
                    Console.WriteLine("No hay conexión Hyper-V activa");
                }

                Console.WriteLine($"Output from {ipvm}: {cleanOutput}");
                return cleanOutput;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error executing remote command on {ipvm}: {ex.Message}");
            return string.Empty;
        }
    }

}