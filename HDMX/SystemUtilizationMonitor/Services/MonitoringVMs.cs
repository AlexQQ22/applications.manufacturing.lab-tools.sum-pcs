using System;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Linq;
using SystemUtilizationMonitor.Models;
using System;
using System.Management.Automation;
using System.Management.Automation.Runspaces;

public class MonitoringVMs
{

    /// <summary>
    /// Prueba la conexión a un host específico en un puerto específico usando PowerShell Test-NetConnection
    /// </summary>
    /// <param name="computerName">Nombre del equipo o dirección IP a probar</param>
    /// <param name="port">Puerto a probar</param>
    /// <param name="timeoutSeconds">Timeout en segundos (opcional, por defecto 10)</param>
    /// <returns>True si la conexión TCP fue exitosa, False en caso contrario</returns>
    public static async Task<bool> TestNetConnectionAsync(string computerName, int port, int timeoutSeconds = 5)
    {
        try
        {
            using (var runspace = RunspaceFactory.CreateRunspace())
            {
                runspace.Open();

                using (var powershell = PowerShell.Create())
                {
                    powershell.Runspace = runspace;

                    // Construir el comando PowerShell
                    string command = $"Test-NetConnection -ComputerName {computerName} -Port {port}";

                    powershell.AddScript(command);

                    // Ejecutar el comando de forma asíncrona con timeout
                    var task = Task.Run(() => powershell.Invoke());

                    if (await Task.WhenAny(task, Task.Delay(timeoutSeconds * 1000)) == task)
                    {
                        var results = task.Result;

                        // Buscar el resultado TcpTestSucceeded
                        foreach (var result in results)
                        {
                            var tcpTestSucceeded = result.Properties["TcpTestSucceeded"]?.Value;

                            if (tcpTestSucceeded != null && tcpTestSucceeded is bool boolResult)
                            {
                                return boolResult;
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Timeout: La operación excedió {timeoutSeconds} segundos");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al ejecutar Test-NetConnection: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Versión sincrónica del método
    /// </summary>
    /// <param name="computerName">Nombre del equipo o dirección IP a probar</param>
    /// <param name="port">Puerto a probar</param>
    /// <param name="timeoutSeconds">Timeout en segundos (opcional, por defecto 10)</param>
    /// <returns>True si la conexión TCP fue exitosa, False en caso contrario</returns>
    public static bool TestNetConnection(string computerName, int port, int timeoutSeconds = 10)
    {
        return TestNetConnectionAsync(computerName, port, timeoutSeconds).Result;
    }

}