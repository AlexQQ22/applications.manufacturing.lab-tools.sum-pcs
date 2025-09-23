# RunSystemUtilizationTests.ps1
# Standalone PowerShell script to run SystemUtilizationMonitor validation tests
# Can be used with any CI/CD system or run manually

param(
    [string]$SourcePath = "\\amr.corp.intel.com\ec\proj\mdl\cr\intel\hdmx_db\mae\SUM\HDMx",
    [string]$ResultsPath = "\\amr.corp.intel.com\ec\proj\mdl\cr\intel\hdmx_db\mae\SUM\Results",
    [int]$MaxCsvFiles = 25,
    [string]$BuildConfiguration = "Release",
    [string]$ProjectPath = "SystemUtilizationMonitor.Tests/SystemUtilizationMonitor.Tests.csproj",
    [string]$GitCommit = "",
    [string]$GitBranch = "",
    [string]$BuildNumber = "",
    [switch]$CleanupOnExit,
    [switch]$Verbose
)

# Function to write colored output
function Write-ColorOutput {
    param(
        [string]$Message,
        [string]$Color = "White"
    )
    
    if ($Verbose) {
        Write-Host $Message -ForegroundColor $Color
    } else {
        Write-Host $Message
    }
}

# Function to create timestamped directory
function New-TimestampedDirectory {
    param([string]$BasePath)
    
    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $fullPath = Join-Path $BasePath "TestRun_$timestamp"
    
    try {
        New-Item -ItemType Directory -Path $fullPath -Force | Out-Null
        Write-ColorOutput "✓ Created results directory: $fullPath" "Green"
        return $fullPath
    } catch {
        Write-ColorOutput "✗ Failed to create directory: $($_.Exception.Message)" "Red"
        throw
    }
}

# Function to discover CSV files
function Get-CsvFilesForTesting {
    param(
        [string]$SourcePath,
        [int]$MaxFiles
    )
    
    Write-ColorOutput "Discovering CSV files in: $SourcePath" "Cyan"
    
    try {
        if (-not (Test-Path $SourcePath)) {
            Write-ColorOutput "⚠ Source path not accessible: $SourcePath" "Yellow"
            return @()
        }
        
        $csvFiles = Get-ChildItem -Path $SourcePath -Filter "hdmx*.csv" -ErrorAction Stop |
                   Sort-Object LastWriteTime -Descending |
                   Select-Object -First $MaxFiles
        
        Write-ColorOutput "Found $($csvFiles.Count) CSV files for testing:" "Green"
        $csvFiles | ForEach-Object { Write-ColorOutput "  - $($_.Name)" "Gray" }
        
        return $csvFiles
    } catch {
        Write-ColorOutput "✗ Error discovering CSV files: $($_.Exception.Message)" "Red"
        return @()
    }
}

# Function to run individual CSV test
function Invoke-CsvValidationTest {
    param(
        [string]$CsvFilePath,
        [string]$ProjectPath,
        [string]$Configuration,
        [string]$ResultsDirectory
    )
    
    $fileName = Split-Path $CsvFilePath -Leaf
    $testName = $fileName -replace '\.csv$', ''
    
    Write-ColorOutput "`n===== Testing: $fileName =====" "Cyan"
    
    try {
        $testArgs = @(
            "test", $ProjectPath,
            "--configuration", $Configuration,
            "--logger", "trx;LogFileName=Test_$testName.trx",
            "--results-directory", $ResultsDirectory,
            "--filter", "FullyQualifiedName~ValidateSystemUtilizationData_CsvVsJson_ShouldMatch",
            "--verbosity", "normal",
            "--",
            "TestRunParameters.Parameter(name=`"CsvFilePath`",value=`"$CsvFilePath`")"
        )
        
        $output = & dotnet $testArgs 2>&1
        $exitCode = $LASTEXITCODE
        
        $result = @{
            File = $fileName
            FilePath = $CsvFilePath
            Status = if ($exitCode -eq 0) { "Passed" } else { "Failed" }
            ExitCode = $exitCode
            Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
            Output = $output -join "`n"
            Duration = $null
        }
        
        if ($result.Status -eq "Passed") {
            Write-ColorOutput "✓ PASSED: $fileName" "Green"
        } else {
            Write-ColorOutput "✗ FAILED: $fileName (Exit Code: $exitCode)" "Red"
            if ($Verbose) {
                Write-ColorOutput "Output: $($result.Output)" "Gray"
            }
        }
        
        return $result
    } catch {
        Write-ColorOutput "⚠ ERROR testing $fileName`: $($_.Exception.Message)" "Yellow"
        return @{
            File = $fileName
            FilePath = $CsvFilePath
            Status = "Error"
            Error = $_.Exception.Message
            Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
        }
    }
}

# Function to run comprehensive tests
function Invoke-ComprehensiveTests {
    param(
        [string]$ProjectPath,
        [string]$Configuration,
        [string]$ResultsDirectory
    )
    
    Write-ColorOutput "`n===== Running Comprehensive Test Suite =====" "Cyan"
    
    try {
        $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
        
        $testArgs = @(
            "test", $ProjectPath,
            "--configuration", $Configuration,
            "--logger", "trx;LogFileName=ComprehensiveTests_$timestamp.trx",
            "--logger", "html;LogFileName=TestResults_$timestamp.html",
            "--results-directory", $ResultsDirectory,
            "--collect:XPlat Code Coverage",
            "--verbosity", "normal"
        )
        
        $output = & dotnet $testArgs 2>&1
        $exitCode = $LASTEXITCODE
        
        if ($exitCode -eq 0) {
            Write-ColorOutput "✓ Comprehensive tests completed successfully" "Green"
        } else {
            Write-ColorOutput "⚠ Some comprehensive tests may have failed (Exit Code: $exitCode)" "Yellow"
        }
        
        return @{
            Status = if ($exitCode -eq 0) { "Passed" } else { "Failed" }
            ExitCode = $exitCode
            Output = $output -join "`n"
        }
    } catch {
        Write-ColorOutput "✗ Error running comprehensive tests: $($_.Exception.Message)" "Red"
        return @{
            Status = "Error"
            Error = $_.Exception.Message
        }
    }
}

# Function to generate reports
function New-TestReports {
    param(
        [array]$TestResults,
        [hashtable]$ComprehensiveTestResult,
        [string]$ResultsDirectory,
        [hashtable]$BuildInfo
    )
    
    Write-ColorOutput "`n===== Generating Reports =====" "Cyan"
    
    # Calculate summary statistics
    $totalTests = $TestResults.Count
    $passedTests = ($TestResults | Where-Object { $_.Status -eq "Passed" }).Count
    $failedTests = ($TestResults | Where-Object { $_.Status -eq "Failed" }).Count
    $errorTests = ($TestResults | Where-Object { $_.Status -eq "Error" }).Count
    $successRate = if ($totalTests -gt 0) { [math]::Round($passedTests / $totalTests * 100, 2) } else { 0 }
    
    # Create summary object
    $summary = @{
        TotalTests = $totalTests
        PassedTests = $passedTests
        FailedTests = $failedTests
        ErrorTests = $errorTests
        SuccessRate = $successRate
        Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss UTC"
        BuildInfo = $BuildInfo
        ComprehensiveTestStatus = $ComprehensiveTestResult.Status
    }
    
    # Save detailed results
    $TestResults | ConvertTo-Json -Depth 3 | Out-File "$ResultsDirectory\DetailedTestResults.json" -Encoding UTF8
    $summary | ConvertTo-Json -Depth 3 | Out-File "$ResultsDirectory\TestSummary.json" -Encoding UTF8
    
    # Generate Markdown report
    $markdownReport = @"
# SystemUtilizationMonitor Test Report

**Generated:** $(Get-Date -Format "yyyy-MM-dd HH:mm:ss UTC")
**Build Number:** $($BuildInfo.BuildNumber)
**Git Commit:** $($BuildInfo.GitCommit)
**Git Branch:** $($BuildInfo.GitBranch)

## Test Summary
- **Total CSV Files Tested:** $totalTests
- **Passed Tests:** $passedTests
- **Failed Tests:** $failedTests
- **Error Tests:** $errorTests
- **Success Rate:** $successRate%
- **Comprehensive Tests:** $($ComprehensiveTestResult.Status)

## Environment
- **Build Configuration:** $($BuildInfo.Configuration)
- **PowerShell Version:** $($PSVersionTable.PSVersion)
- **Machine:** $($env:COMPUTERNAME)
- **User:** $($env:USERNAME)

## Detailed Test Results

| File | Status | Timestamp | Details |
|------|--------|-----------|---------|
"@
    
    foreach ($result in $TestResults) {
        $status = $result.Status
        $details = if ($result.Error) { $result.Error } elseif ($result.Status -eq "Failed" -and $result.Output) { "See logs for details" } else { "-" }
        $markdownReport += "`n| $($result.File) | $status | $($result.Timestamp) | $details |"
    }
    
    $markdownReport | Out-File "$ResultsDirectory\TestReport.md" -Encoding UTF8
    
    # Generate HTML report
    $htmlReport = @"
<!DOCTYPE html>
<html>
<head>
    <title>SystemUtilizationMonitor Test Report</title>
    <meta charset="UTF-8">
    <style>
        body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; margin: 20px; background-color: #f5f5f5; }
        .container { max-width: 1200px; margin: 0 auto; background-color: white; padding: 20px; border-radius: 8px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }
        .header { background-color: #0078d4; color: white; padding: 20px; border-radius: 5px; margin-bottom: 20px; }
        .summary { display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 15px; margin: 20px 0; }
        .summary-card { background-color: #f8f9fa; padding: 15px; border-radius: 5px; border-left: 4px solid #0078d4; }
        .passed { color: #28a745; font-weight: bold; }
        .failed { color: #dc3545; font-weight: bold; }
        .error { color: #fd7e14; font-weight: bold; }
        table { width: 100%; border-collapse: collapse; margin: 20px 0; }
        th, td { border: 1px solid #dee2e6; padding: 12px; text-align: left; }
        th { background-color: #e9ecef; font-weight: 600; }
        tr:nth-child(even) { background-color: #f8f9fa; }
        .test-output { font-family: 'Consolas', 'Monaco', monospace; font-size: 12px; background-color: #f5f5f5; padding: 10px; border-radius: 3px; max-height: 200px; overflow-y: auto; }
        .success-rate { font-size: 24px; font-weight: bold; color: $(if ($successRate -ge 80) { '#28a745' } elseif ($successRate -ge 60) { '#ffc107' } else { '#dc3545' }); }
    </style>
</head>
<body>
    <div class="container">
        <div class="header">
            <h1>🧪 SystemUtilizationMonitor Test Report</h1>
            <p><strong>Generated:</strong> $(Get-Date -Format "yyyy-MM-dd HH:mm:ss UTC")</p>
            <p><strong>Build:</strong> $($BuildInfo.BuildNumber) | <strong>Commit:</strong> $($BuildInfo.GitCommit) | <strong>Branch:</strong> $($BuildInfo.GitBranch)</p>
        </div>
        
        <div class="summary">
            <div class="summary-card">
                <h3>📊 Success Rate</h3>
                <div class="success-rate">$successRate%</div>
            </div>
            <div class="summary-card">
                <h3>📈 Total Tests</h3>
                <div style="font-size: 24px; font-weight: bold;">$totalTests</div>
            </div>
            <div class="summary-card">
                <h3>✅ Passed</h3>
                <div class="passed" style="font-size: 24px;">$passedTests</div>
            </div>
            <div class="summary-card">
                <h3>❌ Failed</h3>
                <div class="failed" style="font-size: 24px;">$failedTests</div>
            </div>
            <div class="summary-card">
                <h3>⚠️ Errors</h3>
                <div class="error" style="font-size: 24px;">$errorTests</div>
            </div>
            <div class="summary-card">
                <h3>🔧 Comprehensive Tests</h3>
                <div class="$(if ($ComprehensiveTestResult.Status -eq 'Passed') { 'passed' } else { 'failed' })" style="font-size: 18px;">$($ComprehensiveTestResult.Status)</div>
            </div>
        </div>
        
        <h2>📋 Detailed Test Results</h2>
        <table>
            <thead>
                <tr>
                    <th>📄 File</th>
                    <th>🎯 Status</th>
                    <th>⏰ Timestamp</th>
                    <th>📝 Details</th>
                </tr>
            </thead>
            <tbody>
"@

    foreach ($result in $TestResults) {
        $statusClass = $result.Status.ToLower()
        $statusIcon = switch ($result.Status) {
            "Passed" { "✅" }
            "Failed" { "❌" }
            "Error" { "⚠️" }
            default { "❓" }
        }
        
        $details = if ($result.Error) { 
            "<div class=`"test-output`">Error: $([System.Web.HttpUtility]::HtmlEncode($result.Error))</div>" 
        } elseif ($result.Status -eq "Failed" -and $result.Output) { 
            "<div class=`"test-output`">$([System.Web.HttpUtility]::HtmlEncode($result.Output.Substring(0, [Math]::Min(500, $result.Output.Length))))</div>" 
        } else { 
            "-" 
        }
        
        $htmlReport += @"
                <tr>
                    <td>$($result.File)</td>
                    <td class="$statusClass">$statusIcon $($result.Status)</td>
                    <td>$($result.Timestamp)</td>
                    <td>$details</td>
                </tr>
"@
    }
    
    $htmlReport += @"
            </tbody>
        </table>
        
        <h2>🔧 Environment Information</h2>
        <table>
            <tr><td><strong>Build Configuration</strong></td><td>$($BuildInfo.Configuration)</td></tr>
            <tr><td><strong>PowerShell Version</strong></td><td>$($PSVersionTable.PSVersion)</td></tr>
            <tr><td><strong>Machine</strong></td><td>$($env:COMPUTERNAME)</td></tr>
            <tr><td><strong>User</strong></td><td>$($env:USERNAME)</td></tr>
            <tr><td><strong>Results Directory</strong></td><td>$ResultsDirectory</td></tr>
        </table>
        
        <div style="margin-top: 30px; padding: 15px; background-color: #e9ecef; border-radius: 5px; text-align: center;">
            <small>Report generated by SystemUtilizationMonitor Test Suite</small>
        </div>
    </div>
</body>
</html>
"@
    
    $htmlReport | Out-File "$ResultsDirectory\TestReport.html" -Encoding UTF8
    
    Write-ColorOutput "✓ Reports generated:" "Green"
    Write-ColorOutput "  - Markdown: $ResultsDirectory\TestReport.md" "Gray"
    Write-ColorOutput "  - HTML: $ResultsDirectory\TestReport.html" "Gray"
    Write-ColorOutput "  - JSON Summary: $ResultsDirectory\TestSummary.json" "Gray"
    Write-ColorOutput "  - Detailed Results: $ResultsDirectory\DetailedTestResults.json" "Gray"
    
    return $summary
}

# Main execution function
function Start-SystemUtilizationTests {
    Write-ColorOutput "🚀 Starting SystemUtilizationMonitor Validation Tests" "Green"
    Write-ColorOutput "=================================================" "Green"
    
    $startTime = Get-Date
    $buildInfo = @{
        BuildNumber = if ($BuildNumber) { $BuildNumber } else { "Local-$(Get-Date -Format 'yyyyMMdd-HHmmss')" }
        GitCommit = if ($GitCommit) { $GitCommit } else { try { git rev-parse HEAD 2>$null } catch { "Unknown" } }
        GitBranch = if ($GitBranch) { $GitBranch } else { try { git branch --show-current 2>$null } catch { "Unknown" } }
        Configuration = $BuildConfiguration
        StartTime = $startTime
        Machine = $env:COMPUTERNAME
        User = $env:USERNAME
    }
    
    Write-ColorOutput "Build Info:" "Cyan"
    Write-ColorOutput "  Build Number: $($buildInfo.BuildNumber)" "Gray"
    Write-ColorOutput "  Git Commit: $($buildInfo.GitCommit)" "Gray"
    Write-ColorOutput "  Git Branch: $($buildInfo.GitBranch)" "Gray"
    Write-ColorOutput "  Configuration: $($buildInfo.Configuration)" "Gray"
    
    try {
        # Step 1: Setup environment
        Write-ColorOutput "`n📁 Setting up environment..." "Cyan"
        
        # Check .NET installation
        try {
            $dotnetVersion = & dotnet --version 2>$null
            Write-ColorOutput "✓ .NET SDK found: $dotnetVersion" "Green"
        } catch {
            Write-ColorOutput "✗ .NET SDK not found. Please install .NET 6.0 or later." "Red"
            return 1
        }
        
        # Check project file
        if (-not (Test-Path $ProjectPath)) {
            Write-ColorOutput "✗ Project file not found: $ProjectPath" "Red"
            return 1
        }
        Write-ColorOutput "✓ Project file found: $ProjectPath" "Green"
        
        # Create results directory
        $resultsDirectory = New-TimestampedDirectory -BasePath $ResultsPath
        
        # Step 2: Build the solution
        Write-ColorOutput "`n🔨 Building solution..." "Cyan"
        try {
            $buildOutput = & dotnet build $ProjectPath --configuration $BuildConfiguration --verbosity minimal 2>&1
            if ($LASTEXITCODE -ne 0) {
                Write-ColorOutput "✗ Build failed:" "Red"
                Write-ColorOutput $buildOutput "Gray"
                return 1
            }
            Write-ColorOutput "✓ Build completed successfully" "Green"
        } catch {
            Write-ColorOutput "✗ Build error: $($_.Exception.Message)" "Red"
            return 1
        }
        
        # Step 3: Discover CSV files
        Write-ColorOutput "`n🔍 Discovering CSV files..." "Cyan"
        $csvFiles = Get-CsvFilesForTesting -SourcePath $SourcePath -MaxFiles $MaxCsvFiles
        
        if ($csvFiles.Count -eq 0) {
            Write-ColorOutput "⚠ No CSV files found for testing. Continuing with comprehensive tests only." "Yellow"
        }
        
        # Step 4: Run CSV validation tests
        $testResults = @()
        if ($csvFiles.Count -gt 0) {
            Write-ColorOutput "`n🧪 Running CSV validation tests..." "Cyan"
            
            $progress = 0
            foreach ($csvFile in $csvFiles) {
                $progress++
                Write-Progress -Activity "Running CSV Tests" -Status "Testing $($csvFile.Name)" -PercentComplete (($progress / $csvFiles.Count) * 100)
                
                $result = Invoke-CsvValidationTest -CsvFilePath $csvFile.FullName -ProjectPath $ProjectPath -Configuration $BuildConfiguration -ResultsDirectory $resultsDirectory
                $testResults += $result
                
                # Small delay to prevent overwhelming the system
                Start-Sleep -Milliseconds 100
            }
            Write-Progress -Activity "Running CSV Tests" -Completed
        }
        
        # Step 5: Run comprehensive tests
        Write-ColorOutput "`n🔧 Running comprehensive test suite..." "Cyan"
        $comprehensiveResult = Invoke-ComprehensiveTests -ProjectPath $ProjectPath -Configuration $BuildConfiguration -ResultsDirectory $resultsDirectory
        
        # Step 6: Generate reports
        $summary = New-TestReports -TestResults $testResults -ComprehensiveTestResult $comprehensiveResult -ResultsDirectory $resultsDirectory -BuildInfo $buildInfo
        
        # Step 7: Display summary
        $endTime = Get-Date
        $duration = $endTime - $startTime
        
        Write-ColorOutput "`n📊 TEST EXECUTION SUMMARY" "Green"
        Write-ColorOutput "=========================" "Green"
        Write-ColorOutput "Total CSV Files Tested: $($summary.TotalTests)" "White"
        Write-ColorOutput "Passed Tests: $($summary.PassedTests)" "Green"
        Write-ColorOutput "Failed Tests: $($summary.FailedTests)" "Red"
        Write-ColorOutput "Error Tests: $($summary.ErrorTests)" "Yellow"
        Write-ColorOutput "Success Rate: $($summary.SuccessRate)%" "Cyan"
        Write-ColorOutput "Comprehensive Tests: $($comprehensiveResult.Status)" "White"
        Write-ColorOutput "Duration: $($duration.ToString('mm\:ss'))" "Gray"
        Write-ColorOutput "Results Location: $resultsDirectory" "Gray"
        
        # Determine exit code
        if ($summary.FailedTests -eq 0 -and $summary.ErrorTests -eq 0 -and $comprehensiveResult.Status -eq "Passed") {
            Write-ColorOutput "`n✅ All tests passed!" "Green"
            return 0
        } elseif ($summary.PassedTests -gt 0) {
            Write-ColorOutput "`n⚠ Some tests failed, but some passed" "Yellow"
            return 1
        } else {
            Write-ColorOutput "`n❌ All tests failed or had errors" "Red"
            return 2
        }
        
    } catch {
        Write-ColorOutput "`n💥 Fatal error during test execution:" "Red"
        Write-ColorOutput $_.Exception.Message "Red"
        Write-ColorOutput $_.ScriptStackTrace "Gray"
        return 3
    } finally {
        if ($CleanupOnExit) {
            Write-ColorOutput "`n🧹 Performing cleanup..." "Cyan"
            # Add any cleanup logic here if needed
        }
    }
}

# Script entry point
if ($MyInvocation.InvocationName -ne '.') {
    # Script is being executed directly
    Write-Host "SystemUtilizationMonitor Test Runner" -ForegroundColor Green
    Write-Host "=====================================" -ForegroundColor Green
    
    # Validate parameters
    if (-not $SourcePath -or -not $ResultsPath) {
        Write-Host "Error: SourcePath and ResultsPath are required parameters" -ForegroundColor Red
        exit 1
    }
    
    # Execute the tests
    $exitCode = Start-SystemUtilizationTests
    
    # Exit with appropriate code
    exit $exitCode
}