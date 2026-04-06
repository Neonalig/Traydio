using System;
using System.Threading.Tasks;

namespace Traydio.Services;

public static class TaskSafetyExtensions
{
    public static void ForgetWithErrorHandling(this Task task, string context, bool showDialog = true)
    {

        _ = ObserveTaskAsync(task, context, showDialog);
    }

    private static async Task ObserveTaskAsync(Task task, string context, bool showDialog)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is expected for some UI/background operations.
        }
        catch (Exception ex)
        {
            AppErrorHandler.Report(ex, context, showDialog);
        }
    }
}

