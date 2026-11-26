<#
    Checks that the advanced screens' light and dark colours stay in step.

    SvoRayAdaptivePalette.cs gives every colour as one property with both sides:
        public Color Name => IsDark ? <dark> : <light>;
    The XAML binds brushes to those properties by name, and a binding to a name that does not
    exist fails silently at runtime (the brush stays transparent). This script fails when:
      - a Color property of the palette does not have both a dark and a light value;
      - a brush in SvoRayTheme.xaml or SvoRayAdvanced.xaml binds to a palette property that does
        not exist;
      - a view or dictionary uses a SvoRay.Brush.* key that SvoRayTheme.xaml does not define.

    Run from the repository root:  powershell -File tools/Check-SvoRayAdaptivePalette.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$wpf = Join-Path $root 'src/v2rayN'
$palettePath = Join-Path $wpf 'Common/SvoRayAdaptivePalette.cs'
$themePath = Join-Path $wpf 'Resources/SvoRayTheme.xaml'
$bridgePath = Join-Path $wpf 'Resources/SvoRayAdvanced.xaml'
$failures = New-Object System.Collections.Generic.List[string]

# 1. Every colour has a dark and a light side.
$palette = Get-Content -Raw -Encoding UTF8 $palettePath
$colorProps = [regex]::Matches($palette, 'public\s+Color\s+(\w+)\s*(=>|\{)[^;]*;')
$names = New-Object System.Collections.Generic.HashSet[string]
foreach ($m in $colorProps) {
    $name = $m.Groups[1].Value
    [void]$names.Add($name)
    if ($m.Value -notmatch '=>\s*IsDark\s*\?\s*[^:;]+:\s*[^;]+;') {
        $failures.Add("Palette colour '$name' does not give both a dark and a light value.")
    }
}
if ($names.Count -eq 0) {
    $failures.Add('No Color properties found in SvoRayAdaptivePalette.cs.')
}

# 2. Every brush binding names a palette colour.
$bindingPattern = 'Binding\s+(\w+),\s*Source=\{x:Static\s+common:SvoRayAdaptivePalette\.Current\}'
$bound = New-Object System.Collections.Generic.HashSet[string]
foreach ($path in @($themePath, $bridgePath)) {
    $text = Get-Content -Raw -Encoding UTF8 $path
    foreach ($m in [regex]::Matches($text, $bindingPattern)) {
        $name = $m.Groups[1].Value
        [void]$bound.Add($name)
        if (-not $names.Contains($name)) {
            $failures.Add("$(Split-Path -Leaf $path) binds to '$name', which SvoRayAdaptivePalette does not have.")
        }
    }
}

# 3. Every SvoRay.Brush.* key in use is defined in SvoRayTheme.xaml.
$theme = Get-Content -Raw -Encoding UTF8 $themePath
$defined = New-Object System.Collections.Generic.HashSet[string]
foreach ($m in [regex]::Matches($theme, 'x:Key="(SvoRay\.Brush\.\w+)"')) {
    [void]$defined.Add($m.Groups[1].Value)
}
$xamlFiles = Get-ChildItem -Recurse -Filter *.xaml -Path (Join-Path $wpf 'Views'), (Join-Path $wpf 'Resources')
foreach ($file in $xamlFiles) {
    $text = Get-Content -Raw -Encoding UTF8 $file.FullName
    foreach ($m in [regex]::Matches($text, '(?:Dynamic|Static)Resource\s+(SvoRay\.Brush\.\w+)')) {
        if (-not $defined.Contains($m.Groups[1].Value)) {
            $failures.Add("$($file.Name) uses $($m.Groups[1].Value), which SvoRayTheme.xaml does not define.")
        }
    }
}

$unused = @($names | Where-Object { -not $bound.Contains($_) })
Write-Host "Palette colours: $($names.Count), bound in XAML: $($bound.Count), SvoRay.Brush keys: $($defined.Count)"
if ($unused.Count -gt 0) {
    Write-Host "Not bound anywhere (allowed): $($unused -join ', ')"
}
if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Host "FAIL: $_" }
    exit 1
}
Write-Host 'OK: every colour has both sides and every binding and key resolves.'
