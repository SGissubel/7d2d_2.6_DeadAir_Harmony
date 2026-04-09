using HarmonyLib;
using UnityEngine;
using Audio;

namespace DeadAir_7LongDarkDays.Patches
{
    [HarmonyPatch(typeof(ItemActionAttack), nameof(ItemActionAttack.Hit))]
    public static class Patch_ItemActionAttack_Hit_HeadClang
    {
        public static void Postfix(
            WorldRayHitInfo hitInfo,
            int _attackerEntityId,
            EnumDamageTypes _damageType,
            float _blockDamage,
            float _entityDamage,
            float _staminaDamageMultiplier,
            float _weaponCondition,
            float _criticalHitChanceOLD,
            float _dismemberChance,
            string _attackingDeviceMadeOf,
            DamageMultiplier _damageMultiplier,
            System.Collections.Generic.List<string> _buffActions,
            ItemActionAttack.AttackHitInfo _attackDetails,
            int _flags = 1,
            int _actionExp = 0,
            float _actionExpBonus = 0f,
            ItemActionAttack rangeCheckedAction = null,
            System.Collections.Generic.Dictionary<string, ItemActionAttack.Bonuses> _toolBonuses = null,
            ItemActionAttack.EnumAttackMode _attackMode = ItemActionAttack.EnumAttackMode.RealNoHarvesting,
            System.Collections.Generic.Dictionary<string, string> _hitSoundOverrides = null,
            int ownedEntityId = -1,
            ItemValue damagingItemValue = null
        )
        {
            try
            {
                if (hitInfo == null || string.IsNullOrEmpty(hitInfo.tag) || !hitInfo.tag.StartsWith("E_"))
                    return;

                string bodyPartName;
                Entity entity = ItemActionAttack.FindHitEntityNoTagCheck(hitInfo, out bodyPartName);
                if (entity is not EntityAlive target)
                    return;

                // Soldier-only for now
                if (target.EntityName != "zombieSoldier"
                    && target.EntityName != "zombieDemolition"
                    && target.EntityName != "zombieUtilityWorker"
                    && target.EntityName != "zombieBiker")
                    return;

                // Skip if helmet already broken
                if (target.Buffs != null && target.Buffs.HasBuff("buffDA_HelmetBroken"))
                    return;

                EnumBodyPartHit hitPart = target.RecordedDamage.HitBodyPart;

                // Head-only clang
                if ((hitPart & EnumBodyPartHit.Head) == EnumBodyPartHit.None)
                    return;

                Entity attacker = GameManager.Instance.World.GetEntity(_attackerEntityId);
                if (attacker is not EntityPlayer)
                    return;

                target.PlayOneShot("bullethitmetal");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[DeadAir] Head clang postfix failed: {ex}");
            }
        }
    }
}
