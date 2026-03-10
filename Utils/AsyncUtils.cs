using System;
using System.Threading.Tasks;

namespace MTFVoiceTools.Utils;

public static class AsyncUtils
{
    public static async Task<T> SupressCancelationThrow<T>(this Task<T> task)
    {
        T result = default;
        try
        {
            result = await task;
        }
        catch (OperationCanceledException)
        {

        }
        
        return result;
    }
}