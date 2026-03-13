#!/bin/sh
set -eu
SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
chmod +x "$SCRIPT_DIR/GG2.ServerLauncher"
exec "$SCRIPT_DIR/GG2.ServerLauncher" "$@"