#!/bin/bash
# Run the test suite in the same SDK image the Dockerfile builds with.
# No local .NET SDK required.
set -e
cd "$(dirname "$0")"
exec docker run --rm \
  -v "$PWD":/src -w /src \
  -v nzbdav-nuget:/root/.nuget/packages \
  mcr.microsoft.com/dotnet/sdk:10.0-alpine \
  dotnet test backend.Tests/backend.Tests.csproj "$@"
