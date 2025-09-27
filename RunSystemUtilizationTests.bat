@echo off
setlocal enabledelayedexpansion

REM RunSystemUtilizationTests.bat
REM Batch script to run SystemUtilizationMonitor validation tests
REM Fixed version with better network path handling

REM Default parameters
set "SOURCE_PATH=\\amr.corp.intel.com\ec\proj\mdl\cr\intel\hdmx_db\mae\SUM\HDMx"
set "RESULTS_PATH=\\amr.corp.intel.com\ec\proj\mdl\cr\intel\hdmx_db\mae\SUM\Results"
set "MAX_CSV_FILES=25"
set "BUILD_CONFIG=Release"
set "PROJECT_PATH=SystemUtilizationMonitor.Tests\SystemUtilizationMonitor.Tests.csproj"
set "GIT_COMMIT="
set "GIT_BRANCH="
set "BUILD_NUMBER="
set "VERBOSE=false"
set "USE_LOCAL_RESULTS=false"

REM Parse command line arguments
:parse_args
if "%~1"=="" goto start_tests
if /i "%~1"=="--source" (
    set "SOURCE_PATH=%~2"
    shift
    shift
    goto parse_args
)
if /i "%~1"=="--results" (
    set "RESULTS_PATH=%~2"
    shift
    shift
    goto parse_args
)
if /i "%~1"=="--config" (
    set "BUILD_CONFIG=%~2"
    shift
    shift
    goto parse_args
)
if /i "%~1"=="--project" (
    set "PROJECT_PATH=%~2"
    shift
    shift
    goto parse_args
)
if /i "%~1"=="--verbose" (
    set "VERBOSE=true"
    shift
    goto parse_args
)
if /i "%~1"=="--local-results" (
    set "USE_LOCAL_RESULTS=true"
    shift
    goto parse_args
)
if /i "%~1"=="--help" (
    goto show_help
)
shift
goto parse_args

:show_help
echo SystemUtilizationMonitor Test Runner
echo ===================================
echo.
echo Usage: RunSystemUtilizationTests.bat [options]
echo.
echo Options:
echo   --source PATH        Source path for CSV files
echo   --results PATH       Results output path
echo   --config CONFIG      Build configuration (Debug/Release)
echo   --project PATH       Project file path
echo   --verbose            Enable verbose output
echo   --local-results      Use local temp directory for results
echo   --help               Show this help
echo.
exit /b 0

:start_tests
echo ========================================
echo SystemUtilizationMonitor Test Runner
echo ========================================
echo.

REM Get timestamp for results directory
for /f "tokens=2-4 delims=/ " %%a in ('date /t') do set "DATE_STAMP=%%c%%a%%b"
for /f "tokens=1-2 delims=: " %%a in ('time /t') do set "TIME_STAMP=%%a%%b"
set "TIME_STAMP=%TIME_STAMP: =0%"
set "TIMESTAMP=%DATE_STAMP%_%TIME_STAMP%"

REM Handle network path issues
if "%USE_LOCAL_RESULTS%"=="true" (
    set "RESULTS_DIR=%TEMP%\SUM_TestResults\TestRun_%TIMESTAMP%"
    echo Using local results directory due to --local-results flag
) else (
    REM Try to use network path, fall back to local if it fails
    set "RESULTS_DIR=%RESULTS_PATH%\TestRun_%TIMESTAMP%"
)

REM Create results directory with better error handling
echo Creating results directory...
call :create_results_directory
if errorlevel 1 exit /b 1

echo Results directory created: %RESULTS_DIR%
echo.

REM Check network connectivity to source path
echo Checking source path accessibility...
if exist "%SOURCE_PATH%" (
    echo Source path is accessible: %SOURCE_PATH%
) else (
    echo WARNING: Source path not accessible: %SOURCE_PATH%
    echo This may affect CSV file discovery.
)
echo.

REM Check .NET installation
echo Checking .NET installation...
dotnet --version >nul 2>&1
if errorlevel 1 (
    echo ERROR: .NET SDK not found. Please install .NET 6.0 or later.
    exit /b 1
)
for /f %%i in ('dotnet --version 2^>nul') do set "DOTNET_VERSION=%%i"
echo .NET SDK found: %DOTNET_VERSION%
echo.

REM Check project file
echo Checking project file...
if not exist "%PROJECT_PATH%" (
    echo ERROR: Project file not found: %PROJECT_PATH%
    echo Current directory: %CD%
    echo Please ensure you're running from the correct directory.
    exit /b 1
)
echo Project file found: %PROJECT_PATH%
echo.

REM Build the solution
echo Building solution...
dotnet build "%PROJECT_PATH%" --configuration %BUILD_CONFIG% --verbosity minimal
if errorlevel 1 (
    echo ERROR: Build failed
    pause
    exit /b 1
)
echo Build completed successfully
echo.

REM Discover CSV files
echo Discovering CSV files in: %SOURCE_PATH%
set "CSV_COUNT=0"
set "CSV_FILES_LIST=%RESULTS_DIR%\csv_files.txt"

if exist "%CSV_FILES_LIST%" del "%CSV_FILES_LIST%"

if exist "%SOURCE_PATH%" (
    echo Searching for hdmx*.csv files...
    for %%f in ("%SOURCE_PATH%\hdmx*.csv") do (
        if exist "%%f" (
            set /a CSV_COUNT+=1
            echo %%f >> "%CSV_FILES_LIST%"
            if "%VERBOSE%"=="true" echo   Found: %%~nxf
            if !CSV_COUNT! geq %MAX_CSV_FILES% goto csv_discovery_done
        )
    )
) else (
    echo WARNING: Source path not accessible: %SOURCE_PATH%
)

:csv_discovery_done
echo Found %CSV_COUNT% CSV files for testing
if %CSV_COUNT% equ 0 (
    echo WARNING: No CSV files found. Tests will run without CSV validation.
)
echo.

REM Initialize test results
set "PASSED_TESTS=0"
set "FAILED_TESTS=0"
set "ERROR_TESTS=0"
set "TEST_RESULTS=%RESULTS_DIR%\test_results.txt"
set "TEST_SUMMARY=%RESULTS_DIR%\test_summary.txt"

if exist "%TEST_RESULTS%" del "%TEST_RESULTS%"
if exist "%TEST_SUMMARY%" del "%TEST_SUMMARY%"

echo Test Results > "%TEST_RESULTS%"
echo ============= >> "%TEST_RESULTS%"
echo Generated: %DATE% %TIME% >> "%TEST_RESULTS%"
echo Machine: %COMPUTERNAME% >> "%TEST_RESULTS%"
echo User: %USERNAME% >> "%TEST_RESULTS%"
echo Build Config: %BUILD_CONFIG% >> "%TEST_RESULTS%"
echo .NET Version: %DOTNET_VERSION% >> "%TEST_RESULTS%"
echo. >> "%TEST_RESULTS%"

REM Run CSV validation tests
if %CSV_COUNT% gtr 0 (
    echo Running CSV validation tests...
    echo.
    
    set "TEST_NUM=0"
    for /f "usebackq delims=" %%f in ("%CSV_FILES_LIST%") do (
        set /a TEST_NUM+=1
        echo [!TEST_NUM!/%CSV_COUNT%] Testing: %%~nxf
        call :run_csv_test "%%f"
    )
) else (
    echo No CSV files found for testing. Skipping CSV validation tests.
    echo.
)

REM Run comprehensive tests
echo Running comprehensive test suite...
set "COMP_LOG=%RESULTS_DIR%\comprehensive_tests.log"
set "COMP_TRX=%RESULTS_DIR%\ComprehensiveTests_%TIMESTAMP%.trx"

dotnet test "%PROJECT_PATH%" --configuration %BUILD_CONFIG% --logger "trx;LogFileName=%COMP_TRX%" --results-directory "%RESULTS_DIR%" --verbosity normal > "%COMP_LOG%" 2>&1

if errorlevel 1 (
    echo WARNING: Comprehensive tests completed with some failures
    set "COMP_STATUS=Failed"
) else (
    echo Comprehensive tests completed successfully  
    set "COMP_STATUS=Passed"
)
echo.

REM Calculate success rate
set /a TOTAL_TESTS=%PASSED_TESTS%+%FAILED_TESTS%+%ERROR_TESTS%
if %TOTAL_TESTS% gtr 0 (
    set /a SUCCESS_RATE=(%PASSED_TESTS%*100)/%TOTAL_TESTS%
) else (
    set "SUCCESS_RATE=0"
)

REM Generate summary report
call :generate_summary

REM Display final results
echo ========================================
echo TEST EXECUTION SUMMARY
echo ========================================
echo Total CSV Files Tested: %TOTAL_TESTS%
echo Passed Tests: %PASSED_TESTS%
echo Failed Tests: %FAILED_TESTS%
echo Error Tests: %ERROR_TESTS%
echo Success Rate: %SUCCESS_RATE%%%
echo Comprehensive Tests: %COMP_STATUS%
echo Results Location: %RESULTS_DIR%
echo ========================================

REM Copy results to network if we used local directory
if "%USE_LOCAL_RESULTS%"=="true" (
    call :copy_results_to_network
)

echo.
echo Test execution completed. Check the results directory for detailed logs.
if "%VERBOSE%"=="true" pause

REM Determine exit code
if %FAILED_TESTS% equ 0 if %ERROR_TESTS% equ 0 if "%COMP_STATUS%"=="Passed" (
    echo.
    echo ✓ All tests passed!
    exit /b 0
) else if %PASSED_TESTS% gtr 0 (
    echo.
    echo ⚠ Some tests failed, but some passed
    exit /b 1
) else (
    echo.
    echo ✗ All tests failed or had errors
    exit /b 2
)

REM Function to create results directory with better error handling
:create_results_directory
REM First, try to create the base results path if it doesn't exist
if not exist "%RESULTS_PATH%" (
    echo Creating base results path: %RESULTS_PATH%
    mkdir "%RESULTS_PATH%" 2>nul
    if errorlevel 1 (
        echo WARNING: Cannot create network results directory. Falling back to local directory.
        set "USE_LOCAL_RESULTS=true"
        set "RESULTS_DIR=%TEMP%\SUM_TestResults\TestRun_%TIMESTAMP%"
        echo New results directory will be: %RESULTS_DIR%
    )
)

REM Create the timestamped results directory
if not exist "%RESULTS_DIR%" (
    mkdir "%RESULTS_DIR%" 2>nul
    if errorlevel 1 (
        if "%USE_LOCAL_RESULTS%"=="false" (
            echo WARNING: Cannot create network results directory. Falling back to local directory.
            set "USE_LOCAL_RESULTS=true" 
            set "RESULTS_DIR=%TEMP%\SUM_TestResults\TestRun_%TIMESTAMP%"
            mkdir "%RESULTS_DIR%" 2>nul
            if errorlevel 1 (
                echo ERROR: Cannot create even local results directory: %RESULTS_DIR%
                exit /b 1
            )
        ) else (
            echo ERROR: Cannot create local results directory: %RESULTS_DIR%
            exit /b 1
        )
    )
)

REM Verify directory was created and is writable
echo. > "%RESULTS_DIR%\test_write.tmp" 2>nul
if exist "%RESULTS_DIR%\test_write.tmp" (
    del "%RESULTS_DIR%\test_write.tmp" 2>nul
) else (
    echo ERROR: Results directory is not writable: %RESULTS_DIR%
    exit /b 1
)

exit /b 0

REM Function to run individual CSV test
:run_csv_test
set "CSV_FILE=%~1"
for %%f in ("%CSV_FILE%") do set "CSV_NAME=%%~nxf"

set "TEST_LOG=%RESULTS_DIR%\test_%CSV_NAME%.log"
set "TEST_TRX=%RESULTS_DIR%\Test_%CSV_NAME%_%TIMESTAMP%.trx"

REM Run the test with timeout
echo   Running test for %CSV_NAME%...

REM Create a temporary batch file to run the test with timeout
set "TEMP_BAT=%TEMP%\run_test_%RANDOM%.bat"
echo @echo off > "%TEMP_BAT%"
echo dotnet test "%PROJECT_PATH%" --configuration %BUILD_CONFIG% --logger "trx;LogFileName=%TEST_TRX%" --results-directory "%RESULTS_DIR%" --filter "FullyQualifiedName~ValidateSystemUtilizationData_CsvVsJson_ShouldMatch" --verbosity normal -- "TestRunParameters.Parameter(name=\"CsvFilePath\",value=\"%CSV_FILE%\")" ^> "%TEST_LOG%" 2^>^&1 >> "%TEMP_BAT%"

REM Run with timeout (5 minutes)
timeout 300 "%TEMP_BAT%" >nul 2>&1
set "TEST_EXIT_CODE=%ERRORLEVEL%"

REM Clean up temp file
if exist "%TEMP_BAT%" del "%TEMP_BAT%" 2>nul

if %TEST_EXIT_CODE% equ 124 (
    echo   TIMEOUT: %CSV_NAME% (test exceeded 5 minutes)
    set /a ERROR_TESTS+=1
    echo TIMEOUT: %CSV_NAME% - %DATE% %TIME% >> "%TEST_RESULTS%"
) else if %TEST_EXIT_CODE% neq 0 (
    echo   FAILED: %CSV_NAME%
    set /a FAILED_TESTS+=1
    echo FAILED: %CSV_NAME% - %DATE% %TIME% >> "%TEST_RESULTS%"
    if "%VERBOSE%"=="true" (
        echo   Log: %TEST_LOG%
        echo   Exit Code: %TEST_EXIT_CODE%
    )
) else (
    echo   PASSED: %CSV_NAME%
    set /a PASSED_TESTS+=1
    echo PASSED: %CSV_NAME% - %DATE% %TIME% >> "%TEST_RESULTS%"
)

echo. >> "%TEST_RESULTS%"
goto :eof

REM Function to generate summary report
:generate_summary
echo Generating summary report...

echo SystemUtilizationMonitor Test Summary > "%TEST_SUMMARY%"
echo ===================================== >> "%TEST_SUMMARY%"
echo. >> "%TEST_SUMMARY%"
echo Generated: %DATE% %TIME% >> "%TEST_SUMMARY%"
echo Machine: %COMPUTERNAME% >> "%TEST_SUMMARY%"
echo User: %USERNAME% >> "%TEST_SUMMARY%"
echo. >> "%TEST_SUMMARY%"
echo Test Results: >> "%TEST_SUMMARY%"
echo   Total CSV Files Tested: %TOTAL_TESTS% >> "%TEST_SUMMARY%"
echo   Passed Tests: %PASSED_TESTS% >> "%TEST_SUMMARY%"
echo   Failed Tests: %FAILED_TESTS% >> "%TEST_SUMMARY%"
echo   Error Tests: %ERROR_TESTS% >> "%TEST_SUMMARY%"
echo   Success Rate: %SUCCESS_RATE%%% >> "%TEST_SUMMARY%"
echo   Comprehensive Tests: %COMP_STATUS% >> "%TEST_SUMMARY%"
echo. >> "%TEST_SUMMARY%"
echo Environment: >> "%TEST_SUMMARY%"
echo   Build Configuration: %BUILD_CONFIG% >> "%TEST_SUMMARY%"
echo   .NET Version: %DOTNET_VERSION% >> "%TEST_SUMMARY%"
echo   Source Path: %SOURCE_PATH% >> "%TEST_SUMMARY%"
echo   Results Path: %RESULTS_DIR% >> "%TEST_SUMMARY%"
echo   Used Local Results: %USE_LOCAL_RESULTS% >> "%TEST_SUMMARY%"
echo. >> "%TEST_SUMMARY%"

echo Summary report generated: %TEST_SUMMARY%
goto :eof

REM Function to copy results to network location
:copy_results_to_network
echo Attempting to copy results to network location...
set "NETWORK_RESULTS_DIR=%RESULTS_PATH%\TestRun_%TIMESTAMP%"

if exist "%RESULTS_PATH%" (
    mkdir "%NETWORK_RESULTS_DIR%" 2>nul
    if not errorlevel 1 (
        echo Copying files to: %NETWORK_RESULTS_DIR%
        xcopy "%RESULTS_DIR%\*.*" "%NETWORK_RESULTS_DIR%\" /Y /Q >nul 2>&1
        if not errorlevel 1 (
            echo ✓ Results copied to network location: %NETWORK_RESULTS_DIR%
        ) else (
            echo WARNING: Failed to copy results to network location
        )
    ) else (
        echo WARNING: Could not create network results directory
    )
) else (
    echo WARNING: Network results path not accessible
)
goto :eof