# Windows Computer Use

Standalone Windows-native automation service for local clients. .NET 10 / Windows x64;
no plugin or remote server required. Source is maintained in WSL; execution is in an
**interactive Windows user session** (not a Windows service or elevated task).
Published releases are self-contained: the target Windows machine does not need
.NET installed; only the development/publishing machine needs the .NET 10 SDK.

## Develop from WSL

```bash
'/mnt/c/Program Files/dotnet/dotnet.exe' restore WindowsComputerUse.slnx -p:EnableWindowsTargeting=true
'/mnt/c/Program Files/dotnet/dotnet.exe' build WindowsComputerUse.slnx -p:EnableWindowsTargeting=true
'/mnt/c/Program Files/dotnet/dotnet.exe' test WindowsComputerUse.slnx -p:EnableWindowsTargeting=true
```

For a versioned Windows install, open Windows PowerShell in the WSL source directory
(for example `\\wsl.localhost\Ubuntu-24.04\home\jesrey0\projects\windows-computer-use`):

```powershell
.\scripts\publish.ps1 -Version 0.1.0
.\scripts\install.ps1 -Version 0.1.0
& "$env:LOCALAPPDATA\WindowsComputerUse\wcu-host.cmd"  # leave running in this terminal
& "$env:LOCALAPPDATA\WindowsComputerUse\wcu.cmd" health
& "$env:LOCALAPPDATA\WindowsComputerUse\wcu.cmd" windows
& "$env:LOCALAPPDATA\WindowsComputerUse\wcu.cmd" tree 0x123456
& "$env:LOCALAPPDATA\WindowsComputerUse\wcu.cmd" screenshot 0x123456 .\capture.png
```

Alternatively build and run the Host and CLI projects with Windows `dotnet` directly;
both use the same per-user token file. Updates publish a *new* version and run
`update.ps1 -Version <version>`; restart the host afterwards. To roll back, stop
the host and run the earlier version's installed `host/*.exe` directly, or switch
`current.txt` back to an existing release version while no host is running. To
uninstall, stop the host then run `scripts/uninstall.ps1` (`-KeepData` retains the
token and logs). No global install or administrator privileges are needed.

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for API, boundaries, lifecycle,
security, and capture/input limitations.
