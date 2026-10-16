# YamlDotNet 16.3.0

MIT-licensed parser, .NET Framework 4.7 build (no extra NuGet dependencies).
Source: https://github.com/aaubry/YamlDotNet/tree/v16.3.0
Binary: `lib/net47/YamlDotNet.dll` from https://api.nuget.org/v3-flatcontainer/yamldotnet/16.3.0/yamldotnet.16.3.0.nupkg
SHA-256: `9172f0758ed76963e0ede3b5ca9a1fb5b82f64c7e7a92cb3a6de39efa933a668`.

The build verifies this exact hash before compilation/copying. Keep the DLL beside the manager EXE, with the MIT notice in `third_party/YamlDotNet/LICENSE.txt`. YAML tags, multiple documents, deep/large graphs and alias cycles are rejected before importing. No CLR object deserialization is used.
