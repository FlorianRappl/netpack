#!/bin/bash

PID=`./platform.sh`

rm -rf src/NetPack/bin/Release/net8.0/$PID/publish
dotnet publish -r $PID -c Release -p:DebugType=None -p:DebugSymbols=false src/NetPack/NetPack.csproj

rm -rf src/npm/@netpack/$PID/bin
cp -r src/NetPack/bin/Release/net8.0/$PID/publish/ src/npm/@netpack/$PID/bin

cd src/npm/dev
npm i
npm run build
cd ../../..
