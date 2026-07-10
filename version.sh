#!/bin/bash

set -e

# Require a version argument
if [ $# -ne 1 ]; then
    echo "Usage: $0 <version>"
    exit 1
fi

VERSION="$1"
PROPS_FILE="src/Directory.Build.props"

# Update the VersionPrefix element
sed -i.bak "s|<VersionPrefix>[^<]*</VersionPrefix>|<VersionPrefix>${VERSION}</VersionPrefix>|" "$PROPS_FILE"
rm -f "${PROPS_FILE}.bak"

cd src/npm/dev
npm run update-version
cd ../../..
