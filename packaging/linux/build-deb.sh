#!/usr/bin/env bash
# Build a Debian package from a self-contained Linux publish of Datum.App.
# Usage: packaging/linux/build-deb.sh <version> <publish-dir>
# Produces datum_<version>_amd64.deb in the current directory. Run from the repo root.
set -euo pipefail

VERSION="${1:?usage: build-deb.sh <version> <publish-dir>}"
PUBLISH_DIR="${2:?usage: build-deb.sh <version> <publish-dir>}"
ARCH="amd64"
PKG="datum_${VERSION}_${ARCH}"
OUT="$(pwd)/${PKG}.deb"

# Stage in a native-filesystem temp dir so file permissions are well-defined (dpkg-deb rejects
# the 777 perms that a Windows-mounted working tree reports under WSL).
STAGE="$(mktemp -d)"
ROOT="${STAGE}/${PKG}"
trap 'rm -rf "${STAGE}"' EXIT
mkdir -p "${ROOT}/DEBIAN" "${ROOT}/opt/datum" "${ROOT}/usr/bin" "${ROOT}/usr/share/applications"

# Application payload (the self-contained single-file executable and any side files).
cp -r "${PUBLISH_DIR}/." "${ROOT}/opt/datum/"
chmod +x "${ROOT}/opt/datum/Datum.App"

# Launcher on PATH.
cat > "${ROOT}/usr/bin/datum" <<'EOF'
#!/bin/sh
exec /opt/datum/Datum.App "$@"
EOF
chmod +x "${ROOT}/usr/bin/datum"

# Desktop entry.
cp packaging/linux/datum.desktop "${ROOT}/usr/share/applications/datum.desktop"

# Package metadata.
cat > "${ROOT}/DEBIAN/control" <<EOF
Package: datum
Version: ${VERSION}
Section: science
Priority: optional
Architecture: ${ARCH}
Maintainer: MacCoss Lab <maccoss@uw.edu>
Description: Peak sampling and quantification modeling
 An application to test and evaluate chromatography peak detection and
 integration methods against ground truth.
EOF

# Normalize permissions (dpkg-deb requires DEBIAN to be 0755 and files to be group-non-writable).
find "${ROOT}" -type d -exec chmod 0755 {} +
find "${ROOT}" -type f -exec chmod 0644 {} +
chmod 0755 "${ROOT}/opt/datum/Datum.App" "${ROOT}/usr/bin/datum"

dpkg-deb --build --root-owner-group "${ROOT}" "${OUT}"
echo "Built ${OUT}"
