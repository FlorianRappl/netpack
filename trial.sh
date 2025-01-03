#!/bin/bash

dotnet publish -r linux-x64 -c Release ./src/NetPack/NetPack.csproj

echo "Test Graph"
time ./src/NetPack/bin/Release/net8.0/linux-x64/publish/NetPack graph data/projects/large/src/index.html

rm -rf dist

echo "Test Bundling"
time ./src/NetPack/bin/Release/net8.0/linux-x64/publish/NetPack bundle data/projects/large/src/index.html --minify

ls -l dist
