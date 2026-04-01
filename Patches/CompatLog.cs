using UnityEngine;

public static class CompatLog
{
    public static void Out(string msg)
    {
        Debug.Log(msg);
    }

    public static void Warning(string msg)
    {
        Debug.LogWarning(msg);
    }

    public static void Error(string msg)
    {
        Debug.LogError(msg);
    }
}