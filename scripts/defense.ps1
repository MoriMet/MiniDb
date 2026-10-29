$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
function Run-Db { & dotnet @args; if ($LASTEXITCODE -ne 0) { throw "dotnet failed: $LASTEXITCODE" } }
Run-Db build -c Release
$dll = Join-Path (Get-Location) 'src/MiniDb/bin/Release/net8.0/MiniDb.dll'
$dir = Join-Path ([IO.Path]::GetTempPath()) ('minidb-defense-' + [Guid]::NewGuid())
New-Item -ItemType Directory $dir | Out-Null
$db = Join-Path $dir 'demo.db'
Run-Db $dll --db $db --generate 100000
$sql = Join-Path $dir 'queries.sql'
@"
EXPLAIN SELECT * FROM users WHERE age=54321;
SELECT * FROM users WHERE age=54321;
CREATE INDEX idx_age ON users(age);
EXPLAIN SELECT * FROM users WHERE age=54321;
SELECT * FROM users WHERE age=54321;
"@ | Set-Content -Encoding UTF8 $sql
Run-Db $dll --db $db --file $sql
Run-Db $dll --db $db --measure
$signal = Join-Path $dir 'ready'
$p = Start-Process dotnet -ArgumentList @("`"$dll`"", '--db', "`"$db`"", '--crash-worker', '--point', 'uncommitted', '--signal', "`"$signal`"") -PassThru
try {
    $deadline = (Get-Date).AddSeconds(30)
    while (!(Test-Path $signal)) { if ($p.HasExited -or (Get-Date) -gt $deadline) { throw 'Crash worker failed or timed out' }; Start-Sleep -Milliseconds 50 }
    Stop-Process -Id $p.Id -Force
    $p.WaitForExit()
} finally { if (!$p.HasExited) { Stop-Process -Id $p.Id -Force; $p.WaitForExit() } }
"SELECT * FROM users WHERE id=1; SELECT * FROM users WHERE id=100001; SELECT * FROM users WHERE age=99;" | Set-Content -Encoding UTF8 $sql
Run-Db $dll --db $db --file $sql
Write-Host "Неподтверждённые изменения пропали. Файлы демонстрации: $dir"
