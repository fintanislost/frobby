namespace SdvTestFramework.Harness.Rpc;

/// <summary>
/// Drains queued RPC callbacks from any SMAPI event that runs on the game thread.
/// Some active-menu states continue rendering while update ticks are sparse or paused.
/// Pre-render draining lets draw/text capture arms take effect for the frame about to
/// draw; post-render draining remains a backup for requests queued during draw.
/// </summary>
public sealed class GameThreadRpcPump
{
    private readonly GameThreadDispatch _dispatch;

    public GameThreadRpcPump(GameThreadDispatch dispatch)
    {
        _dispatch = dispatch;
    }

    public void OnUpdateTicked() => _dispatch.Drain();

    public void OnRendering() => _dispatch.Drain();

    public void OnRendered() => _dispatch.Drain();
}
