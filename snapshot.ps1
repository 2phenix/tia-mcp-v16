param([string]$out)
try { Add-Type -Path "C:\Program Files\Siemens\Automation\Portal V16\PublicAPI\V16\Siemens.Engineering.dll" } catch {}
$p = [Siemens.Engineering.TiaPortal]::GetProcesses()[0].Attach().Projects[0]
$gs = [Siemens.Engineering.IEngineeringServiceProvider].GetMethod("GetService")
$niT = [Siemens.Engineering.HW.Features.NetworkInterface]
function Dump($o, $path) {
  foreach ($ai in $o.GetAttributeInfos()) {
    $v = try { $o.GetAttribute($ai.Name) } catch { "<err>" }
    if ($v -ne $null -and -not ($v.GetType().IsPrimitive -or $v.GetType().IsEnum -or $v -is [string])) { $v = "(" + $v.GetType().Name + ")" }
    "$path | $($ai.Name) [$($ai.AccessMode)] = $v"
  }
}
function Walk($items, $path) {
  foreach ($di in $items) {
    $pp = "$path/$($di.Name)"
    Dump $di $pp
    $ni = $gs.MakeGenericMethod($niT).Invoke($di, $null)
    if ($ni) { foreach ($n in $ni.Nodes) { Dump $n "$pp/node" } }
    Walk $di.DeviceItems $pp
  }
}
& { foreach ($d in $p.Devices) { Dump $d $d.Name; Walk $d.DeviceItems $d.Name } } | Set-Content $out -Encoding UTF8
"$((Get-Content $out).Count) attributs -> $out"
