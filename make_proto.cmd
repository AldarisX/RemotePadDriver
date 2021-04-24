@echo off
protoc --csharp_out=.\RemotePadDriver proto\*.proto
pause