#!/bin/bash

OS=$(uname -s)
ARCH=$(uname -m)

# Set the platform identifier
if [[ "$OS" == "Linux" ]]; then
    if [[ "$ARCH" == "x86_64" ]]; then
        echo "linux-x64"
    elif [[ "$ARCH" == "aarch64" ]]; then
        echo "linux-arm64"
    else
        echo "Unsupported architecture: $ARCH"
        exit 1
    fi
elif [[ "$OS" == "Darwin" ]]; then
    if [[ "$ARCH" == "x86_64" ]]; then
        # Check if running under Rosetta on ARM Macs
        if [[ "$(sysctl -n sysctl.proc_translated)" == "1" ]]; then
            echo "osx-arm64"
        else
            echo "osx-x64"
        fi
    elif [[ "$ARCH" == "arm64" ]]; then
        echo "osx-arm64"
    else
        echo "Unsupported architecture: $ARCH"
        exit 1
    fi
else
    echo "Unsupported OS: $OS"
    exit 1
fi
