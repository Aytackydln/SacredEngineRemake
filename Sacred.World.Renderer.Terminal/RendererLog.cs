namespace Sacred.World.Renderer.Terminal;

/// <summary>Keeps the exporter concise while allowing shared-library console diagnostics on request.</summary>
internal sealed class RendererLog : IDisposable
{
    private static TextWriter _output = Console.Out;
    private readonly TextWriter _previousOutput;
    private static bool _verbose;

    public RendererLog(bool verbose)
    {
        _previousOutput = Console.Out;
        _output = _previousOutput;
        _verbose = verbose;
        // The exporter owns this process. Shared loaders write diagnostics directly
        // to stdout; keep stderr intact and send our summaries to the saved writer.
        if (!verbose) Console.SetOut(TextWriter.Null);
    }

    public static void Info(string message) => _output.WriteLine(message);

    public static void Detail(string message)
    {
        if (_verbose) _output.WriteLine(message);
    }

    public void Dispose() => Console.SetOut(_previousOutput);
}
