using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SystemUtilizationMonitor.Models;

namespace SystemUtilizationMonitor.Services
{
    /// <summary>
    /// Clase responsable de todo el monitoreo, control y gestión de las máquinas virtuales
    /// Incluye funcionalidades para detectar VMs activas, cerrarlas automáticamente y gestionar timeouts
    /// </summary>
    public class MonitoringVMs
    {
        #region Constantes
        private const string VM_MONITORING_BAT_SOURCE = @"C:\SUMInstall\VM_Monitoring.bat";
        private const string VM_CLOSE_POPUP_BAT_SOURCE = @"C:\SUMInstall\VM_Close_PopUP.bat";
        private const string VM_CLOSE_VNC_BAT_SOURCE = @"C:\SUMInstall\VM_Close_VNC.bat";
        private const string PSEXEC_PATH = @"c:\SUMInstall\PsExec64.exe";
        private const string KILLING_PENDINGS_FILE = @"C:\SUMInstall\KillingPendings.txt";
        private const string SUM_VERSION_FILE = @"C:\SUMInstall\Rev.txt";

        // Timeouts en minutos
        private const int VM_TIMEOUT_MINUTES = 2;
        private const int KILL_DELAY_MINUTES = 1;

        // Rango de IPs de las VMs (10.0.0.1 a 10.0.0.4)
        private const int MIN_VM_IP = 1;
        private const int MAX_VM_IP = 4;
        private const string VM_IP_BASE = "10.0.0";
        #endregion

        #region Propiedades Privadas
        private readonly string username;
        private readonly string password;
        private readonly ConfigurationModel config;
        private readonly Action<string> logInfo;
        private readonly Action<string> logError;
        #endregion

        #region Constructor
        /// <summary>
        /// Constructor que inicializa la clase con configuración y métodos de logging
        /// </summary>
        /// <param name="config">Configuración de la aplicación</param>
        /// <param name="logInfo">Método para logging de información</param>
        /// <param name="logError">Método para logging de errores</param>
        public MonitoringVMs(ConfigurationModel config, Action<string> logInfo = null, Action<string> logError = null)
        {
            this.config = config;
            this.logInfo = logInfo ?? Console.WriteLine;
            this.logError = logError ?? Console.WriteLine;

            // Usar valores de configuración que ya tienen valores por defecto en el constructor
            username = config?.VM?.Username ?? "cc3user";
            password = config?.VM?.Password ?? "sthi";
        }

        /// <summary>
        /// Constructor sin parámetros para compatibilidad hacia atrás
        /// </summary>
        public MonitoringVMs() : this(null)
        {
            // Usa valores por defecto del constructor principal
        }
        #endregion

        #region Métodos Públicos Principales

        /// <summary>
        /// Verifica si alguna VM está siendo utilizada por VNC o Hyper-V
        /// </summary>
        /// <returns>True si hay VMs en uso, False si no</returns>
        public async Task<bool> CheckVMsAsync()
        {
            logInfo("Iniciando verificación de VMs en uso...");

            // Verificar procesos de vmconnect en el host local
            if (CheckLocalVmConnectProcesses())
            {
                logInfo("Detectados procesos vmconnect activos en el host local");
                return true;
            }

            // Verificar VMs remotas
            for (int i = MIN_VM_IP; i <= MAX_VM_IP; i++)
            {
                string ipvm = $"{VM_IP_BASE}.{i}";

                if (await PingVMAsync(ipvm))
                {
                    logInfo($"VM {ipvm} está accesible, verificando uso...");

                    // Copiar archivo de monitoreo si no existe
                    await EnsureMonitoringFileExistsAsync(ipvm);

                    // Ejecutar script de monitoreo
                    string result = await ExecuteRemoteMonitoringAsync(ipvm);

                    if (result == "VM_IN_USE_BY_VNC")
                    {
                        logInfo($"VM {ipvm} está en uso por VNC");
                        return true;
                    }
                }
                else
                {
                    logInfo($"VM {ipvm} no está accesible");
                }
            }

            logInfo("No se detectaron VMs en uso");
            return false;
        }

        /// <summary>
        /// Verifica si existen procesos vmconnect locales
        /// </summary>
        /// <returns>True si hay procesos vmconnect activos</returns>
        public bool CheckLocalVmConnectProcesses()
        {
            try
            {
                var vmConnectProcesses = Process.GetProcessesByName("vmconnect");
                bool hasProcesses = vmConnectProcesses.Any();

                if (hasProcesses)
                {
                    logInfo($"Encontrados {vmConnectProcesses.Length} procesos vmconnect");
                }

                // Liberar recursos de los procesos
                foreach (var process in vmConnectProcesses)
                {
                    process.Dispose();
                }

                return hasProcesses;
            }
            catch (Exception ex)
            {
                logError($"Error verificando procesos vmconnect: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Procesa las tareas de cierre pendientes de VMs
        /// </summary>
        /// <returns>DateTime actualizado de lastVmConnectedDetection</returns>
        public async Task<DateTime> ProcessPendingKillsAsync(DateTime lastVmConnectedDetection)
        {
            try
            {
                if (!File.Exists(KILLING_PENDINGS_FILE))
                {
                    logInfo("No hay archivo de cierres pendientes");
                    return lastVmConnectedDetection;
                }

                List<string> remainingLines = new List<string>();
                string[] lines = File.ReadAllLines(KILLING_PENDINGS_FILE);
                bool killExecuted = false;
                DateTime currentUtc = DateTime.UtcNow;

                logInfo($"Procesando {lines.Length} entradas en archivo de cierres pendientes");

                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    // Buscar patrón de tiempo de cierre programado
                    var match = Regex.Match(line, @"Killing vmconnect at (\d{2}:\d{2}:\d{2}:\d{2}:\d{2}:\d{2})");
                    if (match.Success)
                    {
                        string timeString = match.Groups[1].Value;

                        if (TryParseKillTime(timeString, out DateTime killTime))
                        {
                            if (currentUtc >= killTime)
                            {
                                logInfo($"Procesando cierre vencido: {line}");

                                // Verificar actividad de usuario antes de cerrar
                                var activityCheckResult = await CheckVMUserActivityAsync(lastVmConnectedDetection);
                                if (!activityCheckResult.hasActivity)
                                {
                                    lastVmConnectedDetection = await KillVmConnectProcessesAsync(lastVmConnectedDetection);
                                    killExecuted = true;
                                    logInfo("Cierre de VM ejecutado - no se detectó actividad de usuario");
                                }
                                else
                                {
                                    logInfo("Cierre de VM omitido - se detectó actividad de usuario");
                                    lastVmConnectedDetection = activityCheckResult.updatedDetectionTime;
                                }
                            }
                            else
                            {
                                remainingLines.Add(line);
                            }
                        }
                        else
                        {
                            logError($"No se pudo analizar el tiempo de cierre: {line}");
                            remainingLines.Add(line);
                        }
                    }
                    else
                    {
                        remainingLines.Add(line);
                    }
                }

                // Actualizar archivo de pendientes
                UpdatePendingKillsFile(remainingLines, killExecuted, lines.Length);

                return lastVmConnectedDetection;
            }
            catch (Exception ex)
            {
                logError($"Error procesando cierres pendientes: {ex.Message}");
                return lastVmConnectedDetection;
            }
        }

        /// <summary>
        /// Programa el cierre automático de VMs después del timeout
        /// </summary>
        public async Task ScheduleVmCloseAndKillAsync()
        {
            try
            {
                logInfo("Programando operaciones de cierre de VM después del timeout de conexión");

                // Calcular tiempo de cierre (UTC actual + tiempo de retraso)
                DateTime killTime = DateTime.UtcNow.AddMinutes(KILL_DELAY_MINUTES);
                string killTimeString = killTime.ToString("yy:MM:dd:HH:mm:ss");

                // Asegurar que el directorio existe
                EnsureDirectoryExists(KILLING_PENDINGS_FILE);

                // Agregar entrada de cierre al archivo de pendientes
                string killEntry = $"Killing vmconnect at {killTimeString}";
                File.AppendAllText(KILLING_PENDINGS_FILE, killEntry + Environment.NewLine);
                logInfo($"Agregada entrada de cierre: {killEntry}");

                // Ejecutar operaciones de cierre en VMs disponibles de forma asíncrona
                await Task.Run(async () =>
                {
                    try
                    {
                        await ExecuteVmCloseOperationsAsync();
                    }
                    catch (Exception ex)
                    {
                        logError($"Error en tarea de cierre de VM: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                logError($"Error programando cierre y terminación de VM: {ex.Message}");
            }
        }

        /// <summary>
        /// Verifica si debe programarse el cierre de VMs basado en el timeout
        /// </summary>
        /// <param name="lastVmConnectedDetection">Tiempo de la última detección de VM conectada</param>
        /// <returns>True si debe programarse el cierre</returns>
        public bool ShouldScheduleVmClose(DateTime lastVmConnectedDetection)
        {
            if (lastVmConnectedDetection == DateTime.MinValue)
            {
                logInfo("lastVmConnectedDetection es MinValue");
                return false;
            }

            var timeSinceLastDetection = DateTime.UtcNow - lastVmConnectedDetection;
            bool shouldSchedule = timeSinceLastDetection.TotalMinutes >= VM_TIMEOUT_MINUTES;

            logInfo($"Tiempo desde última detección: {timeSinceLastDetection.TotalMinutes:F1} minutos. " +
                   $"¿Debe programar cierre? {shouldSchedule}");

            return shouldSchedule;
        }
        #endregion

        #region Métodos Privados - Verificación de Actividad


        /// <summary>
        /// Verifica si hay actividad de usuario en alguna de las VMs
        /// </summary>
        /// <returns>Tupla con hasActivity y updatedDetectionTime</returns>
        private async Task<(bool hasActivity, DateTime updatedDetectionTime)> CheckVMUserActivityAsync(DateTime lastVmConnectedDetection)
        {
            try
            {
                for (int i = MIN_VM_IP; i <= MAX_VM_IP; i++)
                {
                    string ipvm = $"{VM_IP_BASE}.{i}";

                    if (await PingVMAsync(ipvm))
                    {
                        var result = await CheckUserActivityOnVMAsync(ipvm, lastVmConnectedDetection);
                        if (result.hasActivity)
                        {
                            return result;
                        }
                    }
                }

                return (false, lastVmConnectedDetection);
            }
            catch (Exception ex)
            {
                logError($"Error verificando actividad de usuario en VMs: {ex.Message}");
                return (false, lastVmConnectedDetection);
            }
        }


        /// <summary>
        /// Verifica actividad de usuario en una VM específica
        /// </summary>
        /// <param name="ipvm">IP de la VM a verificar</param>
        /// <returns>Tupla con hasActivity y updatedDetectionTime</returns>
        private async Task<(bool hasActivity, DateTime updatedDetectionTime)> CheckUserActivityOnVMAsync(string ipvm, DateTime lastVmConnectedDetection)
        {
            try
            {
                string remoteActivityFile = $@"\\{ipvm}\c$\Temp\user_has_activity.txt";

                if (File.Exists(remoteActivityFile))
                {
                    string content = File.ReadAllText(remoteActivityFile).Trim();

                    // Verificar si el contenido contiene "yes" (sin importar mayúsculas/minúsculas)
                    if (content.IndexOf("yes", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        logInfo($"Actividad de usuario detectada en VM {ipvm}");

                        // Limpiar archivos de actividad y pendientes
                        DateTime updatedTime = await CleanupActivityFilesAsync(remoteActivityFile, lastVmConnectedDetection);

                        return (true, updatedTime);
                    }
                }

                return (false, lastVmConnectedDetection);
            }
            catch (Exception ex)
            {
                logError($"Error verificando actividad de usuario en VM {ipvm}: {ex.Message}");
                return (false, lastVmConnectedDetection);
            }
        }



        /// <summary>
        /// Limpia los archivos de actividad después de detectar actividad de usuario
        /// </summary>
        /// <param name="remoteActivityFile">Archivo de actividad remoto a limpiar</param>
        /// <returns>DateTime actualizado</returns>
        private async Task<DateTime> CleanupActivityFilesAsync(string remoteActivityFile, DateTime lastVmConnectedDetection)
        {
            try
            {
                // Limpiar archivo de actividad
                File.WriteAllText(remoteActivityFile, "");
                DateTime updatedTime = DateTime.UtcNow;

                // Limpiar archivo de cierres pendientes
                if (File.Exists(KILLING_PENDINGS_FILE))
                {
                    File.WriteAllText(KILLING_PENDINGS_FILE, "");
                    logInfo("Archivos de actividad y pendientes limpiados");
                }

                return updatedTime;
            }
            catch (Exception ex)
            {
                logError($"Error limpiando archivos de actividad: {ex.Message}");
                return lastVmConnectedDetection;
            }
        }

        #endregion

        #region Métodos Privados - Operaciones de Red

        /// <summary>
        /// Hace ping a una VM de forma asíncrona
        /// </summary>
        /// <param name="ip">IP de la VM</param>
        /// <param name="timeoutMs">Timeout en milisegundos (por defecto 500ms)</param>
        /// <returns>True si la VM responde al ping</returns>
        private async Task<bool> PingVMAsync(string ip, int timeoutMs = 500)
        {
            try
            {
                using (var ping = new Ping())
                {
                    var reply = await ping.SendPingAsync(ip, timeoutMs);
                    return reply.Status == IPStatus.Success;
                }
            }
            catch (Exception ex)
            {
                logError($"Error haciendo ping a {ip}: {ex.Message}");
                return false;
            }
        }
        #endregion

        #region Métodos Privados - Gestión de Archivos

        /// <summary>
        /// Asegura que el archivo de monitoreo exista en la VM remota
        /// </summary>
        /// <param name="ipvm">IP de la VM</param>
        private async Task EnsureMonitoringFileExistsAsync(string ipvm)
        {
            await CopyFileIfNotExistsAsync(
                VM_MONITORING_BAT_SOURCE,
                $@"\\{ipvm}\c$\Users\{username}\Desktop\VM_monitoring.bat",
                $"archivo de monitoreo a {ipvm}"
            );
        }

        /// <summary>
        /// Copia un archivo a la VM remota si no existe
        /// </summary>
        /// <param name="sourcePath">Ruta del archivo origen</param>
        /// <param name="destinationPath">Ruta del archivo destino</param>
        /// <param name="description">Descripción para logging</param>
        private async Task CopyFileIfNotExistsAsync(string sourcePath, string destinationPath, string description)
        {
            try
            {
                if (!File.Exists(destinationPath))
                {
                    await Task.Run(() => File.Copy(sourcePath, destinationPath, true));
                    logInfo($"Copiado {description}");
                }
            }
            catch (Exception ex)
            {
                logError($"Error copiando {description}: {ex.Message}");
            }
        }

        /// <summary>
        /// Asegura que el directorio de un archivo exista
        /// </summary>
        /// <param name="filePath">Ruta del archivo</param>
        private void EnsureDirectoryExists(string filePath)
        {
            string directory = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        /// <summary>
        /// Actualiza el archivo de cierres pendientes
        /// </summary>
        /// <param name="remainingLines">Líneas que deben permanecer</param>
        /// <param name="killExecuted">Si se ejecutó algún cierre</param>
        /// <param name="originalCount">Cantidad original de líneas</param>
        private void UpdatePendingKillsFile(List<string> remainingLines, bool killExecuted, int originalCount)
        {
            if (killExecuted || remainingLines.Count != originalCount)
            {
                if (remainingLines.Count > 0)
                {
                    File.WriteAllLines(KILLING_PENDINGS_FILE, remainingLines);
                }
                else
                {
                    File.Delete(KILLING_PENDINGS_FILE);
                    logInfo("Archivo de pendientes eliminado (vacío)");
                }
            }
        }
        #endregion

        #region Métodos Privados - Ejecución Remota

        /// <summary>
        /// Ejecuta el script de monitoreo en una VM remota
        /// </summary>
        /// <param name="ipvm">IP de la VM</param>
        /// <returns>Resultado del script de monitoreo</returns>
        private async Task<string> ExecuteRemoteMonitoringAsync(string ipvm)
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = PSEXEC_PATH,
                    Arguments = $@"\\{ipvm} -u {username} -p {password} -h -i -accepteula -nobanner cmd /c ""c:\Users\{username}\Desktop\VM_monitoring.bat""",
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
                        logError($"Error ejecutando en {ipvm}: {error}");
                    }

                    string cleanOutput = output.Trim();

                    // Verificar si la VM está en uso por VNC
                    if (cleanOutput.Contains("VM_IN_USE_BY_VNC"))
                    {
                        return "VM_IN_USE_BY_VNC";
                    }

                    logInfo($"Salida de {ipvm}: {cleanOutput}");
                    return cleanOutput;
                }
            }
            catch (Exception ex)
            {
                logError($"Error ejecutando comando remoto en {ipvm}: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Ejecuta operaciones de cierre en todas las VMs disponibles
        /// </summary>
        private async Task ExecuteVmCloseOperationsAsync()
        {
            var tasks = new List<Task>();

            for (int i = MIN_VM_IP; i <= MAX_VM_IP; i++)
            {
                string ipvm = $"{VM_IP_BASE}.{i}";
                tasks.Add(ExecuteVmCloseOnSingleVMAsync(ipvm));
            }

            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Ejecuta operación de cierre en una VM específica
        /// </summary>
        /// <param name="ipAddress">IP de la VM</param>
        private async Task ExecuteVmCloseOnSingleVMAsync(string ipAddress)
        {
            try
            {
                // Verificar conectividad con timeout
                if (await PingVMAsync(ipAddress, 1000))
                {
                    logInfo($"VM {ipAddress} es accesible, ejecutando script de cierre");

                    // Asegurar que los archivos necesarios existan
                    await EnsureCloseScriptsExistAsync(ipAddress);

                    // Ejecutar script de cierre con popup
                    await ExecuteRemoteCloseScriptAsync(ipAddress);
                }
                else
                {
                    logInfo($"VM {ipAddress} no es accesible, omitiendo");
                }
            }
            catch (Exception ex)
            {
                logError($"Error ejecutando cierre en VM {ipAddress}: {ex.Message}");
            }
        }

        /// <summary>
        /// Asegura que los scripts de cierre existan en la VM
        /// </summary>
        /// <param name="ipAddress">IP de la VM</param>
        private async Task EnsureCloseScriptsExistAsync(string ipAddress)
        {
            var copyTasks = new[]
            {
                CopyFileIfNotExistsAsync(
                    VM_CLOSE_POPUP_BAT_SOURCE,
                    $@"\\{ipAddress}\c$\Users\{username}\Desktop\VM_Close_PopUP.bat",
                    $"VM_Close_PopUP.bat a {ipAddress}"
                ),
                CopyFileIfNotExistsAsync(
                    VM_CLOSE_VNC_BAT_SOURCE,
                    $@"\\{ipAddress}\c$\Users\{username}\Desktop\VM_Close_VNC.bat",
                    $"VM_Close_VNC.bat a {ipAddress}"
                ),
                CopyFileIfNotExistsAsync(
                    VM_MONITORING_BAT_SOURCE,
                    $@"\\{ipAddress}\c$\Users\{username}\Desktop\VM_Monitoring.bat",
                    $"VM_Monitoring.bat a {ipAddress}"
                )
            };

            await Task.WhenAll(copyTasks);
        }

        /// <summary>
        /// Ejecuta el script de cierre remoto con timeout
        /// </summary>
        /// <param name="ipAddress">IP de la VM</param>
        private async Task ExecuteRemoteCloseScriptAsync(string ipAddress)
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = PSEXEC_PATH,
                    Arguments = $@"\\{ipAddress} -u {username} -p {password} -h -i 1 -accepteula -nobanner cmd /c ""c:\Users\{username}\Desktop\VM_Close_PopUP.bat""",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                logInfo($"Ejecutando script de cierre en {ipAddress}");

                using (Process process = Process.Start(startInfo))
                {
                    // Timeout de 10 segundos para el proceso
                    bool finished = process.WaitForExit(10000);

                    if (finished)
                    {
                        string output = process.StandardOutput.ReadToEnd();
                        string errors = process.StandardError.ReadToEnd();

                        logInfo($"Script en {ipAddress} - Código de salida: {process.ExitCode}");

                        if (!string.IsNullOrEmpty(output))
                            logInfo($"Salida: {output}");

                        if (!string.IsNullOrEmpty(errors))
                            logError($"Errores: {errors}");
                    }
                    else
                    {
                        logError($"Script en {ipAddress} agotó tiempo de espera, terminando proceso");
                        try
                        {
                            process.Kill();
                        }
                        catch (Exception killEx)
                        {
                            logError($"Falló al terminar proceso que agotó tiempo: {killEx.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logError($"Error ejecutando script de cierre en {ipAddress}: {ex.Message}");
            }
        }
        #endregion

        #region Métodos Privados - Terminación de Procesos


        /// <summary>
        /// Termina todos los procesos vmconnect y cierra conexiones VNC
        /// </summary>
        /// <returns>DateTime actualizado</returns>
        private async Task<DateTime> KillVmConnectProcessesAsync(DateTime lastVmConnectedDetection)
        {
            try
            {
                // Primero cerrar conexiones VNC en las VMs
                await CloseVNCConnectionsAsync();

                // Luego terminar procesos vmconnect locales
                await TerminateLocalVmConnectProcessesAsync();
                DateTime updatedTime = DateTime.UtcNow;

                // Limpiar archivo de pendientes
                if (File.Exists(KILLING_PENDINGS_FILE))
                {
                    File.WriteAllText(KILLING_PENDINGS_FILE, "");
                    logInfo("Archivo de cierres pendientes limpiado");
                }

                return updatedTime;
            }
            catch (Exception ex)
            {
                logError($"Error terminando procesos vmconnect: {ex.Message}");
                return lastVmConnectedDetection;
            }
        }

        /// <summary>
        /// Cierra conexiones VNC en todas las VMs
        /// </summary>
        private async Task CloseVNCConnectionsAsync()
        {
            var tasks = new List<Task>();

            for (int i = MIN_VM_IP; i <= MAX_VM_IP; i++)
            {
                string ipvm = $"{VM_IP_BASE}.{i}";
                tasks.Add(CloseVNCOnSingleVMAsync(ipvm));
            }

            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Cierra conexión VNC en una VM específica
        /// </summary>
        /// <param name="ipvm">IP de la VM</param>
        private async Task CloseVNCOnSingleVMAsync(string ipvm)
        {
            try
            {
                if (await PingVMAsync(ipvm, 1000))
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = PSEXEC_PATH,
                        Arguments = $@"\\{ipvm} -u {username} -p {password} -h -i 1 -accepteula -nobanner cmd /c ""c:\Users\{username}\Desktop\VM_Close_VNC.bat""",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using (Process process = Process.Start(startInfo))
                    {
                        process.WaitForExit(1000);

                        string output = process.StandardOutput.ReadToEnd();
                        string errors = process.StandardError.ReadToEnd();

                        logInfo($"Cierre VNC en {ipvm} - Código: {process.ExitCode}");

                        if (!string.IsNullOrEmpty(output))
                            logInfo($"Salida: {output}");

                        if (!string.IsNullOrEmpty(errors))
                            logError($"Errores: {errors}");
                    }
                }
                else
                {
                    logInfo($"VM {ipvm} no accesible, omitiendo cierre VNC");
                }
            }
            catch (Exception ex)
            {
                logError($"Error cerrando VNC en {ipvm}: {ex.Message}");
            }
        }

        /// <summary>
        /// Termina procesos vmconnect locales
        /// </summary>
        private async Task TerminateLocalVmConnectProcessesAsync()
        {
            try
            {
                Process[] vmConnectProcesses = Process.GetProcessesByName("vmconnect");

                if (vmConnectProcesses.Length > 0)
                {
                    logInfo($"Encontrados {vmConnectProcesses.Length} proceso(s) vmconnect para terminar");

                    foreach (Process process in vmConnectProcesses)
                    {
                        try
                        {
                            logInfo($"Terminando proceso vmconnect con PID: {process.Id}");
                            process.Kill();
                            logInfo($"Proceso vmconnect PID {process.Id} terminado exitosamente");
                        }
                        catch (Exception ex)
                        {
                            logError($"Falló al terminar proceso vmconnect PID {process.Id}: {ex.Message}");
                        }
                        finally
                        {
                            process.Dispose();
                        }
                    }
                }
                else
                {
                    logInfo("No se encontraron procesos vmconnect para terminar");
                }
            }
            catch (Exception ex)
            {
                logError($"Error buscando procesos vmconnect: {ex.Message}");
            }
        }
        #endregion

        #region Métodos Privados - Utilidades

        /// <summary>
        /// Intenta parsear una cadena de tiempo de cierre
        /// Formato esperado: yy:MM:dd:HH:mm:ss
        /// </summary>
        /// <param name="timeString">Cadena de tiempo a parsear</param>
        /// <param name="killTime">Tiempo parseado de salida</param>
        /// <returns>True si el parseo fue exitoso</returns>
        private bool TryParseKillTime(string timeString, out DateTime killTime)
        {
            killTime = DateTime.MinValue;

            try
            {
                string[] parts = timeString.Split(':');
                if (parts.Length != 6) return false;

                int year = 2000 + int.Parse(parts[0]);
                int month = int.Parse(parts[1]);
                int day = int.Parse(parts[2]);
                int hour = int.Parse(parts[3]);
                int minute = int.Parse(parts[4]);
                int second = int.Parse(parts[5]);

                killTime = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);
                return true;
            }
            catch
            {
                return false;
            }
        }
        #endregion
    }
}