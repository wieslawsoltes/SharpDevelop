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
report_fixture="$repo_root/src/AddIns/Analysis/CodeQuality/Reporting/DependencyReport.srd"

if [[ ! -f "$app_dll" ]]; then
  echo "Missing SharpDevelop runtime: $app_dll" >&2
  exit 1
fi

sample_checksum_before="$(cksum "$sample_source")"
report_checksum_before="$(cksum "$report_fixture")"

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

run_resource_toolkit_smoke() {
  local attempt="$1"
  local config_dir="$work_root/resource-toolkit-config-$attempt"
  local home_dir="$work_root/resource-toolkit-home-$attempt"
  local log_file="$work_root/resource-toolkit-$attempt.log"
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
    LIBREWPF_SHARPDEVELOP_RESOURCE_TOOLKIT_SMOKE=1 \
    LIBREWPF_SHARPDEVELOP_EXIT_AFTER_MS=30000 \
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
    && grep -Fq 'LibreWPF ResourceToolkit smoke result=Success' "$log_file" \
    && grep -Fq 'LibreWPF WorkbenchStartup application exit code=0' "$log_file"; then
    grep -E 'ResourceToolkit smoke result=|application exit code=' "$log_file"
    return 0
  fi

  echo "ResourceToolkit smoke attempt $attempt did not reach the complete success state." >&2
  tail -n 160 "$log_file" >&2
  return 1
}

if ! run_resource_toolkit_smoke 1; then
  echo "Retrying ResourceToolkit once with a fresh HOME and SharpDevelop configuration directory..." >&2
  run_resource_toolkit_smoke 2
fi

run_hex_editor_smoke() {
  local attempt="$1"
  local config_dir="$work_root/hex-editor-config-$attempt"
  local home_dir="$work_root/hex-editor-home-$attempt"
  local log_file="$work_root/hex-editor-$attempt.log"
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
    LIBREWPF_SHARPDEVELOP_HEXEDITOR_SMOKE=1 \
    LIBREWPF_SHARPDEVELOP_EXIT_AFTER_MS=30000 \
    "$dotnet_cmd" "$app_dll" >"$log_file" 2>&1 &
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
    && grep -Fq 'LibreWPF HexEditor smoke result=Success' "$log_file" \
    && grep -Fq 'repaintStable=True' "$log_file" \
    && grep -Fq 'LibreWPF WorkbenchStartup application exit code=0' "$log_file"; then
    grep -E 'HexEditor smoke result=|application exit code=' "$log_file"
    return 0
  fi

  echo "HexEditor smoke attempt $attempt did not reach the complete success state." >&2
  tail -n 160 "$log_file" >&2
  return 1
}

if ! run_hex_editor_smoke 1; then
  echo "Retrying HexEditor once with a fresh HOME and SharpDevelop configuration directory..." >&2
  run_hex_editor_smoke 2
fi

run_reporting_smoke() {
  local attempt="$1"
  local config_dir="$work_root/reporting-config-$attempt"
  local home_dir="$work_root/reporting-home-$attempt"
  local log_file="$work_root/reporting-$attempt.log"
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
    LIBREWPF_SHARPDEVELOP_REPORTING_SMOKE="$report_fixture" \
    LIBREWPF_SHARPDEVELOP_EXIT_AFTER_MS=30000 \
    "$dotnet_cmd" "$app_dll" >"$log_file" 2>&1 &
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
    && grep -Fq 'LibreWPF Reporting workbench smoke result=Success' "$log_file" \
    && grep -Fq 'binding=SharpDevelopReportsBinding view=DesignerView hosted=True presentation=True' "$log_file" \
    && grep -Fq 'initialName=DependencyReport sections=5 items=12 cleanSaveExact=True initialPreview=True' "$log_file" \
    && grep -Fq 'reloadSameView=True reloadName=DependencyReport-LibreWPF-Reloaded reloadSections=5 reloadItems=12 reloadedPreview=True' "$log_file" \
    && grep -Fq 'reloadCleanSaveExact=True dirtyCleared=True closed=True cleanup=True' "$log_file" \
    && grep -Fq 'LibreWPF WorkbenchStartup application exit code=0' "$log_file"; then
    grep -E 'Reporting workbench smoke result=|application exit code=' "$log_file"
    return 0
  fi

  echo "Reporting workbench smoke attempt $attempt did not reach the complete success state." >&2
  tail -n 160 "$log_file" >&2
  return 1
}

reporting_smoke_mode="${LIBREWPF_SHARPDEVELOP_REPORTING_SMOKE_MODE:-auto}"
reporting_smoke_passed=0
winforms_assembly="$work_root/nuget/librewinforms.system.windows.forms/$expected_version/lib/net10.0/System.Windows.Forms.dll"
if [[ "$reporting_smoke_mode" == "auto" ]]; then
  if [[ -f "$winforms_assembly" ]] && grep -aFq 'IWinFormsIdleHost' "$winforms_assembly"; then
    reporting_smoke_mode=1
  else
    reporting_smoke_mode=0
  fi
fi

if [[ "$reporting_smoke_mode" == "1" ]]; then
  if ! run_reporting_smoke 1; then
    echo "Retrying Reporting once with a fresh HOME and SharpDevelop configuration directory..." >&2
    run_reporting_smoke 2
  fi
  reporting_smoke_passed=1
elif [[ "$reporting_smoke_mode" == "0" ]]; then
  echo "Skipping the Reporting reload smoke because LibreWinForms $expected_version does not expose typed idle dispatch."
else
  echo "LIBREWPF_SHARPDEVELOP_REPORTING_SMOKE_MODE must be auto, 0, or 1." >&2
  exit 1
fi

sample_checksum_after="$(cksum "$sample_source")"
if [[ "$sample_checksum_before" != "$sample_checksum_after" ]]; then
  echo "The FormsDesigner smoke changed $sample_source." >&2
  exit 1
fi

report_checksum_after="$(cksum "$report_fixture")"
if [[ "$report_checksum_before" != "$report_checksum_after" ]]; then
  echo "The Reporting smoke changed $report_fixture." >&2
  exit 1
fi

if [[ "$reporting_smoke_passed" == "1" ]]; then
  echo "SharpDevelop public LibreWPF $expected_version build, FormsDesigner smoke, ResourceToolkit smoke, HexEditor smoke, and Reporting workbench smoke passed."
else
  echo "SharpDevelop public LibreWPF $expected_version build, FormsDesigner smoke, ResourceToolkit smoke, and HexEditor smoke passed; Reporting reload awaits typed LibreWinForms idle dispatch."
fi
