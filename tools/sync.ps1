# Заново копирует общие файлы из kit.txt. Руками эти копии не правятся: меняется kit.txt, потом запускается эта команда.
#   powershell -ExecutionPolicy Bypass -File tools\sync.ps1 путь\к\kit.txt
param([Parameter(Mandatory = $true)][string]$Kit)
$ErrorActionPreference = 'Stop'
$src = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) 'src'
if (-not (Test-Path "$Kit\tokens\tokens.json")) { throw "Это не папка kit.txt: $Kit" }
$files = @(
    @('dist\Tokens.cs', 'Tokens.cs'),
    @('ui\Ui.cs', 'Ui.cs'),
    @('hub\HubLink.cs', 'HubLink.cs'),
    @('hub\Family.cs', 'Family.cs'),
    @('hub\CoreApp.cs', 'CoreApp.cs')
)
foreach ($f in $files) {
    Copy-Item (Join-Path $Kit $f[0]) (Join-Path $src $f[1]) -Force
    "$($f[0]) -> src\$($f[1])"
}
"OK. Теперь соберите и проверьте: build.ps1, затем --selftest и --demo --shot"
