using HarmonyLib;

[HarmonyPatch(typeof(GameManager), "StartGame")]
public class Patch_Test
{
    static void Postfix()
    {
        CompatLog.Out("[DeadAir] Patch_Test fired (StartGame).");
    }
}
