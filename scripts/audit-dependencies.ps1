$ErrorActionPreference='Stop'
$report = dotnet list FutsalPro.slnx package --vulnerable --include-transitive --format json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Dependency audit could not complete' }
$bad = @($report.projects | ForEach-Object { $_.frameworks } | ForEach-Object { @($_.topLevelPackages) + @($_.transitivePackages) } | Where-Object { $_ -and $_.vulnerabilities })
if ($bad.Count -gt 0) { $bad | Select-Object id,resolvedVersion | Format-Table; throw 'Known vulnerable dependencies found' }
Write-Output 'No known vulnerable packages.'
