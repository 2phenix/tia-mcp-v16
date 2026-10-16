$scl = "FUNCTION ""FC_McpTest"" : Void`r`n{ S7_Optimized_Access := 'TRUE' }`r`nVERSION : 0.1`r`n   VAR_INPUT`r`n      a : Int;`r`n      b : Int;`r`n   END_VAR`r`n   VAR_OUTPUT`r`n      s : Int;`r`n   END_VAR`r`nBEGIN`r`n   #s := #a + #b;`r`nEND_FUNCTION`r`n"
function Call($id, $name, $a) {
  @{ jsonrpc = "2.0"; id = $id; method = "tools/call"; params = @{ name = $name; arguments = $a } } | ConvertTo-Json -Depth 5 -Compress
}
$reqs = @(
  (Call 1 "set_attribute" @{ plc = "PLC_1"; target = "node"; name = "Address"; value = "192.168.0.10"; expected = "192.168.0.1" }),
  (Call 2 "set_attribute" @{ plc = "PLC_1"; target = "node"; name = "Address"; value = "10.0.0.1"; expected = "192.168.0.1" }),
  (Call 3 "set_attribute" @{ plc = "PLC_1"; target = "cpu"; name = "OrderNumber"; value = "x"; expected = "6ES7 214-1HG40-0XB0" }),
  (Call 4 "set_attribute" @{ plc = "PLC_1"; target = "node"; name = "Address"; value = "192.168.0.1"; expected = "192.168.0.10" }),
  (Call 5 "import_scl" @{ plc = "PLC_1"; scl = $scl }),
  (Call 6 "compile" @{ plc = "PLC_1" }),
  (Call 7 "list_blocks" @{ plc = "PLC_1" })
)
$reqs | & "$PSScriptRoot\bin\Release\net48\tia-mcp-v16.exe" 2>$null | ForEach-Object {
  $j = $_ | ConvertFrom-Json
  "--- id $($j.id) erreur=$($j.result.isError)"
  $j.result.content[0].text
}

