# cleanup_mcp_server.ps1
$pidFile = Join-Path $env:TEMP "mcp_unity_bridge_server.pid"

if (Test-Path $pidFile) {
    try {
        $pidInfo = Get-Content $pidFile
        $pid = [int]($pidInfo[0])
        $port = [int]($pidInfo[1])
        
        Write-Host "Found MCP Bridge server PID file. PID: $pid, Port: $port"
        
        $process = Get-Process -Id $pid -ErrorAction SilentlyContinue
        if ($process) {
            Write-Host "Killing process $pid..."
            $process | Stop-Process -Force
            Write-Host "Process killed."
        }
        else {
            Write-Host "Process $pid not found (already terminated)."
        }
        
        Remove-Item $pidFile -Force
        Write-Host "PID file removed."
    }
    catch {
        Write-Host "Error during cleanup: $_"
    }
}
else {
    Write-Host "No MCP Bridge server PID file found."
}