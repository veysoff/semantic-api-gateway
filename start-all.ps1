param([switch]$NoWait,[switch]$Help)
if($Help){Write-Host "Semantic API Gateway - Start All Services";Write-Host "Usage: .\start-all.ps1 [-NoWait] [-Help]";exit 0}
$s="Cyan";$g="Green";$e="Red";$p=Split-Path -Parent $MyInvocation.MyCommand.Path
Write-Host "Starting All Services" -ForegroundColor $s
$d=dotnet --version 2>$null
if($LASTEXITCODE -ne 0){Write-Host "ERROR: .NET not found" -ForegroundColor $e;exit 1}
Write-Host "OK: $d" -ForegroundColor $g
$services=@(
  @{Name="Gateway";Path="SemanticApiGateway.Gateway";Port=5000},
  @{Name="Order Service";Path="SemanticApiGateway.MockServices/OrderService";Port=5100},
  @{Name="User Service";Path="SemanticApiGateway.MockServices/UserService";Port=5300},
  @{Name="Inventory Service";Path="SemanticApiGateway.MockServices/InventoryService";Port=5200}
)
$pwsh=(Get-Command pwsh -ErrorAction SilentlyContinue).Source
if(!$pwsh){$pwsh="powershell.exe"}
foreach($svc in $services){
  $svcPath=Join-Path $p $svc.Path
  if(!(Test-Path $svcPath)){Write-Host "ERROR: $($svc.Name) not found at $svcPath" -ForegroundColor $e;exit 1}
}
Write-Host "All service paths verified" -ForegroundColor $g
Write-Host ""
foreach($svc in $services){
  $svcPath=Join-Path $p $svc.Path
  Write-Host "Starting $($svc.Name) (port $($svc.Port))..." -ForegroundColor $s
  Start-Process -FilePath $pwsh -ArgumentList "-NoExit","-Command","cd '$svcPath'; dotnet run" -WindowStyle Normal
}
Write-Host "All services started in new windows" -ForegroundColor $g
if(!$NoWait){Write-Host "Waiting for Gateway on port 5000..." -ForegroundColor $s;for($i=0;$i -lt 30;$i++){$r=Invoke-WebRequest -Uri "http://localhost:5000/health" -ErrorAction SilentlyContinue;if($r.StatusCode -eq 200){Write-Host "Gateway ready!" -ForegroundColor $g;break}Start-Sleep -Seconds 1}}
Write-Host "" -ForegroundColor $g
Write-Host "Services running on:" -ForegroundColor $g
foreach($svc in $services){Write-Host "  • $($svc.Name): http://localhost:$($svc.Port)" -ForegroundColor $g}
Write-Host "  • Swagger: http://localhost:5000/swagger" -ForegroundColor $g
Write-Host "" -ForegroundColor $g
Write-Host "Done. Visit http://localhost:5000/swagger" -ForegroundColor $g
