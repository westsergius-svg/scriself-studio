param(
    [string]$Op = 'observe',
    [string]$Session = 'probe',
    [double]$Width = 1440,
    [double]$Height = 768,
    [double]$X = 0,
    [double]$Y = 0,
    [double]$Delta = -3,
    [string]$Target,
    [string]$Text = '',
    [string]$Key = 'Enter',
    [string]$Modifiers = 'None',
    [string]$Button = 'Left',
    [int]$Count = 1,
    [int]$WaitMs = 700,
    [switch]$Cancel,
    [string]$FileName,
    [string]$Note
)

# This talks to the isolated Avalonia test process, not a live Windows window.
$qaRequest = @{
    op = $Op; session = $Session; width = $Width; height = $Height
    x = $X; y = $Y; delta = $Delta; text = $Text; key = $Key
    modifiers = $Modifiers; button = $Button; count = $Count
    waitMs = $WaitMs; cancel = [bool]$Cancel; note = $Note
}
if ($Target) { $qaRequest.target = $Target }
if ($FileName) { $qaRequest.fileName = $FileName }
Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:38461/' `
    -ContentType 'application/json; charset=utf-8' -Body ($qaRequest | ConvertTo-Json) |
    ConvertTo-Json -Depth 8
