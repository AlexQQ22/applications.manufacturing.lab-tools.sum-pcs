using System;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using SystemUtilizationMonitor.Models;

public class MonitoringVMs
{

        public static bool TestNetConnection(string computerName, int port, int timeoutMs = 5000)
        {
            try
            {
                using (var tcpClient = new TcpClient())
                {
                    // Configurar timeout
                    tcpClient.ReceiveTimeout = timeoutMs;
                    tcpClient.SendTimeout = timeoutMs;

                    // Conectar de forma síncrona
                    tcpClient.Connect(computerName, port);

                    return tcpClient.Connected;
                }
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"Error de socket al conectar a {computerName}:{port}: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al probar conexión a {computerName}:{port}: {ex.Message}");
                return false;
            }
        }
    

}