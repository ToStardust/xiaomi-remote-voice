param([Parameter(Mandatory=$true)][string]$InstallRoot)
$ErrorActionPreference='Stop'
$name='XiaomiRemoteVoiceBroker'
$service=Get-CimInstance Win32_Service -Filter "Name='$name'" -ErrorAction SilentlyContinue
if (-not $service) { exit 0 }
$expected=[IO.Path]::GetFullPath((Join-Path $InstallRoot 'resources\remote-broker\XiaomiRemoteVoiceBroker.exe'))
$actual=[Environment]::ExpandEnvironmentVariables($service.PathName).Trim().Trim('"')
if (-not [String]::Equals($actual,$expected,[StringComparison]::OrdinalIgnoreCase)) { Write-Error 'Service belongs to another installation; leaving it unchanged.'; exit 1 }
if ($service.State -ne 'Stopped') { Stop-Service -Name $name -Force; (Get-Service -Name $name).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(15)) }
& "$env:SystemRoot\System32\sc.exe" delete $name | Out-Null
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
