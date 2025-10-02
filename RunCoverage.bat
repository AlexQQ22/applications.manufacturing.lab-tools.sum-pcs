@echo off
echo Running tests with code coverage exclusions...
dotnet test SystemUtilizationMonitor.Tests\SystemUtilizationMonitor.Tests.csproj --settings coverlet.runsettings --collect:"XPlat Code Coverage" --results-directory "./TestResults"

if %errorlevel% neq 0 (
    echo Tests failed or had errors!
    echo Continuing to generate report anyway...
)

echo.
echo Generating HTML coverage report...
reportgenerator -reports:"./TestResults/*/coverage.cobertura.xml" -targetdir:"./CoverageReport" -reporttypes:Html

if %errorlevel% neq 0 (
    echo Failed to generate report!
    pause
    exit /b %errorlevel%
)

echo.
echo Opening coverage report...
start CoverageReport\index.html

echo.
echo Done!
pause