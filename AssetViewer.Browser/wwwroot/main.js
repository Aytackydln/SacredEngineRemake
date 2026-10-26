import {dotnet} from './_framework/dotnet.js';

try {
    const runtime = await dotnet.withDiagnosticTracing(false).create();
    const exports = await runtime.getAssemblyExports('AssetViewer.Browser');
    globalThis.sacred = exports.AssetViewer.Browser.Startup.BrowserCheats;
    await runtime.runMain();
    document.getElementById('startup-status')?.remove();
} catch (error) {
    console.error('[Browser] Startup failed', error);
    document.getElementById('startup-status').textContent = `Startup failed: ${error.message}`;
}
