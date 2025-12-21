#Requires -Version 5.1
# PDK Environment Parity Check (REQ-09-022)
# Verifies local environment matches CI requirements

$ErrorActionPreference = "Continue"
$script:ExitCode = 0

Write-Host "Environment Parity Check"
Write-Host "========================"
Write-Host ""

function Write-OK {
    param([string]$Message)
    Write-Host "[OK]" -ForegroundColor Green -NoNewline
    Write-Host " $Message"
}

function Write-Fail {
    param([string]$Message)
    Write-Host "[FAIL]" -ForegroundColor Red -NoNewline
    Write-Host " $Message"
    $script:ExitCode = 2
}

function Write-Warn {
    param([string]$Message)
    Write-Host "[WARN]" -ForegroundColor Yellow -NoNewline
    Write-Host " $Message"
}

# Check .NET SDK
Write-Host "Checking .NET SDK..."
try {
    $dotnetVersion = & dotnet --version 2>$null
    if ($dotnetVersion -match "^8\.") {
        Write-OK ".NET SDK:     $dotnetVersion (required: 8.0.x)"
    } elseif ($dotnetVersion -match "^9\.") {
        # .NET 9.x is backwards compatible with 8.x projects
        Write-Warn ".NET SDK:     $dotnetVersion (CI uses 8.0.x, but 9.x is compatible)"
    } else {
        Write-Fail ".NET SDK:     $dotnetVersion (required: 8.0.x or higher)"
    }
} catch {
    Write-Fail ".NET SDK:     Not installed (required: 8.0.x)"
}

# Check Docker
Write-Host "Checking Docker..."
try {
    $dockerVersionOutput = & docker --version 2>$null
    if ($dockerVersionOutput -match "(\d+\.\d+\.\d+)") {
        $dockerVersion = $Matches[1]
        # Check if Docker daemon is running
        $dockerInfo = & docker info 2>$null
        if ($LASTEXITCODE -eq 0) {
            Write-OK "Docker:       $dockerVersion (running)"
        } else {
            Write-Fail "Docker:       $dockerVersion (not running - start Docker daemon)"
        }
    } else {
        Write-Fail "Docker:       Unable to get version"
    }
} catch {
    Write-Fail "Docker:       Not installed (required for PDK execution)"
}

# Check Git
Write-Host "Checking Git..."
try {
    $gitVersionOutput = & git --version 2>$null
    if ($gitVersionOutput -match "(\d+\.\d+\.\d+)") {
        $gitVersion = $Matches[1]
        Write-OK "Git:          $gitVersion"
    } else {
        Write-OK "Git:          Installed"
    }
} catch {
    Write-Fail "Git:          Not installed"
}

# Check GitHub CLI (optional)
Write-Host "Checking GitHub CLI..."
try {
    $ghVersionOutput = & gh --version 2>$null
    if ($ghVersionOutput -and $ghVersionOutput[0] -match "(\d+\.\d+\.\d+)") {
        $ghVersion = $Matches[1]
        # Check if authenticated
        $authStatus = & gh auth status 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-OK "GitHub CLI:   $ghVersion (authenticated)"
        } else {
            Write-Warn "GitHub CLI:   $ghVersion (not authenticated - run 'gh auth login' for CI comparison)"
        }
    } else {
        Write-Warn "GitHub CLI:   Not installed (optional - needed for CI comparison)"
    }
} catch {
    Write-Warn "GitHub CLI:   Not installed (optional - needed for CI comparison)"
}

# Check project dependencies
Write-Host "Checking dependencies..."
if ((Test-Path "PDK.sln") -or (Test-Path "src/PDK.CLI/PDK.CLI.csproj")) {
    try {
        $null = & dotnet restore --verbosity quiet 2>$null
        if ($LASTEXITCODE -eq 0) {
            Write-OK "Dependencies: Restored successfully"
        } else {
            Write-Fail "Dependencies: Failed to restore"
        }
    } catch {
        Write-Fail "Dependencies: Failed to restore"
    }
} else {
    Write-Warn "Dependencies: Not in PDK project directory"
}

# Check workflow file
Write-Host "Checking workflow file..."
if (Test-Path ".github/workflows/ci.yml") {
    Write-OK "CI Workflow:  .github/workflows/ci.yml exists"
} else {
    Write-Fail "CI Workflow:  .github/workflows/ci.yml not found"
}

# Check coverlet settings
Write-Host "Checking coverage config..."
if (Test-Path "coverlet.runsettings") {
    Write-OK "Coverage:     coverlet.runsettings exists"
} else {
    Write-Warn "Coverage:     coverlet.runsettings not found"
}

Write-Host ""
Write-Host "========================"

if ($script:ExitCode -eq 0) {
    Write-Host "Environment check passed" -ForegroundColor Green
} else {
    Write-Host "Environment check failed" -ForegroundColor Red
    Write-Host "Please install missing required components before running self-test."
}

exit $script:ExitCode
