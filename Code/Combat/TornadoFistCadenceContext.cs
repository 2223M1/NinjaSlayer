namespace NinjaSlayer.Code.Combat;

internal static class TornadoFistCadenceContext
{
    private static readonly AsyncLocal<int> ActiveDepth = new();

    public static bool IsActive => ActiveDepth.Value > 0;

    public static async Task<T> Run<T>(Func<Task<T>> action)
    {
        int previousDepth = ActiveDepth.Value;
        ActiveDepth.Value = previousDepth + 1;
        try
        {
            return await action();
        }
        finally
        {
            ActiveDepth.Value = previousDepth;
        }
    }
}
