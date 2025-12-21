# Clean table and test
$entities = @(
    "1tVUus8OgEyjLQSgO4v-YJXXERG_80w4sYLN11WzzSM",
    "4PW-DokHGTP_e0jvgstySNFCWhunXS6l0SzJhe6iBKo",
    "5MYTjslYlpULc7q3fqQ-WO9dwVGDmPZc73iFAyfOeUU",
    "8Ho7c7xRJxebHb8pqQaOFutI-x9wKnFwIV0JG1aOraA",
    "9RbhAESrxi8NPWgVHYsgES1_0l3nAZU760bGFtNNeU0",
    "IL2_kahnuqRspqAylww5grbknu61noeepsS-0utxORE",
    "IjeFShfd8RGrBLrIE0Qv7nS-DFSqaBwIfluZleRnWVc",
    "rHLOKiYcOBXt22X-_nxofk8BVb4z4ft4Lywazw4OSIc",
    "vL10pxOtGiCq6Q7tWrnvLZd-rPKxV7Qip12Pc32LGyM",
    "wGJ_QALZ8vgSsbjihZ_104wsiT-D_Ysm9vkwjkoIr_k"
)

Write-Host "Deleting 10 entities from ProcessedPosts table..." -ForegroundColor Yellow

foreach ($rowKey in $entities) {
    az storage entity delete --account-name stdevsecblogauto --table-name ProcessedPosts --partition-key "SecurityBlog-202512" --row-key $rowKey 2>&1 | Out-Null
    Write-Host "✓ Deleted: $rowKey" -ForegroundColor Green
}

Write-Host "`nTable cleaned! Triggering workflow..." -ForegroundColor Cyan

az rest --method post --uri "https://management.azure.com/subscriptions/$(az account show --query id -o tsv)/resourceGroups/rg-security-blog-automation-dev/providers/Microsoft.Logic/workflows/logic-dev-security-blog-automation/triggers/Recurrence/run?api-version=2016-06-01"

Write-Host "`nWaiting 30 seconds for workflow to complete..." -ForegroundColor Cyan
Start-Sleep -Seconds 30

Write-Host "`nChecking workflow status..." -ForegroundColor Cyan
$result = az rest --method get --uri "https://management.azure.com/subscriptions/$(az account show --query id -o tsv)/resourceGroups/rg-security-blog-automation-dev/providers/Microsoft.Logic/workflows/logic-dev-security-blog-automation/runs?api-version=2016-06-01" | ConvertFrom-Json

$latestRun = $result.value[0]
Write-Host "`nLatest Run:" -ForegroundColor Yellow
Write-Host "  Name: $($latestRun.name)" -ForegroundColor White
Write-Host "  Status: $($latestRun.properties.status)" -ForegroundColor $(if ($latestRun.properties.status -eq 'Succeeded') { 'Green' } else { 'Red' })
Write-Host "  Start: $($latestRun.properties.startTime)" -ForegroundColor White
Write-Host "  End: $($latestRun.properties.endTime)" -ForegroundColor White

if ($latestRun.properties.status -eq 'Succeeded') {
    Write-Host "`n✅ Workflow succeeded! Check your email for the new blue header!" -ForegroundColor Green
} else {
    Write-Host "`n❌ Workflow failed or still running. Check Azure Portal for details." -ForegroundColor Red
}
