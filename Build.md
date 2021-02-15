# How to build

### Requirements for this guide
- Souce code from this repo;
- Source code from [sta/websocket-sharp](https://github.com/sta/websocket-sharp)
- Visual Studio;
- [ILRepack utility](https://www.nuget.org/packages/ILRepack/), extract executable from package to use;

### Steps
- Open websocket-sharp solution and accept project update (we don't care about example projects);
- Edit `WebSocket` class as follows:
  - add property: `public string UserAgent { get; set; }`;
  - in method `createHandshakeRequest()`, add this before `return` statement:
```
if (UserAgent != null)
{
    headers["User-Agent"] = UserAgent;
}
```
- Unload and edit `websocket-sharp.csproj` to match following:
```
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
    <OutputType>Library</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="System.ServiceModel" />
  </ItemGroup>
</Project>
```
- Switch solution from "ubuntu" related to regular configs;
- Load target project back and build it;
- Open PixelPlanetUtils project, delete `websocket-sharp` reference and add reference to DLL you just built instead;
- Deal with update checker, updater, class `PathTo`, app options to match your goals;
  - Create `Resources` folder in PixelPlanetUtils project folder if you are going to use updater - its EXE file is copied there to be included as resource;
- Change paths to ILRepack.exe and to output folder in app projects you want to build;
- Build solution in release mode;

If anything, I could forget to mention something.
