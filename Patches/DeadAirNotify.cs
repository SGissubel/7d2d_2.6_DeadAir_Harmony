using System;
using System.Reflection;

/// <summary>Player-facing feedback (best-effort; always logs).</summary>
namespace DeadAir_7LongDarkDays.Patches
{
    public static class DeadAirNotify
    {
        public static void Msg(EntityPlayerLocal player, string localizationKey)
        {
            var text = Localization.Get(localizationKey);
            if (string.IsNullOrEmpty(text))
            {
                text = localizationKey;
            }

            CompatLog.Out("[DeadAir] " + text);

            if (player == null)
            {
                return;
            }

            TryGameMessage(player, text);
        }

        static void TryGameMessage(EntityPlayerLocal player, string text)
        {
            try
            {
                var gm = GameManager.Instance;
                if (gm == null)
                {
                    return;
                }

                var gmType = typeof(GameManager);
                foreach (var m in gmType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (m.Name != "GameMessage")
                    {
                        continue;
                    }

                    var ps = m.GetParameters();
                    if (ps.Length == 1 && ps[0].ParameterType == typeof(string))
                    {
                        m.Invoke(gm, new object[] { text });
                        return;
                    }

                    if (ps.Length == 2 && ps[0].ParameterType.IsAssignableFrom(typeof(EntityPlayer)) && ps[1].ParameterType == typeof(string))
                    {
                        m.Invoke(gm, new object[] { player, text });
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                CompatLog.Warning("[DeadAir] Notify GameMessage failed: " + ex.Message);
            }
        }
    }
}