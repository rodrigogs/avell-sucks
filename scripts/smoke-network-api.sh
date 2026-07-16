#!/usr/bin/env bash
# End-to-end smoke test for the network API + MCP exposure (Task 18, non-elevated subset).
# Runs ON the remote Windows+WSL box. Launches the server via WSL interop (the only
# launch path that reliably listens here) and drives HTTP checks from the Windows side
# via PowerShell (so loopback/proxy behave like a real client).
#
# Validates over the wire: loopback read + MCP without auth; MCP endpoint absent when
# disabled; fail-closed auth on a non-loopback bind (no token / wrong token / valid
# token); and the remote-write gate returning 403 BEFORE any hardware write.
# Does NOT actuate real EC hardware, install the service, or open the firewall.
set -u
EXE_DIR="/mnt/c/Temp/avs-server"
EXE="$EXE_DIR/AvellSucks.Server.exe"
CFG="/mnt/c/ProgramData/AvellSucks/service.json"
PS="/mnt/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe"
PORT=5081
TOKEN="smoke-token-abc123"
# SHA-256 hex of the token (matches TokenHasher.HashHex)
TOKEN_HASH=$(printf '%s' "$TOKEN" | sha256sum | awk '{print $1}')

pass=0; fail=0
check() { # name  expected  actual
  if [ "$2" = "$3" ]; then echo "[PASS] $1 (got $3)"; pass=$((pass+1));
  else echo "[FAIL] $1 (expected $2, got $3)"; fail=$((fail+1)); fi
}

write_cfg() { # bind  tokenHashOrEmpty  allowRemoteWrites(true/false)  mcp(true/false)
  local bind="$1" th="$2" arw="$3" mcp="$4" thjson="null"
  [ -n "$th" ] && thjson="\"$th\""
  mkdir -p "$(dirname "$CFG")"
  cat > "$CFG" <<JSON
{"bindAddress":"$bind","port":$PORT,"scheme":"http","httpsCertPath":null,"auth":{"bearerTokenSha256":$thjson,"mtlsEnabled":false,"mtlsCaThumbprint":null},"allowRemoteWrites":$arw,"mcpEnabled":$mcp,"firewallAutoOpen":false}
JSON
}

TASKKILL="/mnt/c/Windows/System32/taskkill.exe"
start_srv() { # bindAddr
  # Ensure no orphan from a prior scenario is holding the port (a bash `kill` only
  # reaps the WSL-interop wrapper, not the Windows AvellSucks.Server.exe process).
  "$TASKKILL" /IM AvellSucks.Server.exe /F >/dev/null 2>&1; sleep 1
  ( cd "$EXE_DIR" && ./AvellSucks.Server.exe > /tmp/avs-srv.log 2>&1 & )
  # readiness: TCP connect to the bind addr from the Windows side, up to ~30s
  for _ in $(seq 1 30); do
    sleep 1
    local ok
    ok=$("$PS" -NoProfile -Command "try{\$t=New-Object Net.Sockets.TcpClient;\$t.Connect('$1',$PORT);\$t.Close();'Y'}catch{'N'}" 2>/dev/null | tr -d '\r')
    [ "$ok" = "Y" ] && return 0
  done
  return 1
}
stop_srv() { "$TASKKILL" /IM AvellSucks.Server.exe /F >/dev/null 2>&1; sleep 1; }

# HTTP request from Windows; echoes the numeric status (or -1 on connect failure).
req() { # method url [authHeader] [jsonBody]
  local method="$1" url="$2" auth="${3:-}" body="${4:-}"
  local hdr="" bodyarg=""
  [ -n "$auth" ] && hdr="-Headers @{Authorization='$auth';Accept='application/json, text/event-stream'}"
  [ -z "$auth" ] && hdr="-Headers @{Accept='application/json, text/event-stream'}"
  [ -n "$body" ] && bodyarg="-Body '$body' -ContentType 'application/json'"
  "$PS" -NoProfile -Command "try{(Invoke-WebRequest -Uri '$url' -Method $method $hdr $bodyarg -TimeoutSec 6 -UseBasicParsing -Proxy \$null).StatusCode}catch{if(\$_.Exception.Response){[int]\$_.Exception.Response.StatusCode}else{-1}}" 2>/dev/null | tr -d '\r'
}

LAN=$("$PS" -NoProfile -Command "(Get-NetIPAddress -AddressFamily IPv4 | Where-Object {\$_.IPAddress -ne '127.0.0.1' -and \$_.PrefixOrigin -ne 'WellKnown'} | Select-Object -First 1).IPAddress" 2>/dev/null | tr -d '\r')

echo "=== SCENARIO 1: loopback, no auth, MCP on ==="
write_cfg "127.0.0.1" "" false true
if start_srv "127.0.0.1"; then
  check "loopback GET /" 200 "$(req GET "http://127.0.0.1:$PORT/")"
  check "loopback GET /api/system/snapshot" 200 "$(req GET "http://127.0.0.1:$PORT/api/system/snapshot")"
  mcp='{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"smoke","version":"1"}}}'
  check "loopback POST /mcp initialize" 200 "$(req POST "http://127.0.0.1:$PORT/mcp" "" "$mcp")"
else echo "[FAIL] scenario1 server never listened"; fail=$((fail+1)); fi
stop_srv

echo "=== SCENARIO 1b: MCP disabled -> /mcp absent ==="
write_cfg "127.0.0.1" "" false false
if start_srv "127.0.0.1"; then
  mcp='{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}'
  check "MCP off -> /mcp 404" 404 "$(req POST "http://127.0.0.1:$PORT/mcp" "" "$mcp")"
else echo "[FAIL] scenario1b server never listened"; fail=$((fail+1)); fi
stop_srv

if [ -n "$LAN" ]; then
  echo "=== SCENARIO 2: non-loopback bind ($LAN), bearer required ==="
  write_cfg "$LAN" "$TOKEN_HASH" false true
  if start_srv "$LAN"; then
    n=$(req GET "http://$LAN:$PORT/api/system/snapshot"); [ "$n" = "401" -o "$n" = "403" ] && n="deny"
    check "remote GET no token -> deny" deny "$n"
    b=$(req GET "http://$LAN:$PORT/api/system/snapshot" "Bearer wrong-token"); [ "$b" = "401" -o "$b" = "403" ] && b="deny"
    check "remote GET wrong token -> deny" deny "$b"
    check "remote GET valid token -> 200" 200 "$(req GET "http://$LAN:$PORT/api/system/snapshot" "Bearer $TOKEN")"
    check "remote WRITE valid token, remote-writes OFF -> 403" 403 "$(req POST "http://$LAN:$PORT/api/fan/mode" "Bearer $TOKEN" '{"mode":"auto"}')"
  else echo "[FAIL] scenario2 server never listened"; fail=$((fail+1)); fi
  stop_srv
else
  echo "[WARN] no non-loopback IPv4 found; scenario 2 skipped"
fi

# reset to safe defaults
write_cfg "127.0.0.1" "" false false
echo "=== RESULT: $pass passed, $fail failed ==="
exit $([ "$fail" -eq 0 ] && echo 0 || echo 1)
