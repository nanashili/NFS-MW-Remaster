#!/usr/bin/env bash
# Requires the Boot scene already running in a focused Unity Game view.
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.."
case "${1:-performance}" in
  performance)
    script=Tools/FrontendValidation/showroom-performance.cs
    report=Tools/FrontendValidation/Evidence/showroom-performance-latest.txt ;;
  cache)
    script=Tools/FrontendValidation/showroom-cache-check.cs
    report=Tools/FrontendValidation/Evidence/showroom-performance-20260910/cache-check.txt ;;
  *) echo 'Usage: showroom-check.sh [performance|cache]' >&2; exit 2 ;;
esac
previous="$(cat "$report" 2>/dev/null || true)"
payload="$(jq -n --rawfile code "$script" '{name:"Unity_RunCommand",arguments:{Code:$code,Title:"Showroom validation"}}')"
response="$(python3 Tools/WorldTools/unity_mcp.py tools/call "$payload")"
if ! jq -e '.result.structuredContent.data.isExecutionSuccessful == true' <<< "$response" >/dev/null; then
  echo "$response" >&2
  exit 1
fi
deadline=$((SECONDS + 180))
while (( SECONDS < deadline )); do
  current="$(cat "$report" 2>/dev/null || true)"
  if [[ "$current" != "$previous" ]] && rg -q '^(FINISHED|FAILED)' <<< "$current"; then
    echo "$current"
    rg -q '^FINISHED PASS$' <<< "$current"
    exit $?
  fi
  sleep 1
done
echo "Timed out; inspect $report" >&2
exit 1
