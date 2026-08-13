using Steamworks;

namespace Civ6WorkshopUploader;

public class SteamCallResult<T> : IDisposable where T : struct
{
    private readonly TaskCompletionSource<T> _completionSource = new();
    private readonly CallResult<T> _callResult;

    public Task<T> Task => _completionSource.Task;

    public SteamCallResult(SteamAPICall_t call)
    {
        _callResult = CallResult<T>.Create(OnCallResult);
        _callResult.Set(call);
    }

    private void OnCallResult(T result, bool ioError)
    {
        if (ioError)
        {
            _completionSource.SetException(new IOException($"Got IO failure from CallResult of type {typeof(T)}!"));
        }
        else
        {
            _completionSource.SetResult(result);
        }
    }

    public void Dispose()
    {
        _callResult.Dispose();
    }
}