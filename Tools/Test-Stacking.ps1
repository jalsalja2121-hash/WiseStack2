$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $PSScriptRoot '../Assets/Scripts/Data/StackingCalculator.cs')
$calculator = [ARLogistics.Data.StackingCalculator]
$auto = [ARLogistics.Data.StackOrientation]::Recommended
$cases = @(
    @('height limit', .4, .5, .3, 10, 1.2, 1, 1.95, 1000, 30),
    @('load limit', .4, .5, .3, 10, 1.2, 1, 6, 120, 12),
    @('zero load', .4, .5, .3, 10, 1.2, 1, 6, 0, 0),
    @('one full layer too heavy', .4, .5, .3, 10, 1.2, 1, 6, 59, 0),
    @('too tall', .4, .5, 2, 10, 1.2, 1, 1.95, 1000, 0),
    @('oversized footprint', 1.3, 1.3, .3, 10, 1.2, 1, 6, 1000, 0),
    @('invalid number', [float]::NaN, .5, .3, 10, 1.2, 1, 6, 1000, 0),
    @('negative load', .4, .5, .3, 10, 1.2, 1, 6, -1, 0),
    @('screenshot regression: high ceiling cannot produce 55 layers', .35, .48, .315, 3.008, 1.2, 1, 100, 1000, 30),
    @('low ceiling overrides preview height', .4, .5, .3, 10, 1.2, 1, 1.1, 1000, 12),
    @('box exceeds preview limit despite high ceiling', .4, .5, 1.7, 10, 1.2, 1, 100, 1000, 0)
)
foreach ($case in $cases) {
    $plan = $calculator::Calculate($case[1], $case[2], $case[3], $case[4], $case[5], $case[6], $case[7], $case[8], $auto, $false)
    if ($plan.Total -ne $case[9]) { throw "$($case[0]): expected $($case[9]), got $($plan.Total)" }
    Write-Output "PASS: $($case[0])"
}
$normal = $calculator::Calculate(.6, .4, .3, 10, 1.2, 1, 6, 1000, [ARLogistics.Data.StackOrientation]::Original, $false)
$rotated = $calculator::Calculate(.6, .4, .3, 10, 1.2, 1, 6, 1000, [ARLogistics.Data.StackOrientation]::Rotated, $false)
if ($normal.PerLayer -ne 4 -or $rotated.PerLayer -ne 3) { throw 'Orientation counts incorrect' }
Write-Output 'PASS: orientation changes footprint counts'
foreach ($w in @(.17, .31, .4, .7, 1.3)) {
    foreach ($h in @(.1, .3, 2.0)) {
        foreach ($load in @(0, 50, 1000)) {
            $p = $calculator::Calculate($w, .4, $h, 5, 1.2, 1, 3, $load, $auto, $false)
            if ($p.Total -gt 0 -and ($p.TotalWeight -gt $load + .001 -or $p.StackHeight -gt 1.801)) { throw 'Capacity exceeds limits' }
        }
    }
}
Write-Output 'PASS: 45 height/load combinations stay within limits'
foreach ($ceiling in @(1.1, 3, 6, 100, 600)) {
    foreach ($orientation in [Enum]::GetValues([ARLogistics.Data.StackOrientation])) {
        $p = $calculator::Calculate(.35, .48, .315, 3.008, 1.2, 1, $ceiling, 1000, $orientation, $false)
        if ($p.Layers -gt 5 -or $p.StackHeight -gt [Math]::Min(1.8, $ceiling - .3) + .001) { throw 'Tall-stack regression' }
    }
}
Write-Output 'PASS: screenshot dimensions stay within 5 layers in all orientations, even at 600m ceiling'
