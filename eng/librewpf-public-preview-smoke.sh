#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet_cmd="${DOTNET:-dotnet}"
expected_version="${LIBREWPF_SHARPDEVELOP_EXPECTED_VERSION:-$(sed -n 's:.*<ProGpuWpfSdkVersion>\([^<]*\)</ProGpuWpfSdkVersion>.*:\1:p' "$repo_root/Directory.Build.props" | head -n 1)}"
work_root="${LIBREWPF_SHARPDEVELOP_SMOKE_WORK_ROOT:-$(mktemp -d "${TMPDIR:-/tmp}/sharpdevelop-librewpf-public.XXXXXX")}"
keep_work_root="${LIBREWPF_SHARPDEVELOP_KEEP_SMOKE_WORK_ROOT:-0}"

if [[ -z "$expected_version" ]]; then
  echo "Unable to read ProGpuWpfSdkVersion from Directory.Build.props." >&2
  exit 1
fi

cleanup() {
  if [[ "$keep_work_root" != "1" ]]; then
    rm -rf "$work_root"
  else
    echo "LibreWPF SharpDevelop smoke work root: $work_root"
  fi
}
trap cleanup EXIT

mkdir -p "$work_root/nuget"

if git -C "$repo_root" grep -n -E 'preview\.sharpdevelop\.1|SharpDevelopLocal' -- \
  Directory.Build.props NuGet.config '*.csproj' '*.props'; then
  echo "Private SharpDevelop package pins or feeds remain in the repository." >&2
  exit 1
fi

echo "Building SharpDevelop against public LibreWPF $expected_version packages..."
NUGET_PACKAGES="$work_root/nuget" \
  "$dotnet_cmd" build "$repo_root/src/Main/SharpDevelop/SharpDevelop.Full.LibreWpf.csproj" \
  --configuration Release \
  --verbosity minimal \
  --nologo \
  --disable-build-servers \
  -p:LibreWpfSharpDevelopIncludeResourceToolkit=true

assets_file="$repo_root/src/Main/SharpDevelop/obj/project.assets.json"
if [[ ! -f "$assets_file" ]]; then
  echo "Missing restore assets: $assets_file" >&2
  exit 1
fi

package_ids=(
  LibreWPF.Interop
  LibreWPF.ProGPU
  LibreWPF.Transport
  LibreWinForms.System.Windows.Forms
  LibreWinForms.WindowsFormsIntegration
  ProGPU.Backend
  ProGPU.Compute
  ProGPU.DirectX
  ProGPU.Scene
  ProGPU.SkiaSharp
  ProGPU.System.Drawing.Common
  ProGPU.Text
  ProGPU.Transpiler
  ProGPU.Vector
)

for package_id in "${package_ids[@]}"; do
  if ! grep -Fq "\"$package_id/$expected_version\"" "$assets_file"; then
    echo "Restore assets do not contain $package_id/$expected_version." >&2
    exit 1
  fi
done

app_dll="$repo_root/src/Main/SharpDevelop/bin/Release/net10.0-windows/SharpDevelop.dll"
solution="$repo_root/samples/LineCounter/LineCounter.sln"
sample_source="$repo_root/samples/LineCounter/Src/LineCounterBrowser.cs"

if [[ ! -f "$app_dll" ]]; then
  echo "Missing SharpDevelop runtime: $app_dll" >&2
  exit 1
fi

sample_checksum_before="$(cksum "$sample_source")"

run_designer_smoke() {
  local attempt="$1"
  local config_dir="$work_root/config-$attempt"
  local home_dir="$work_root/home-$attempt"
  local log_file="$work_root/forms-designer-$attempt.log"
  local timeout_seconds="${LIBREWPF_SHARPDEVELOP_SMOKE_TIMEOUT_SECONDS:-90}"
  local pid watchdog_pid exit_code

  mkdir -p "$config_dir" "$home_dir"

  env \
    HOME="$home_dir" \
    DOTNET_ROLL_FORWARD=Major \
    DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    LIBREWPF_SHARPDEVELOP_CONFIG_DIR="$config_dir" \
    LIBREWPF_SHARPDEVELOP_TRACE_OPEN=1 \
    LIBREWPF_SHARPDEVELOP_FORMS_DESIGNER_SMOKE=1 \
    LIBREWPF_SHARPDEVELOP_EXIT_AFTER_MS=60000 \
    "$dotnet_cmd" "$app_dll" "$solution" >"$log_file" 2>&1 &
  pid=$!

  (
    sleep "$timeout_seconds"
    if kill -0 "$pid" 2>/dev/null; then
      kill "$pid" 2>/dev/null || true
      sleep 5
      kill -9 "$pid" 2>/dev/null || true
    fi
  ) &
  watchdog_pid=$!

  set +e
  wait "$pid"
  exit_code=$?
  set -e
  kill "$watchdog_pid" 2>/dev/null || true
  wait "$watchdog_pid" 2>/dev/null || true

  if [[ "$exit_code" -eq 0 ]] \
    && grep -Fq 'LibreWPF FormsDesigner smoke result=Attached' "$log_file" \
    && grep -Fq 'LibreWPF FormsDesigner custom-paint smoke result=Success' "$log_file" \
    && grep -Fq 'LibreWPF owner-draw smoke result=Success' "$log_file" \
    && grep -Fq 'LibreWPF FormsDesigner mutation smoke result=Success' "$log_file" \
    && grep -Fq 'toolboxUndoRedo=True' "$log_file" \
    && grep -Fq 'toolboxDeleteUndoRedo=True' "$log_file" \
    && grep -Fq 'LibreWPF WorkbenchStartup application exit code=0' "$log_file"; then
    grep -E 'FormsDesigner (smoke|custom-paint smoke|mutation smoke) result=|owner-draw smoke result=|application exit code=' "$log_file"
    return 0
  fi

  echo "FormsDesigner smoke attempt $attempt did not reach the complete success state." >&2
  tail -n 120 "$log_file" >&2
  return 1
}

if ! run_designer_smoke 1; then
  echo "Retrying once with a fresh HOME and SharpDevelop configuration directory..." >&2
  run_designer_smoke 2
fi

sample_checksum_after="$(cksum "$sample_source")"
if [[ "$sample_checksum_before" != "$sample_checksum_after" ]]; then
  echo "The FormsDesigner smoke changed $sample_source." >&2
  exit 1
fi

echo "SharpDevelop public LibreWPF $expected_version build and FormsDesigner smoke passed."
