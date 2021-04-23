@echo off
protoc --csharp_out=.\RemotePadDriver proto\*.proto
protoc --go_out=.\client\src .\proto\*.proto
protoc --java_out=proto\java proto\*.proto
pause