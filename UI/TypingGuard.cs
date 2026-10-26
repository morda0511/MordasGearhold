using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace MordasGearhold.UI
{
    /// <summary>
    /// While a Gearhold text box (a preset name) has the keyboard, the game must not see the keys as commands:
    /// E would close the menu, Tab the inventory, and other keys act on the character. The game reads its keys
    /// through ZInput; those answer "not pressed" for as long as the box is focused. The text itself reaches the
    /// box through Unity's own input, which this does not touch.
    /// </summary>
    internal static class TypingGuard
    {
        private static bool s_typing;

        internal static bool Typing => s_typing;

        /// <summary>Called every frame by the look menu: drops the flag when the box lost focus without telling (it was destroyed).</summary>
        private static float s_unfocusedSince = -1f;

        internal static void Tick()
        {
            if (!s_typing || Plugin.TextFieldFocused())
            {
                s_unfocusedSince = -1f;
                return;
            }
            if (s_unfocusedSince < 0f) s_unfocusedSince = UnityEngine.Time.unscaledTime;
            else if (UnityEngine.Time.unscaledTime - s_unfocusedSince > 0.4f) s_typing = false; // the box is gone without telling
        }

        internal static void Begin() => s_typing = true;

        internal static void End() => s_typing = false;

        [HarmonyPatch]
        private static class ZInput_Keys_Patch
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                return typeof(ZInput).GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Where(m => m.ReturnType == typeof(bool) && (m.Name.StartsWith("GetButton") || m.Name.StartsWith("GetKey")))
                    .Cast<MethodBase>();
            }

            private static bool Prefix(ref bool __result)
            {
                if (!s_typing)
                {
                    return true;
                }
                __result = false;
                return false;
            }
        }
    }
}
