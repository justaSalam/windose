param(
    [int]$Memory = 4096,
    [switch]$Windowed
)

$qemuRoot = Join-Path $env:LOCALAPPDATA "Cosmos\Tools\qemu"
$qemu = Join-Path $qemuRoot "bin\qemu-system-x86_64.exe"
$firmware = Join-Path $qemuRoot "share\qemu"
$iso = Join-Path $PSScriptRoot "output-x64\Windose.iso"

if (-not (Test-Path -LiteralPath $qemu)) {
    Write-Error "Cosmos QEMU was not found at $qemu"
    exit 1
}

if (-not (Test-Path -LiteralPath $iso)) {
    Write-Error "Windose.iso was not found. Build the x64 project first."
    exit 1
}

$qemuArgs = @(
    "-L", $firmware,
    "-M", "q35",
    "-cpu", "max",
    "-m", "${Memory}M",
    "-cdrom", $iso,
    "-boot", "d",
    "-no-reboot",
    "-no-shutdown",
    "-vga", "std",
    "-serial", "stdio",
    "-display", "sdl"
)

if (-not $Windowed) {
    $qemuArgs += "-full-screen"
}

& $qemu @qemuArgs
exit $LASTEXITCODE
