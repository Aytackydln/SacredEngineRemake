using Sacred.Granny.Native.Worker;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: Sacred.Granny.Native.Worker <path-to-granny.dll>");
    return 2;
}

try
{
    using var granny = new Granny1NativeApi(Path.GetFullPath(args[0]));
    using var input = new BinaryReader(Console.OpenStandardInput());
    using var output = new BinaryWriter(Console.OpenStandardOutput());

    NativeWorkerProtocol.WriteHandshake(output);
    Console.Error.WriteLine($"Granny 1 worker ready: {args[0]}");
    while (NativeWorkerProtocol.TryReadRequest(input, out var payload))
    {
        byte[] response;
        try
        {
            var mesh = Granny1NativeMeshReader.Read(granny, payload);
            response = NativeWorkerProtocol.WriteSuccess(mesh);
            Console.Error.WriteLine(
                $"Granny 1 model loaded: {mesh.Buffers.Count} buffers, {mesh.Surfaces.Count} surfaces");
        }
        catch (Exception exception)
        {
            response = NativeWorkerProtocol.WriteFailure(exception.Message);
            Console.Error.WriteLine($"Granny 1 model load failed: {exception}");
        }

        output.Write(response.Length);
        output.Write(response);
        output.Flush();
    }

    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}
