using System;
using System.Collections.Generic;

namespace DeadAir_7LongDarkDays.Patches
{
    public class ConsoleCmdDeadAirQuestTier : ConsoleCmdAbstract
    {
        public override string[] getCommands()
        {
            return new[] { "daqt", "deadairquesttier" };
        }

        public override string getDescription()
        {
            return "Sets or clears DeadAir debug forced quest tier. Usage: daqt <0-6>";
        }

        public override string getHelp()
        {
            return
                "Usage:\n" +
                "  daqt 0   -> disable forced tier\n" +
                "  daqt 4   -> force tier 4 behavior\n" +
                "  daqt 5   -> force tier 5 behavior\n" +
                "  daqt 6   -> force tier 6 behavior\n";
        }

        public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
        {
            try
            {
                if (_params == null || _params.Count < 1)
                {
                    SdtdConsole.Instance.Output($"[DeadAir] Forced quest tier = {DeadAirDebugQuestTier.ForcedQuestTier}");
                    return;
                }

                if (!int.TryParse(_params[0], out int tier))
                {
                    SdtdConsole.Instance.Output("[DeadAir] Invalid tier. Use 0-6.");
                    return;
                }

                if (tier < 0) tier = 0;
                if (tier > 6) tier = 6;

                if (tier == 0)
                {
                    DeadAirDebugQuestTier.Clear();
                    SdtdConsole.Instance.Output("[DeadAir] Forced quest tier disabled.");
                }
                else
                {
                    DeadAirDebugQuestTier.SetForcedTier(tier);
                    SdtdConsole.Instance.Output($"[DeadAir] Forced quest tier = {tier}");
                }
            }
            catch (Exception e)
            {
                SdtdConsole.Instance.Output($"[DeadAir] daqt error: {e}");
            }
        }
    }
}
