# Simplified version with parallel execution

$outputPath = "C:\Users\Owner\source"
$basePath = "C:\Users\Owner\source\repos\toolkit.revit.transaction-cache"

$projects = @(
    "src\Toolkit.Revit.TransactionCache"
)

$configurations = @(
    "Release"
)

foreach ($project in $projects) {
    $projectPath = Join-Path $basePath $project
    Set-Location $projectPath
    
    foreach ($config in $configurations) {
        Write-Host "Packing $project with $config" -ForegroundColor Green
        dotnet pack --configuration "$config" --output $outputPath
        
        if ($LASTEXITCODE -ne 0) {
            Write-Host "Failed to pack $project with $config" -ForegroundColor Red
        }
    }
}

Write-Host "All packs completed!" -ForegroundColor Cyan
