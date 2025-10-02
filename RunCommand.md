RunSystemUtilizationTests.bat --source "%TEMP%\claude\hdmx_test_data" --local-results

Step 2: Generate the HTML Report
dotnet test SystemUtilizationMonitor.Tests\SystemUtilizationMonitor.Tests.csproj --settings coverlet.runsettings --collect:"XPlat Code Coverage" --results-directory "./TestResults"

Step 3: Open the Report
reportgenerator -reports:"./TestResults/*/coverage.cobertura.xml" -targetdir:"./CoverageReport" -reporttypes:Html
start CoverageReport\index.html