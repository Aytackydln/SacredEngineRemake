using System.Runtime.InteropServices.JavaScript;
using AssetViewer.Browser.Rendering;
using AssetViewer.Browser.Startup;
using Avalonia;
using Avalonia.Browser;

await JSHost.ImportAsync("files", "../files.js");
await JSHost.ImportAsync("webgpu", "../webgpu.js");
WebGpuShaderSources.Register();
await AppBuilder.Configure<BrowserApp>().WithInterFont().StartBrowserAppAsync("out");
