using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using MordasGearhold.Core;
using MordasGearhold.Patches;

namespace MordasGearhold.UI
{
    /// <summary>The "YOUR VIKING" text: vital values, equipment modifiers and every damage resistance.</summary>
    internal static class CharacterSheet
    {
        private const string Head = "#ffc96e";
        private const string Muted = "#9a907e";
        private const string Good = "#8fd16a";
        private const string Bad = "#e06a5a";

        private static readonly (HitData.DamageType type, string name)[] s_damageTypes =
        {
            (HitData.DamageType.Blunt, "Blunt"), (HitData.DamageType.Slash, "Slash"), (HitData.DamageType.Pierce, "Pierce"),
            (HitData.DamageType.Fire, "Fire"), (HitData.DamageType.Frost, "Frost"), (HitData.DamageType.Lightning, "Lightning"),
            (HitData.DamageType.Poison, "Poison"), (HitData.DamageType.Spirit, "Spirit"),
        };

        private static readonly StringBuilder s_sb = new StringBuilder(1024);

        internal static string Build(Player player)
        {
            StringBuilder sb = s_sb.Clear();
            Inventory inventory = player.GetInventory();

            // the set number is on the tabs; health, stamina and eitr are on the vanilla HUD
            Row(sb, "Armor", player.GetBodyArmor().ToString("0"));
            Row(sb, "Weight", inventory.GetTotalWeight().ToString("0") + " / " + player.GetMaxCarryWeight().ToString("0"));

            // equipment modifiers: only the ones that are not zero
            Percent(sb, "Movement", player.GetEquipmentMovementModifier(), higherIsBetter: true);
            Percent(sb, "Heat resist.", player.GetEquipmentHeatResistanceModifier(), higherIsBetter: true);
            Percent(sb, "Run stamina", player.GetEquipmentRunStaminaModifier(), higherIsBetter: false);
            Percent(sb, "Attack stamina", player.GetEquipmentAttackStaminaModifier(), higherIsBetter: false);
            Percent(sb, "Block stamina", player.GetEquipmentBlockStaminaModifier(), higherIsBetter: false);
            Percent(sb, "Dodge stamina", player.GetEquipmentDodgeStaminaModifier(), higherIsBetter: false);
            Percent(sb, "Jump stamina", player.GetEquipmentJumpStaminaModifier(), higherIsBetter: false);
            Percent(sb, "Swim stamina", player.GetEquipmentSwimStaminaModifier(), higherIsBetter: false);
            Percent(sb, "Sneak stamina", player.GetEquipmentSneakStaminaModifier(), higherIsBetter: false);

            AppendWeapon(sb, player);
            AppendShield(sb, player);
            AppendTrinkets(sb, player);
            AppendUtilities(sb, player);

            // armour + status effects, the same values the game uses when you are hit; "normal" ones are left out
            HitData.DamageModifiers mods = player.GetDamageModifiers();
            bool header = false;
            foreach ((HitData.DamageType type, string name) in s_damageTypes)
            {
                HitData.DamageModifier modifier = mods.GetModifier(type);
                if (modifier == HitData.DamageModifier.Normal)
                {
                    continue;
                }
                if (!header)
                {
                    sb.Append("\n<color=").Append(Head).Append(">RESISTANCES</color>\n");
                    header = true;
                }
                Row(sb, name, Describe(modifier));
            }
            return sb.ToString();
        }

        private static readonly string[] s_damageNames = { "Blunt", "Slash", "Pierce", "Chop", "Pickaxe", "Fire", "Frost", "Lightning", "Poison", "Spirit" };

        /// <summary>The weapon in hand, with the same base values the vanilla item tooltip shows.</summary>
        private static void AppendWeapon(StringBuilder sb, Player player)
        {
            ItemDrop.ItemData left = Access.LeftItem(player);
            ItemDrop.ItemData weapon = Access.RightItem(player);
            if (weapon == null && left != null && left.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Shield
                && left.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Torch)
            {
                weapon = left; // bows and left-handed two-handers sit in the left hand
            }
            if (weapon == null)
            {
                return; // nothing drawn: no section at all
            }
            Header(sb, "WEAPON");

            HitData.DamageTypes d = weapon.GetDamage();
            float[] values = { d.m_blunt, d.m_slash, d.m_pierce, d.m_chop, d.m_pickaxe, d.m_fire, d.m_frost, d.m_lightning, d.m_poison, d.m_spirit };
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] > 0f)
                {
                    Row(sb, s_damageNames[i], values[i].ToString("0"));
                }
            }
            Attack attack = weapon.m_shared.m_attack;
            if (attack != null && attack.m_attackStamina > 0f)
            {
                Row(sb, "Stamina use", attack.m_attackStamina.ToString("0"));
            }
            if (attack != null && attack.m_attackEitr > 0f)
            {
                Row(sb, "Eitr use", attack.m_attackEitr.ToString("0"));
            }
            AppendBlock(sb, weapon);
            if (weapon.m_shared.m_attackForce > 0f)
            {
                Row(sb, "Knockback", weapon.m_shared.m_attackForce.ToString("0"));
            }
            if (weapon.m_shared.m_backstabBonus > 1f)
            {
                Row(sb, "Backstab", weapon.m_shared.m_backstabBonus.ToString("0.#") + "x");
            }
            if (weapon.m_shared.m_useDurability)
            {
                Row(sb, "Durability", weapon.m_durability.ToString("0") + " / " + weapon.GetMaxDurability().ToString("0"));
            }
        }

        private static void AppendShield(StringBuilder sb, Player player)
        {
            ItemDrop.ItemData shield = Access.LeftItem(player);
            if (shield == null || shield.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Shield)
            {
                return;
            }
            Header(sb, "SHIELD");
            AppendBlock(sb, shield);
        }

        /// <summary>
        /// What the active trinkets do: their summed max adrenaline and each effect with the game's own effect
        /// description (StatusEffect.GetTooltipString), for the vanilla trinket and the two extra ones.
        /// </summary>
        private static void AppendTrinkets(StringBuilder sb, Player player)
        {
            List<ItemDrop.ItemData> active = new List<ItemDrop.ItemData>(3);
            ItemDrop.ItemData main = Access.TrinketItem(player);
            if (main != null)
            {
                active.Add(main);
            }
            active.AddRange(Trinkets.ActiveExtras(player).FindAll(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trinket));
            if (active.Count == 0)
            {
                return;
            }
            Header(sb, "TRINKETS");
            float maxAdrenaline = 0f;
            foreach (ItemDrop.ItemData item in active)
            {
                maxAdrenaline += item.m_shared.m_maxAdrenaline;
            }
            if (maxAdrenaline != 0f)
            {
                Row(sb, "Max adrenaline", "<color=" + Good + ">" + maxAdrenaline.ToString("+0;-0") + "</color>");
            }
            foreach (ItemDrop.ItemData item in active)
            {
                Effect(sb, item.m_shared.m_fullAdrenalineSE, "at full adrenaline");
                Effect(sb, item.m_shared.m_equipStatusEffect, "while worn");
            }
        }

        /// <summary>What the worn utility items do (belt, wishbone, ...): their while-worn effects.</summary>
        private static void AppendUtilities(StringBuilder sb, Player player)
        {
            List<ItemDrop.ItemData> active = new List<ItemDrop.ItemData>(3);
            ItemDrop.ItemData main = Access.UtilityItem(player);
            if (main != null)
            {
                active.Add(main);
            }
            active.AddRange(Trinkets.ActiveExtras(player).FindAll(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Utility));
            if (!active.Exists(i => i.m_shared.m_equipStatusEffect != null))
            {
                return;
            }
            Header(sb, "UTILITY");
            foreach (ItemDrop.ItemData item in active)
            {
                Effect(sb, item.m_shared.m_equipStatusEffect, "while worn");
            }
        }

        private static readonly Regex s_tags = new Regex("<.*?>");

        private static void Effect(StringBuilder sb, StatusEffect effect, string when)
        {
            if (effect == null)
            {
                return;
            }
            sb.Append("<color=").Append(Head).Append('>').Append(Clip(Localization.instance.Localize(effect.m_name), 22))
              .Append("</color> <color=").Append(Muted).Append('>').Append(when).Append("</color>\n");
            string tooltip = Localization.instance.Localize(effect.GetTooltipString() ?? "");
            int lines = 0;
            foreach (string raw in tooltip.Split('\n'))
            {
                string line = s_tags.Replace(raw, "").Trim();
                if (line.Length == 0)
                {
                    continue;
                }
                if (++lines > 4)
                {
                    break;
                }
                sb.Append("  ").Append(Clip(line, 34)).Append('\n');
            }
        }

        private static string Clip(string text, int max) => text.Length <= max ? text : text.Substring(0, max - 1) + "…";

        private static void Header(StringBuilder sb, string title)
        {
            sb.Append("\n<color=").Append(Head).Append('>').Append(title).Append("</color>\n");
        }

        private static void AppendBlock(StringBuilder sb, ItemDrop.ItemData item)
        {
            float block = item.GetBaseBlockPower(item.m_quality);
            if (block > 0f)
            {
                Row(sb, "Block armor", block.ToString("0"));
                Row(sb, "Block force", item.GetDeflectionForce(item.m_quality).ToString("0"));
            }
            if (item.m_shared.m_timedBlockBonus > 1f)
            {
                Row(sb, "Parry bonus", item.m_shared.m_timedBlockBonus.ToString("0.#") + "x");
            }
        }

        private static void Row(StringBuilder sb, string label, string value)
        {
            sb.Append(label).Append("<pos=58%>").Append(value).Append('\n');
        }

        private static void Percent(StringBuilder sb, string label, float value, bool higherIsBetter)
        {
            if (value > -0.0005f && value < 0.0005f)
            {
                return;
            }
            bool good = higherIsBetter ? value > 0f : value < 0f;
            Row(sb, label, "<color=" + (good ? Good : Bad) + ">" + (value * 100f).ToString("+0;-0") + "%</color>");
        }

        private static string Describe(HitData.DamageModifier modifier)
        {
            switch (modifier)
            {
                case HitData.DamageModifier.Immune: return "<color=" + Good + ">Immune</color>";
                case HitData.DamageModifier.Ignore: return "<color=" + Good + ">Ignored</color>";
                case HitData.DamageModifier.VeryResistant: return "<color=" + Good + ">Very resistant</color>";
                case HitData.DamageModifier.Resistant: return "<color=" + Good + ">Resistant</color>";
                case HitData.DamageModifier.SlightlyResistant: return "<color=" + Good + ">Slightly resistant</color>";
                case HitData.DamageModifier.SlightlyWeak: return "<color=" + Bad + ">Slightly weak</color>";
                case HitData.DamageModifier.Weak: return "<color=" + Bad + ">Weak</color>";
                case HitData.DamageModifier.VeryWeak: return "<color=" + Bad + ">Very weak</color>";
                default: return "<color=" + Muted + ">Normal</color>";
            }
        }
    }
}
