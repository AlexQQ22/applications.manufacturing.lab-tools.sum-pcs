using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;  // ← ADD THIS
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SystemUtilizationMonitor.Models;
using SystemUtilizationMonitor.Services;
using SystemUtilizationMonitor.Utilities;

namespace SystemUtilizationMonitor
{
    [ExcludeFromCodeCoverage]  // ← ADD THIS
    public class Program
    {
        #region Variables Privadas
        private static MonitorConfiguration config;
        private static bool shouldStop = false;
        private static string outputDirectory;
        private static string currentOutputFile;
        private static DateTime currentDay;
        private static ConfigurationModel appConfig;
        private static string logInfo;
        private static InputHookManager inputHook;
        private static DateTime lastEndTime = DateTime.UtcNow;
        private static DateTime lastVmConnectedDetection = DateTime.MinValue;

        // Instancia de la clase de monitoreo de VMs
        private static MonitoringVMs vmMonitor;
        #endregion

        [STAThread]
        public static void Main(string[] args)
        {
            try
            {
                // Cargar configuración de la aplicación
                LoadConfiguration();

                // Ocultar ventana de consola si no está en modo debug
                if (!appConfig.SumPOR.Debug)
                    HideConsoleWindow();
                else
                {
                    Console.WriteLine("=== SystemUtilizationMonitor Debug Mode ===");
                    Console.WriteLine("Press Ctrl+C to stop...");
                }

                // Configurar directorios y archivos de salida
                SetupOutputDirectory();
                InitializeForCurrentDay();
                SetupMonitoringConfiguration();
                SetupCancellation();
                InitializeInputHooks();

                // Inicializar monitor de VMs con logging
                InitializeVmMonitor();

                StartFileCleanupTask();

                // Iniciar bucle principal de monitoreo
                MonitoringLoop();
            }
            catch (Exception ex)
            {
                LogError("Error en ejecución principal: " + ex.Message);
            }
            finally
            {
                Cleanup();
            }
        }

        /// <summary>
        /// Inicializa el monitor de VMs con métodos de logging
        /// </summary>
        private static void InitializeVmMonitor()
        {
            try
            {
                vmMonitor = new MonitoringVMs(appConfig, LogInfo, LogError);
                LogInfo("Monitor de VMs inicializado correctamente");
            }
            catch (Exception ex)
            {
                LogError($"Error inicializando monitor de VMs: {ex.Message}");
            }
        }


        /// <summary>
        /// Bucle principal de monitoreo del sistema
        /// </summary>
        private static void MonitoringLoop()
        {
            while (!shouldStop)
            {
                var startTime = lastEndTime;
                logInfo = string.Empty;

                // Verificar si cambió el día para crear nuevo archivo de salida
                if (DateTime.UtcNow.Date != currentDay)
                {
                    InitializeForCurrentDay();
                    LogInfo($"Cambiado a nuevo archivo diario: {currentOutputFile}");
                }

                // 1. Verificar si existen instancias de vmconnect
                bool vmConnectExists = CheckVmConnectProcesses();
                LogInfo($"Veri VM Alex: {vmConnectExists}");

                // Actualizar tiempo de última detección de VM conectada
                UpdateVmConnectionDetectionTime(vmConnectExists);

                // 2. Procesar cierres pendientes primero (ahora actualiza lastVmConnectedDetection)
                ProcessPendingVmKills();

                // 3. Verificar si han pasado los minutos de timeout desde la PRIMERA detección de conexión VM
                // Y vmconnect aún existe
                bool shouldScheduleVmClose = ShouldScheduleVmClose(lastVmConnectedDetection);
                LogInfo($"shouldScheduleVmClose VM Alex: {vmConnectExists}");
                if (vmConnectExists && shouldScheduleVmClose)
                {
                    ScheduleVmCloseAndKill();
                }

                LogInfo("Verificación de cierre de VM completada");

                // Preparar para siguiente ciclo
                var endTime = startTime.Add(config.RecordInterval);
                ResetCounters();

                try
                {
                    Thread.Sleep(config.RecordInterval);
                }
                catch (ThreadInterruptedException)
                {
                    break;
                }

                if (shouldStop) break;
                LogInfo("Ciclo de 5 minutos completado");

                // Recopilar datos de utilización del sistema
                var timeFrame = CollectUtilizationData(startTime, endTime, vmConnectExists);
                lastEndTime = endTime;
                WriteToFile(currentOutputFile, timeFrame);
            }
        }

        #region Métodos de Gestión de VMs

        /// <summary>
        /// Verifica si existen procesos vmconnect usando la clase MonitoringVMs
        /// </summary>
        /// <returns>True si hay procesos vmconnect activos</returns>
        private static bool CheckVmConnectProcesses()
        {
            try
            {
                if (vmMonitor == null)
                {
                    LogError("Monitor de VMs no inicializado");
                    return false;
                }

                // Verificar VMs remotas de forma asíncrona
                var vmCheckTask = Task.Run(() => vmMonitor.CheckVMsAsync());
                var vmCheck = vmCheckTask.GetAwaiter().GetResult();

                // Verificar procesos locales de Hyper-V
                var hasLocalHyperV = vmMonitor.CheckLocalVmConnectProcesses();

                // Retornar true si cualquiera de las verificaciones es positiva
                if (vmCheck || hasLocalHyperV)
                {
                    LogInfo($"VMs detectadas - Remotas: {vmCheck}, Locales: {hasLocalHyperV}");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                LogError($"Error verificando procesos vmconnect: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Actualiza el tiempo de detección de conexión VM
        /// </summary>
        /// <param name="vmConnectExists">Si existen conexiones VM actualmente</param>
        private static void UpdateVmConnectionDetectionTime(bool vmConnectExists)
        {
            // Solo actualizar lastVmConnectedDetection cuando detectamos VMConnect por PRIMERA vez
            // o cuando VMConnect no se detectó en el ciclo anterior pero ahora sí se detecta
            if (vmConnectExists && lastVmConnectedDetection == DateTime.MinValue)
            {
                lastVmConnectedDetection = DateTime.UtcNow;
                LogInfo("Procesos VMConnect detectados por primera vez, iniciando temporizador de timeout");
            }
            else if (vmConnectExists)
            {
                LogInfo("Procesos VMConnect aún en ejecución");
            }
            else
            {
                // Si VMConnect ya no está en ejecución, resetear el tiempo de detección
                if (lastVmConnectedDetection != DateTime.MinValue)
                {
                    LogInfo("Procesos VMConnect ya no detectados, reseteando temporizador");
                    lastVmConnectedDetection = DateTime.MinValue;
                }
            }
            LogInfo("No hay VMs abiertas ni temportizador a resetear");
        }

        /// <summary>
        /// Procesa cualquier cierre de VM pendiente usando la clase MonitoringVMs
        /// </summary>
        private static void ProcessPendingVmKills()
        {
            try
            {
                if (vmMonitor != null)
                {
                    // Ejecutar de forma síncrona y capturar el valor actualizado
                    var task = Task.Run(() => vmMonitor.ProcessPendingKillsAsync(lastVmConnectedDetection));
                    lastVmConnectedDetection = task.GetAwaiter().GetResult();
                    LogInfo("Cierres pendientes procesados");
                }
                else
                {
                    LogError("Monitor de VMs no disponible para procesar cierres pendientes");
                }
            }
            catch (Exception ex)
            {
                LogError($"Error procesando cierres pendientes: {ex.Message}");
            }
        }


        /// <summary>
        /// Verifica si debe programarse el cierre de VMs
        /// </summary>
        /// <returns>True si debe programarse el cierre</returns>
        private static bool ShouldScheduleVmClose(DateTime lastVmConnectedDetection)
        {
            if (vmMonitor == null)
            {
                LogError("Monitor de VMs no disponible para verificar timeout");
                return false;
            }

            return vmMonitor.ShouldScheduleVmClose(lastVmConnectedDetection);
        }

        /// <summary>
        /// Programa el cierre y terminación de VMs usando la clase MonitoringVMs
        /// </summary>
        private static void ScheduleVmCloseAndKill()
        {
            try
            {
                if (vmMonitor != null)
                {
                    // Ejecutar de forma asíncrona para no bloquear el hilo principal
                    Task.Run(async () =>
                    {
                        try
                        {
                            LogInfo("await vmMonitor.ScheduleVmCloseAndKillAsync()");
                            await vmMonitor.ScheduleVmCloseAndKillAsync();
                        }
                        catch (Exception ex)
                        {
                            LogError($"Error en programación asíncrona de cierre de VM: {ex.Message}");
                        }
                    });
                }
                else
                {
                    LogError("Monitor de VMs no disponible para programar cierre");
                }
            }
            catch (Exception ex)
            {
                LogError($"Error programando cierre de VM: {ex.Message}");
            }
        }

        #endregion

        #region Métodos de Recopilación de Datos

        private static string GetProductPartNumber()
        {
            try
            {
                LogInfo("Starting enhanced product detection logic...");

                string hdmxPath = @"D:\HDMT3\HdmtOutputFiles";
                if (Directory.Exists(hdmxPath))
                {
                    LogInfo("HDMX machine detected - checking TesterHwConfig.xml");
                    string product = GetProductFromHDMX(hdmxPath);
                    if (!string.IsNullOrEmpty(product))
                    {
                        LogInfo($"Successfully retrieved product from HDMX: {product}");
                        return product;
                    }
                    LogInfo("HDMX path exists but no product found, falling back to HST methods");
                }
                else
                {
                    LogInfo("HDMX path not found, assuming HST machine");
                }

                string hstMethod1Product = GetProductFromHSTMethod1();
                if (!string.IsNullOrEmpty(hstMethod1Product))
                {
                    LogInfo($"Successfully retrieved product from HST Method 1: {hstMethod1Product}");
                    return hstMethod1Product;
                }

                string hstMethod2Product = GetProductFromHSTMethod2();
                if (!string.IsNullOrEmpty(hstMethod2Product))
                {
                    LogInfo($"Successfully retrieved product from HST Method 2: {hstMethod2Product}");
                    return hstMethod2Product;
                }

                LogError("All product detection methods failed");
                return "PRODUCT_NOT_FOUND";
            }
            catch (Exception ex)
            {
                LogError($"Error in GetProductPartNumber: {ex.Message}");
                return "ERROR_GETTING_PRODUCT";
            }
        }
        private static string GetProductFromHDMX2(string hdmxPath)
        {
            try
            {
                string configFilePath = Path.Combine(hdmxPath, "TesterHwConfig.xml");
                if (!File.Exists(configFilePath))
                {
                    LogInfo($"TesterHwConfig.xml not found at: {configFilePath}");
                    return "";
                }

                LogInfo($"Reading TesterHwConfig.xml from: {configFilePath}");
                string xmlContent = File.ReadAllText(configFilePath);

                // Nuevo regex para buscar DUTSocketSerialNumber0
                var dutSocketSerialRegex = new System.Text.RegularExpressions.Regex(
                    @"<SupplementalData\s+Name=""DUTSocketSerialNumber0""\s+Value=""([^""]+)""",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled
                );

                var match = dutSocketSerialRegex.Match(xmlContent);
                if (match.Success)
                {
                    string dutSerialNumber = match.Groups[1].Value.Trim();
                    LogInfo($"Found DUTSocketSerialNumber0 in HDMX config: {dutSerialNumber}");
                    return dutSerialNumber;
                }
                else
                {
                    LogInfo("DUTSocketSerialNumber0 pattern not found in TesterHwConfig.xml");
                    return "";
                }
            }
            catch (Exception ex)
            {
                LogError($"Error reading HDMX config: {ex.Message}");
                return "";
            }
        }
        private static string GetProductFromHDMX(string hdmxPath)
        {
            try
            {
                string configFilePath = Path.Combine(hdmxPath, "TesterHwConfig.xml");

                if (!File.Exists(configFilePath))
                {
                    LogInfo($"TesterHwConfig.xml not found at: {configFilePath}");
                    return "";
                }

                LogInfo($"Reading TesterHwConfig.xml from: {configFilePath}");

                string xmlContent = File.ReadAllText(configFilePath);

                var tiuSerialNumberRegex = new System.Text.RegularExpressions.Regex(
                    @"BoardName=""TIUEEPROM1""[^>]*SerialNumber=""([^""]+)""",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled
                );

                var match = tiuSerialNumberRegex.Match(xmlContent);
                if (match.Success) // && match.in(json_config_product_dictionary)
                {
                    string serialNumber = match.Groups[1].Value.Trim();

                    LogInfo($"Found TIU SerialNumber in HDMX config: {serialNumber}");
                    return serialNumber;
                }
                else
                {
                    // try GetProductFromHDMX2, if also fails then:
                    LogInfo("TIU SerialNumber pattern not found in TesterHwConfig.xml");
                    return "";
                }
            }
            catch (Exception ex)
            {
                LogError($"Error reading HDMX config: {ex.Message}");
                return "";
            }
        }

        private static string GetProductFromHSTMethod1()
        {
            try
            {
                string hstCachePath = @"c:\hst\tpcache\o\D7";

                if (!Directory.Exists(hstCachePath))
                {
                    LogInfo($"HST cache path not found: {hstCachePath}");
                    return "";
                }

                LogInfo($"Checking HST cache path: {hstCachePath}");

                string[] firstLevelDirs = Directory.GetDirectories(hstCachePath);

                if (firstLevelDirs.Length == 0)
                {
                    LogInfo("No directories found in HST cache D7 folder");
                    return "";
                }

                string firstDir = firstLevelDirs[0];
                LogInfo($"Found first level directory: {Path.GetFileName(firstDir)}");

                string[] secondLevelDirs = Directory.GetDirectories(firstDir);

                if (secondLevelDirs.Length == 0)
                {
                    LogInfo("No second level directories found in HST cache");
                    return "";
                }

                string secondDir = secondLevelDirs[0];
                string productName = Path.GetFileName(secondDir);

                LogInfo($"Found second level directory (product): {productName}");
                return productName;
            }
            catch (Exception ex)
            {
                LogError($"Error in HST Method 1: {ex.Message}");
                return "";
            }
        }

        private static string GetProductFromHSTMethod2()
        {
            try
            {
                string hstLoopsPath = @"D:\HST\TP_ENG_Loops\TP";

                if (!Directory.Exists(hstLoopsPath))
                {
                    LogInfo($"HST loops path not found: {hstLoopsPath}");
                    return "";
                }

                LogInfo($"Checking HST loops path: {hstLoopsPath}");

                string[] zipFiles = Directory.GetFiles(hstLoopsPath, "*.zip");

                if (zipFiles.Length == 0)
                {
                    LogInfo("No ZIP files found in HST loops directory");
                    return "";
                }

                LogInfo($"Found {zipFiles.Length} ZIP files in HST loops directory");

                Array.Sort(zipFiles, (x, y) => File.GetLastWriteTime(y).CompareTo(File.GetLastWriteTime(x)));

                string mostRecentZip = zipFiles[0];
                string productName = Path.GetFileNameWithoutExtension(mostRecentZip);

                LogInfo($"Most recent ZIP file: {Path.GetFileName(mostRecentZip)}");
                LogInfo($"Product name from ZIP: {productName}");

                return productName;
            }
            catch (Exception ex)
            {
                LogError($"Error in HST Method 2: {ex.Message}");
                return "";
            }
        }

        /// <summary>
        /// Recopila datos de utilización del sistema para el período especificado
        /// </summary>
        /// <param name="startTime">Tiempo de inicio del período</param>
        /// <param name="endTime">Tiempo de fin del período</param>
        /// <returns>Objeto UtilizationTimeFrame con los datos recopilados</returns>
        private static UtilizationTimeFrame CollectUtilizationData(DateTime startTime, DateTime endTime, bool vmInUse)
        {
            var timeFrame = new UtilizationTimeFrame
            {
                StartTime = startTime,
                EndTime = endTime,
                MachineName = Environment.MachineName,
                Product = GetProductPartNumber()
            };

            // Recopilar eventos de entrada (mouse y teclado)
            if (inputHook != null)
            {
                timeFrame.MouseEvents = inputHook.GetMouseEventCount();
                timeFrame.KeyboardEvents = inputHook.GetKeyboardEventCount();
            }

            // Verificar uso de VMs
            // bool vmInUse = CheckVmsInUse();

            // Decidir si monitorear archivos basado en el uso de VMs
            if (vmInUse)
            {
                timeFrame.FileChanges = "VMs In Use By VMC or hyperV";
                LogInfo("INFO: VMs detectadas en uso, omitiendo monitoreo de archivos");
            }
            //else
            //{
            //    timeFrame = MonitoringSUM.MonitoringFiles(timeFrame, appConfig, logInfo);
            //    LogInfo("INFO: No hay VMs en uso o verificación de VM falló, procediendo con monitoreo de archivos");
            //}


            timeFrame = MonitoringSUM.MonitoringFiles(timeFrame, appConfig, logInfo);
            LogInfo("INFO: No hay VMs en uso o verificación de VM falló, procediendo con monitoreo de archivos");

            return timeFrame;
        }

        /// <summary>
        /// Verifica si las VMs están en uso con timeout para evitar bloqueos
        /// </summary>
        /// <returns>True si las VMs están en uso</returns>
        private static bool CheckVmsInUse()
        {
            if (vmMonitor == null)
            {
                LogError("Monitor de VMs no disponible para verificación de uso");
                return false;
            }

            try
            {
                // Establecer timeout para la verificación de VM para prevenir colgados
                var vmCheckTask = Task.Run(() => vmMonitor.CheckVMsAsync());

                // Esperar a que la tarea se complete con timeout (ej. 5 segundos)
                if (vmCheckTask.Wait(TimeSpan.FromSeconds(5)))
                {
                    bool vmInUse = vmCheckTask.Result;
                    LogInfo($"INFO: Verificación de VM completada exitosamente. VMs en uso: {vmInUse}");
                    return vmInUse;
                }
                else
                {
                    LogInfo("ERROR: Verificación de VM agotó tiempo después de 5 segundos, continuando con monitoreo de archivos");
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogError($"ERROR: Monitoreo de VM falló con error: {ex.Message}, continuando con monitoreo de archivos");
                return false;
            }
        }

        #endregion

        #region Métodos de Utilidad

        /// <summary>
        /// Oculta la ventana de consola cuando no está en modo debug
        /// </summary>
        private static void HideConsoleWindow()
        {
            var handle = GetConsoleWindow();
            if (handle != IntPtr.Zero)
                ShowWindow(handle, 0);
            try { FreeConsole(); } catch { }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool FreeConsole();

        /// <summary>
        /// Carga la configuración de la aplicación desde archivo JSON
        /// </summary>
        private static void LoadConfiguration()
        {
            string configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Intel", "SystemUtilizationMonitor", "SystemUtilizationConfig.json");

            if (!File.Exists(configPath))
                CreateDefaultConfiguration(configPath);

            string jsonContent = File.ReadAllText(configPath);
            appConfig = JsonConvert.DeserializeObject<ConfigurationModel>(jsonContent);
        }

        /// <summary>
        /// Crea una configuración por defecto si no existe el archivo de configuración
        /// </summary>
        /// <param name="configPath">Ruta donde crear el archivo de configuración</param>
        private static void CreateDefaultConfiguration(string configPath)
        {

            Directory.CreateDirectory(Path.GetDirectoryName(configPath));

            var defaultConfig = new ConfigurationModel
            {
                Jose = new Dictionary<string, MonitorTxtConfig>
                {
                    ["montior_txt_priority"] = new MonitorTxtConfig
                    {
                        FilePath = "C:\\STHI\\logs\\strut_detail_log_yyyy/MM/dd.txt",
                        NoContent = "RmqEventsListener",
                        Skip = "GetStatus;RetrieveHWConfigInfo =;CommandType =;CommandSource =;UniqueCommandId =;SysCClientUniqueCommandId =;SiteId =;AdditionalParameters =;TesterInfo.get_VMImageVersion - VMImageVersion:;TpCache.GetCachedTps - Test package caching is not currently implemented.;NetworkConfigurator.get_IpAddressToSpacialLocation - IP location mapping:;localhost: 1;HwConfig.CollectHwConfig;HwConfig.CreateSocketEntities;HwConfig.parseCMMSList;HwConfig.SerializeXml;xml version=;HWConfiguration;</;/>;<SocketEntity;<TesterExternalEntity;<TesterExternalEntity;<BoardBLT;<TesterCoreEntity;TesterHWConfigAsXMLString",
                        FormatDate = "yyyy/MM/dd",
                        LastlineContent = "EventManager.SendEvent - Send SiteInformationEvent Event to Supervisor for command UndefinedSiteCommand, uniqueCommandId 8888888888888888888, SysCClientUniqueCommandId:"
                    },
                    ["montior_txt_normal_1"] = new MonitorTxtConfig
                    {
                        FilePath = "C:\\Logs\\Aguila\\Sequencer 1\\TraceLog.txt"
                    },
                    ["montior_txt_normal_2"] = new MonitorTxtConfig
                    {
                        FilePath = "C:\\Logs\\Aguila\\Sequencer 2\\TraceLog.txt"
                    },
                    ["montior_txt_normal_3"] = new MonitorTxtConfig
                    {
                        FilePath = "C:\\Logs\\Aguila\\Sequencer 3\\TraceLog.txt"
                    },
                    ["montior_txt_normal_4"] = new MonitorTxtConfig
                    {
                        FilePath = "C:\\Logs\\Aguila\\Sequencer 4\\TraceLog.txt"
                    }
                },
                SumPOR = new SumPORConfig
                {
                    ShouldReadLogFiles = true,
                    Debug = false,
                    ProductLogPath = @"",
                    Args = new ArgsConfig
                    {
                        RollingInterval = "Day",
                        RetainedFileCountLimit = 15,
                        OutputTemplate = "{Message:l}{NewLine}"
                    }
                },
                Mouse = new MouseConfig
                {
                    WM_LBUTTONDOWN = 0x0201,
                    WM_RBUTTONDOWN = 0x0204,
                    WM_MBUTTONDOWN = 0x0207,
                    WM_MOUSEMOVE = 0x0200,
                    WM_MOUSEWHEEL = 0x020A,
                    MouseMoveThrottleMs = 100
                },
                Keyboard = new KeyboardConfig
                {
                    WM_KEYDOWN = 0x0100,
                    WM_SYSKEYDOWN = 0x0104
                },
                Hook = new HookConfig
                {
                    WH_KEYBOARD_LL = 13,
                    WH_MOUSE_LL = 14
                },
                JsonOutputPath = ""
            };

            string jsonContent = JsonConvert.SerializeObject(defaultConfig, Formatting.Indented);
            File.WriteAllText(configPath, jsonContent);
        }
        /// <summary>
        /// Configura el directorio de salida para archivos de monitoreo
        /// </summary>
        private static void SetupOutputDirectory()
        {
            outputDirectory = !string.IsNullOrEmpty(appConfig.JsonOutputPath)
                ? Environment.ExpandEnvironmentVariables(appConfig.JsonOutputPath)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Intel", "SystemUtilizationMonitor");

            if (!Directory.Exists(outputDirectory))
                Directory.CreateDirectory(outputDirectory);
        }

        /// <summary>
        /// Inicializa variables para el día actual
        /// </summary>
        private static void InitializeForCurrentDay()
        {
            currentDay = DateTime.UtcNow.Date;
            currentOutputFile = Path.Combine(outputDirectory, $"SystemUtilizationTimeFrames{currentDay:yyyyMMdd}.json");
        }

        /// <summary>
        /// Configura los parámetros de monitoreo
        /// </summary>
        private static void SetupMonitoringConfiguration()
        {
            config = new MonitorConfiguration();
            config.RecordInterval = TimeSpan.FromMinutes(appConfig.Monitoring.RecordIntervalMinutes);
        }

        /// <summary>
        /// Configura el manejo de señales de cancelación (Ctrl+C)
        /// </summary>
        private static void SetupCancellation()
        {
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                shouldStop = true;
            };
        }

        /// <summary>
        /// Inicializa los hooks de entrada (mouse y teclado)
        /// </summary>
        private static void InitializeInputHooks()
        {
            try
            {
                inputHook = new InputHookManager(appConfig);
                inputHook?.Start();
                LogInfo("Monitoreo de entrada inicializado exitosamente");
            }
            catch (Exception ex)
            {
                LogError("No se pudieron inicializar hooks de entrada: " + ex.Message);
            }
        }

        /// <summary>
        /// Inicia tarea de limpieza de archivos en segundo plano
        /// </summary>
        private static void StartFileCleanupTask()
        {
            Task.Factory.StartNew(() =>
            {
                while (!shouldStop)
                {
                    try
                    {
                        Thread.Sleep(TimeSpan.FromHours(1));
                        // Agregar lógica de limpieza de archivos aquí si es necesario
                    }
                    catch (Exception ex)
                    {
                        LogError("Error en limpieza de archivos: " + ex.Message);
                    }
                }
            });
        }

        /// <summary>
        /// Resetea contadores de entrada para el siguiente ciclo
        /// </summary>
        private static void ResetCounters()
        {
            inputHook?.ResetCounters();
        }

        /// <summary>
        /// Escribe datos de utilización al archivo JSON usando el serializador personalizado
        /// </summary>
        /// <param name="fileName">Nombre del archivo de salida</param>
        /// <param name="timeFrame">Datos de utilización a escribir</param>
        private static void WriteToFile(string fileName, UtilizationTimeFrame timeFrame)
        {
            try
            {
                var json = CustomJsonSerializer.Serialize(timeFrame);
                File.AppendAllText(fileName, json + Environment.NewLine);
            }
            catch (Exception ex)
            {
                LogError("Error escribiendo al archivo: " + ex.Message);
            }
        }

        /// <summary>
        /// Registra mensaje de información en consola y archivo de log
        /// </summary>
        /// <param name="message">Mensaje a registrar</param>
        private static void LogInfo(string message)
        {
            if (appConfig?.SumPOR?.Debug == true)
                Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] INFO: {message}");

            try
            {
                logInfo += $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] INFO: {message}{Environment.NewLine}";
                string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Intel", "SystemUtilizationMonitor", "Monitoring_logs.txt");
                File.AppendAllText(logPath, logInfo);
            }
            catch { }
        }

        /// <summary>
        /// Registra mensaje de error en consola y archivo de log
        /// </summary>
        /// <param name="message">Mensaje de error a registrar</param>
        private static void LogError(string message)
        {
            if (appConfig?.SumPOR?.Debug == true)
                Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] ERROR: {message}");

            try
            {
                logInfo += $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] ERROR: {message}{Environment.NewLine}";
                string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Intel", "SystemUtilizationMonitor", "Monitoring_logs.txt");
                File.AppendAllText(logPath, logInfo);
            }
            catch { }
        }

        /// <summary>
        /// Limpia recursos antes de cerrar la aplicación
        /// </summary>
        private static void Cleanup()
        {
            inputHook?.Dispose();
            LogInfo("Limpieza completada.");
        }

        #endregion
    }
}