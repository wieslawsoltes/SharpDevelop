#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
sdk_package_dir="${LIBREWPF_CANONICAL_SDK_PACKAGE_DIR:-}"
canonical_package_dir="${LIBREWPF_CANONICAL_WINFORMS_PACKAGE_DIR:-}"

if [[ ! -d "$sdk_package_dir" ]]; then
  echo "Set LIBREWPF_CANONICAL_SDK_PACKAGE_DIR to the LibreWPF SDK package artifact directory." >&2
  exit 1
fi

if [[ ! -d "$canonical_package_dir" ]]; then
  echo "Set LIBREWPF_CANONICAL_WINFORMS_PACKAGE_DIR to the canonical LibreWinForms/ProGPU package artifact directory." >&2
  exit 1
fi

discover_package_version() {
  local package_dir="$1"
  local package_id="$2"
  local -a packages=()
  local package_name

  shopt -s nullglob
  packages=("$package_dir/$package_id."*.nupkg)
  shopt -u nullglob
  if [[ "${#packages[@]}" -ne 1 ]]; then
    echo "Expected exactly one $package_id package in $package_dir; found ${#packages[@]}." >&2
    exit 1
  fi

  package_name="$(basename "${packages[0]}")"
  package_name="${package_name#"$package_id."}"
  printf '%s\n' "${package_name%.nupkg}"
}

sdk_version="$(discover_package_version "$sdk_package_dir" LibreWPF.Sdk)"
canonical_version="$(discover_package_version "$canonical_package_dir" LibreWinForms.System.Windows.Forms)"
progpu_version="$(discover_package_version "$canonical_package_dir" ProGPU.System.Drawing.Common)"

canonical_package="$canonical_package_dir/LibreWinForms.System.Windows.Forms.$canonical_version.nupkg"
canonical_entries="$(unzip -Z1 "$canonical_package")"
for required_entry in \
  "lib/net10.0/System.Windows.Forms.Design.dll" \
  "ref/net10.0/System.Windows.Forms.Design.dll"; do
  if ! grep -Fxq "$required_entry" <<<"$canonical_entries"; then
    echo "Canonical WinForms package is missing $required_entry." >&2
    exit 1
  fi
done

canonical_nuspec="$(unzip -p "$canonical_package" LibreWinForms.System.Windows.Forms.nuspec)"
if ! grep -Fq 'id="System.CodeDom" version="10.0.10"' <<<"$canonical_nuspec"; then
  echo "Canonical WinForms design package does not declare the qualified System.CodeDom dependency." >&2
  exit 1
fi

for required_package in LibreWinForms.ProGPU LibreWinForms.WindowsFormsIntegration; do
  if [[ ! -f "$canonical_package_dir/$required_package.$canonical_version.nupkg" ]]; then
    echo "Canonical package closure is missing $required_package.$canonical_version.nupkg." >&2
    exit 1
  fi
done

for required_package in LibreWPF.Interop ProGPU.Backend ProGPU.Compute ProGPU.DirectX ProGPU.Scene ProGPU.SkiaSharp ProGPU.Text ProGPU.Transpiler ProGPU.Vector; do
  if [[ ! -f "$canonical_package_dir/$required_package.$progpu_version.nupkg" ]]; then
    echo "ProGPU source closure is missing $required_package.$progpu_version.nupkg." >&2
    exit 1
  fi
done

LIBREWPF_SHARPDEVELOP_USE_CANONICAL_WINFORMS=1 \
LIBREWPF_SHARPDEVELOP_EXPECTED_VERSION="$sdk_version" \
LIBREWPF_SHARPDEVELOP_EXPECTED_WINFORMS_VERSION="$canonical_version" \
LIBREWPF_SHARPDEVELOP_EXPECTED_PROGPU_VERSION="$progpu_version" \
LIBREWPF_SHARPDEVELOP_PACKAGE_SOURCES="$sdk_package_dir;$canonical_package_dir" \
DOTNET_ROLL_FORWARD="${DOTNET_ROLL_FORWARD:-Major}" \
DOTNET_ROLL_FORWARD_TO_PRERELEASE="${DOTNET_ROLL_FORWARD_TO_PRERELEASE:-1}" \
  "$repo_root/eng/librewpf-public-preview-smoke.sh"
