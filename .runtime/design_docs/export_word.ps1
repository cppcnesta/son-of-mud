$ErrorActionPreference = 'Stop'
$taskRoot = 'D:\GAMEOFF\mud-child-map-practice'
$wordApp = $null
try {
    $wordApp = New-Object -ComObject Word.Application
    $wordApp.Visible = $false
    $wordApp.DisplayAlerts = 0
    $wordApp.AutomationSecurity = 3
    foreach ($name in @('MudChild_Level_Combat_Design_V1_0','MudChild_Master_Design_V1_0')) {
        $source = Join-Path $taskRoot ('Documentation\Design\' + $name + '.docx')
        $target = Join-Path $taskRoot ('.runtime\design_docs\' + $name + '.pdf')
        $doc = $wordApp.Documents.Open($source,$false,$true)
        try {
            $doc.Repaginate()
            $doc.Fields.Update() | Out-Null
            $doc.ExportAsFixedFormat($target,17)
            Write-Output ($name + ': pages=' + $doc.ComputeStatistics(2))
        } finally { $doc.Close(0) }
    }
} finally {
    if ($null -ne $wordApp) { $wordApp.Quit(); [void][Runtime.InteropServices.Marshal]::ReleaseComObject($wordApp) }
}
