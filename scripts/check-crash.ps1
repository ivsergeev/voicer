
# Check Windows Event Viewer for recent application crashes
$events = Get-WinEvent -FilterHashtable @{LogName='Application'; Id=1000} -MaxEvents 10 -ErrorAction SilentlyContinue

if ($events) {
    foreach ($ev in $events) {
        $msg = $ev.Message
        if ($msg -match 'Voicer' -or $msg -match 'onnx' -or $msg -match 'skia' -or $msg -match 'dotnet' -or $msg -match 'coreclr') {
            Write-Host "=== CRASH at $($ev.TimeCreated) ==="
            Write-Host $msg
            Write-Host ""
        }
    }
} else {
    Write-Host "No Application Error events found."
}

# Also check for .NET Runtime errors (Event ID 1026)
$dotnetEvents = Get-WinEvent -FilterHashtable @{LogName='Application'; Id=1026} -MaxEvents 10 -ErrorAction SilentlyContinue
if ($dotnetEvents) {
    foreach ($ev in $dotnetEvents) {
        $msg = $ev.Message
        if ($msg -match 'Voicer' -or $msg -match 'onnx') {
            Write-Host "=== .NET RUNTIME ERROR at $($ev.TimeCreated) ==="
            Write-Host $msg
            Write-Host ""
        }
    }
}

# Windows Error Reporting (Event ID 1001)
$werEvents = Get-WinEvent -FilterHashtable @{LogName='Application'; Id=1001} -MaxEvents 10 -ErrorAction SilentlyContinue
if ($werEvents) {
    foreach ($ev in $werEvents) {
        $msg = $ev.Message
        if ($msg -match 'Voicer' -or $msg -match 'onnx') {
            Write-Host "=== WER REPORT at $($ev.TimeCreated) ==="
            Write-Host $msg
            Write-Host ""
        }
    }
}
