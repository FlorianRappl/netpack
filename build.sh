#!/bin/bash

rm -rf src/NetPack/bin/Release/net8.0/linux-x64/publish
dotnet publish -r linux-x64 -c Release src/NetPack/NetPack.csproj
cp -r src/NetPack/bin/Release/net8.0/linux-x64/publish npm/@netpack/linux-x64/bin

cd npm/dev
npm i
npm run build
cd ../..
