using Microsoft.UI.Dispatching;

namespace iFlyCompassGUI.Helpers;

public class DispatcherHelper
{
    private readonly DispatcherQueue _dispatcherQueue;
    
    public DispatcherHelper()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }
    
    public void RunOnUIThread(Action action)
    {
        if (_dispatcherQueue.HasThreadAccess)
            action();
        else
            _dispatcherQueue.TryEnqueue(() => action());
    }
}
