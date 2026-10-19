using System.Runtime.InteropServices;
using System.Text;
using PzLauncher.Core.Platform;

namespace PzLauncher.Core.Steam.Workshop.Helper;

/// <summary>Steamworks flat API of a <c>steam_api</c> library loaded from a path (the game install's own copy).</summary>
/// <remarks>Not thread-safe: one thread inits, pumps callbacks and shuts down.</remarks>
internal sealed unsafe class SteamApi : IDisposable
{
    private const int ErrMsgSize = 1024;

    private readonly delegate* unmanaged[Cdecl]<byte*, SteamApiInitResult> _initFlat;
    private readonly delegate* unmanaged[Cdecl]<void> _shutdown;
    private readonly delegate* unmanaged[Cdecl]<int> _getHSteamPipe;
    private readonly delegate* unmanaged[Cdecl]<void> _manualDispatchInit;
    private readonly delegate* unmanaged[Cdecl]<int, void> _runFrame;
    private readonly delegate* unmanaged[Cdecl]<int, CallbackMsg*, byte> _getNextCallback;
    private readonly delegate* unmanaged[Cdecl]<int, void> _freeLastCallback;
    private readonly delegate* unmanaged[Cdecl]<int, ulong, void*, int, int, byte*, byte> _getApiCallResult;
    private readonly delegate* unmanaged[Cdecl]<nint> _steamUgc;
    private readonly delegate* unmanaged[Cdecl]<nint, ulong, uint> _getItemState;
    private readonly delegate* unmanaged[Cdecl]<nint, ulong, ulong*, ulong*, byte> _getItemDownloadInfo;
    private readonly delegate* unmanaged[Cdecl]<nint, ulong, ulong> _subscribeItem;
    private readonly delegate* unmanaged[Cdecl]<nint, ulong, ulong> _unsubscribeItem;
    private readonly delegate* unmanaged[Cdecl]<nint, ulong, byte, byte> _downloadItem;

    private int _pipe;
    private nint _ugc;
    private bool _initialized;

    // Never freed: the helper process exits right after shutdown.
    private SteamApi(nint library)
    {
        _initFlat = (delegate* unmanaged[Cdecl]<byte*, SteamApiInitResult>)NativeLibrary.GetExport(library, "SteamAPI_InitFlat");
        _shutdown = (delegate* unmanaged[Cdecl]<void>)NativeLibrary.GetExport(library, "SteamAPI_Shutdown");
        _getHSteamPipe = (delegate* unmanaged[Cdecl]<int>)NativeLibrary.GetExport(library, "SteamAPI_GetHSteamPipe");
        _manualDispatchInit = (delegate* unmanaged[Cdecl]<void>)NativeLibrary.GetExport(library, "SteamAPI_ManualDispatch_Init");
        _runFrame = (delegate* unmanaged[Cdecl]<int, void>)NativeLibrary.GetExport(library, "SteamAPI_ManualDispatch_RunFrame");
        _getNextCallback = (delegate* unmanaged[Cdecl]<int, CallbackMsg*, byte>)NativeLibrary.GetExport(library, "SteamAPI_ManualDispatch_GetNextCallback");
        _freeLastCallback = (delegate* unmanaged[Cdecl]<int, void>)NativeLibrary.GetExport(library, "SteamAPI_ManualDispatch_FreeLastCallback");
        _getApiCallResult = (delegate* unmanaged[Cdecl]<int, ulong, void*, int, int, byte*, byte>)NativeLibrary.GetExport(library, "SteamAPI_ManualDispatch_GetAPICallResult");
        _steamUgc = (delegate* unmanaged[Cdecl]<nint>)NativeLibrary.GetExport(library, "SteamAPI_SteamUGC_v021");
        _getItemState = (delegate* unmanaged[Cdecl]<nint, ulong, uint>)NativeLibrary.GetExport(library, "SteamAPI_ISteamUGC_GetItemState");
        _getItemDownloadInfo = (delegate* unmanaged[Cdecl]<nint, ulong, ulong*, ulong*, byte>)NativeLibrary.GetExport(library, "SteamAPI_ISteamUGC_GetItemDownloadInfo");
        _subscribeItem = (delegate* unmanaged[Cdecl]<nint, ulong, ulong>)NativeLibrary.GetExport(library, "SteamAPI_ISteamUGC_SubscribeItem");
        _unsubscribeItem = (delegate* unmanaged[Cdecl]<nint, ulong, ulong>)NativeLibrary.GetExport(library, "SteamAPI_ISteamUGC_UnsubscribeItem");
        _downloadItem = (delegate* unmanaged[Cdecl]<nint, ulong, byte, byte>)NativeLibrary.GetExport(library, "SteamAPI_ISteamUGC_DownloadItem");
    }

    /// <summary>Callback structs use pack 8 on Windows and pack 4 elsewhere (VALVE_CALLBACK_PACK_LARGE / SMALL).</summary>
    public bool LargeCallbackPack { get; } = HostRuntime.IsWindows;

    /// <exception cref="DllNotFoundException">The library is missing or can't be loaded.</exception>
    /// <exception cref="BadImageFormatException">The file isn't a library for this process.</exception>
    /// <exception cref="EntryPointNotFoundException">The library lacks an export this binding needs.</exception>
    public static SteamApi Load(string path) => new(NativeLibrary.Load(path));

    public SteamInitOutcome Init()
    {
        var message = stackalloc byte[ErrMsgSize];
        var result = _initFlat(message);
        if (result != SteamApiInitResult.Ok)
        {
            return new SteamInitOutcome(result, ReadMessage(message));
        }

        _initialized = true;
        _manualDispatchInit();
        _pipe = _getHSteamPipe();
        _ugc = _steamUgc();
        return new SteamInitOutcome(result, "");
    }

    public SteamItemState GetItemState(ulong id) => (SteamItemState)_getItemState(_ugc, id);

    /// <returns>(0, 0) when Steam has no download info for the item.</returns>
    public (ulong Downloaded, ulong Total) GetItemDownloadInfo(ulong id)
    {
        ulong downloaded = 0, total = 0;
        return _getItemDownloadInfo(_ugc, id, &downloaded, &total) != 0 ? (downloaded, total) : (0, 0);
    }

    /// <returns>The call handle, 0 when Steam refused the call.</returns>
    public ulong SubscribeItem(ulong id) => _subscribeItem(_ugc, id);

    /// <returns>The call handle, 0 when Steam refused the call.</returns>
    public ulong UnsubscribeItem(ulong id) => _unsubscribeItem(_ugc, id);

    public bool DownloadItem(ulong id, bool highPriority) => _downloadItem(_ugc, id, highPriority ? (byte)1 : (byte)0) != 0;

    /// <summary>Runs one dispatch frame and returns every callback and finished call result it produced.</summary>
    public List<SteamCallback> Pump()
    {
        var callbacks = new List<SteamCallback>();
        _runFrame(_pipe);
        CallbackMsg message;
        while (_getNextCallback(_pipe, &message) != 0)
        {
            try
            {
                callbacks.Add(message.Callback == SteamCallbackIds.ApiCallCompleted
                    ? ReadCallResult(message)
                    : new SteamCallback(message.Callback, 0, false, new ReadOnlySpan<byte>(message.Param, message.ParamSize).ToArray()));
            }
            finally
            {
                _freeLastCallback(_pipe);
            }
        }

        return callbacks;
    }

    public void Dispose()
    {
        if (_initialized)
        {
            _initialized = false;
            _shutdown();
        }
    }

    // SteamAPICallCompleted_t has the same offsets under both packings.
    private SteamCallback ReadCallResult(CallbackMsg message)
    {
        var call = *(ulong*)message.Param;
        var callbackId = *(int*)(message.Param + 8);
        var size = *(uint*)(message.Param + 12);
        var payload = new byte[size];
        byte failed;
        fixed (byte* buffer = payload)
        {
            if (_getApiCallResult(_pipe, call, buffer, (int)size, callbackId, &failed) == 0)
            {
                failed = 1;
            }
        }

        return new SteamCallback(callbackId, call, failed != 0, failed != 0 ? [] : payload);
    }

    private static string ReadMessage(byte* message)
    {
        var length = new ReadOnlySpan<byte>(message, ErrMsgSize).IndexOf((byte)0);
        return Encoding.UTF8.GetString(message, length < 0 ? ErrMsgSize : length);
    }

    // Declared at the larger (Windows) size so the library never writes past it under either packing.
    [StructLayout(LayoutKind.Sequential)]
    private struct CallbackMsg
    {
        public int SteamUser;
        public int Callback;
        public byte* Param;
        public int ParamSize;
    }
}
