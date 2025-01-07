#!/bin/bash

PID=`./platform.sh`

echo $PID

dotnet publish -r $PID -c Release ./src/NetPack/NetPack.csproj

echo "Test Graph"
time ./src/NetPack/bin/Release/net8.0/$PID/publish/NetPack graph data/projects/large/src/index.html

rm -rf dist

echo "Test Bundling"
time ./src/NetPack/bin/Release/net8.0/$PID/publish/NetPack bundle data/projects/large/src/index.html --minify

ls -l dist
