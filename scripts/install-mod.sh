#!/usr/bin/env bash

set -euo pipefail

VERBOSE=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    -v|--verbose)
      VERBOSE=1
      shift
      ;;
    *)
      echo "Unknown option: $1" >&2
      echo "Usage: $0 [-v|--verbose]" >&2
      exit 1
      ;;
  esac
done

log_verbose() {
  if [[ "$VERBOSE" -eq 1 ]]; then
    echo "$@"
  fi
}

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd)"
ENV_FILE="${ENV_FILE:-$REPO_ROOT/.env}"

log_verbose "Resolved script dir: $SCRIPT_DIR"
log_verbose "Resolved repo root: $REPO_ROOT"
log_verbose "Using env file: $ENV_FILE"

if [[ -f "$ENV_FILE" ]]; then
  log_verbose "Loading environment from $ENV_FILE"
  # shellcheck disable=SC1090
  source "$ENV_FILE"
else
  log_verbose "No env file found, using process environment"
fi

: "${STEAMAPPS_DIR:?Set STEAMAPPS_DIR in .env or the environment}"

BUILD_CONFIGURATION="${BUILD_CONFIGURATION:-Release-Bep5}"
GAME_DIR="${GAME_DIR:-$STEAMAPPS_DIR/common/Nuclear Option}"
MOD_INSTALL_DIR="${MOD_INSTALL_DIR:-$GAME_DIR/BepInEx/plugins/NuclearVOIP}"
BUILD_OUTPUT_DIR="$REPO_ROOT/bin/x64/$BUILD_CONFIGURATION/netstandard2.1"
BEPINEX_CONFIG_FILE="$GAME_DIR/BepInEx/config/BepInEx.cfg"

detect_bepinex_major() {
  if [[ ! -f "$BEPINEX_CONFIG_FILE" ]]; then
    return 1
  fi

  local version_line
  version_line="$(grep -E '^Version[[:space:]]*=' "$BEPINEX_CONFIG_FILE" || true)"
  if [[ -z "$version_line" ]]; then
    return 1
  fi

  local version
  version="${version_line#*=}"
  version="${version%%.*}"

  if [[ "$version" =~ ^[0-9]+$ ]]; then
    printf '%s\n' "$version"
    return 0
  fi

  return 1
}

expected_build_for_major() {
  case "$1" in
    5)
      printf '%s\n' "Release-Bep5"
      ;;
    6)
      printf '%s\n' "Release-Bep6"
      ;;
    *)
      return 1
      ;;
  esac
}

if detected_major="$(detect_bepinex_major)"; then
  log_verbose "Detected BepInEx major version: $detected_major"

  if expected_build="$(expected_build_for_major "$detected_major")"; then
    if [[ "$BUILD_CONFIGURATION" != "$expected_build" ]]; then
      echo "Warning: requested build '$BUILD_CONFIGURATION' does not match installed BepInEx $detected_major." >&2
      echo "Expected build configuration: $expected_build" >&2
    fi
  fi
else
  log_verbose "Could not detect BepInEx version from $BEPINEX_CONFIG_FILE"
fi

log_verbose "Build configuration: $BUILD_CONFIGURATION"
log_verbose "Game directory: $GAME_DIR"
log_verbose "Install directory: $MOD_INSTALL_DIR"
log_verbose "Build output directory: $BUILD_OUTPUT_DIR"
log_verbose "BepInEx config file: $BEPINEX_CONFIG_FILE"

echo "Building NuclearVOIP ($BUILD_CONFIGURATION)..."
dotnet build "$REPO_ROOT/NuclearVOIP.sln" -c "$BUILD_CONFIGURATION"

log_verbose "Ensuring install directory exists"
mkdir -p "$MOD_INSTALL_DIR"

copy_if_exists() {
  local src="$1"
  local dest_dir="$2"

  if [[ -f "$src" ]]; then
    log_verbose "Copying $(basename "$src") to $dest_dir"
    cp "$src" "$dest_dir/"
  else
    log_verbose "Skipping missing file: $src"
  fi
}

copy_if_exists "$BUILD_OUTPUT_DIR/NuclearVOIP.dll" "$MOD_INSTALL_DIR"
copy_if_exists "$BUILD_OUTPUT_DIR/NuclearVOIP.deps.json" "$MOD_INSTALL_DIR"

if [[ -n "${OPUS_DLL_SOURCE:-}" ]]; then
  log_verbose "Configured opus source: $OPUS_DLL_SOURCE"
  copy_if_exists "$OPUS_DLL_SOURCE" "$MOD_INSTALL_DIR"
else
  log_verbose "No OPUS_DLL_SOURCE configured"
fi

if [[ ! -f "$MOD_INSTALL_DIR/NuclearVOIP.dll" ]]; then
  echo "Install failed: NuclearVOIP.dll was not copied." >&2
  exit 1
fi

log_verbose "Verified NuclearVOIP.dll exists in install directory"
echo "Installed to: $MOD_INSTALL_DIR"

if [[ ! -f "$MOD_INSTALL_DIR/opus.dll" ]]; then
  echo "Note: opus.dll is not present in the install directory."
  echo "Set OPUS_DLL_SOURCE in .env if you want this script to copy it automatically."
fi
