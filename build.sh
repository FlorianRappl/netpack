#!/bin/bash

PID=`./platform.sh`

rm -rf src/NetPack/bin/Release/net8.0/$PID/publish
dotnet publish -r $PID -c Release src/NetPack/NetPack.csproj
cp -r src/NetPack/bin/Release/net8.0/$PID/publish npm/@netpack/$PID/bin

cd npm/dev
npm i
npm run build
cd ../..
