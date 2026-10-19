using System.Text;
using System.Text.Json;

namespace PzLauncher.Core.Steam.Workshop.Helper;

/// <summary>Entry point of the short-lived Steam helper process: one request on stdin, JSON-line events on stdout.</summary>
public static class SteamHelperHost
{
    /// <returns>0 after a <c>finished</c> event (items may still have failed), 1 after a <c>failed</c> event.</returns>
    public static int Run(Stream input, Stream output)
    {
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using var reader = new StreamReader(input, encoding);
        using var writer = new StreamWriter(output, encoding) { AutoFlush = true };
        void Write(SteamHelperEvent e) => writer.WriteLine(JsonSerializer.Serialize(e, SteamHelperJsonContext.Default.SteamHelperEvent));

        var request = ReadRequest(reader.ReadLine());
        if (request is null || !Path.IsPathFullyQualified(request.SteamApiPath))
        {
            Write(new SteamHelperFailed(SteamHelperFailure.BadRequest, "expected one JSON request line with an absolute steamApiPath"));
            return 1;
        }

        SteamApi api;
        try
        {
            api = SteamApi.Load(request.SteamApiPath);
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
        {
            Write(new SteamHelperFailed(SteamHelperFailure.LibraryNotLoaded, ex.Message));
            return 1;
        }
        catch (EntryPointNotFoundException ex)
        {
            Write(new SteamHelperFailed(SteamHelperFailure.ExportMissing, ex.Message));
            return 1;
        }

        using (api)
        {
            var init = api.Init();
            switch (init.Result)
            {
                case SteamApiInitResult.Ok:
                    break;
                case SteamApiInitResult.NoSteamClient:
                    Write(new SteamHelperFailed(SteamHelperFailure.SteamNotRunning, init.Message));
                    return 1;
                case SteamApiInitResult.VersionMismatch:
                    Write(new SteamHelperFailed(SteamHelperFailure.VersionMismatch, init.Message));
                    return 1;
                case SteamApiInitResult.FailedGeneric:
                    Write(new SteamHelperFailed(SteamHelperFailure.InitFailed, init.Message));
                    return 1;
            }

            Write(new SteamHelperStarted());
            var session = new SteamHelperSession(api, Write, TimeProvider.System);
            switch (request.Action)
            {
                case SteamHelperAction.Subscribe:
                    session.ChangeSubscriptions(request.Ids, subscribe: true);
                    break;
                case SteamHelperAction.Unsubscribe:
                    session.ChangeSubscriptions(request.Ids, subscribe: false);
                    break;
                case SteamHelperAction.Update:
                    session.Update(request.Ids);
                    break;
            }
        }

        Write(new SteamHelperFinished());
        return 0;
    }

    private static SteamHelperRequest? ReadRequest(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(line, SteamHelperJsonContext.Default.SteamHelperRequest);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
