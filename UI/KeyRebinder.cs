using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace MordasGearhold.UI
{
    /// <summary>
    /// In-panel hotkey editor for the quickslots: click the button under a quickslot, then press a key
    /// (optionally with Shift / Ctrl / Alt). Esc cancels, Delete or Backspace clears the binding.
    /// The result is written to the BepInEx config, so it also shows up in the config file.
    /// </summary>
    internal static class KeyRebinder
    {
        private static readonly KeyCode[] s_modifiers =
        {
            KeyCode.LeftShift, KeyCode.RightShift, KeyCode.LeftControl, KeyCode.RightControl, KeyCode.LeftAlt, KeyCode.RightAlt,
        };

        // keyboard keys only: no mouse buttons (the click that started rebinding) and no joystick buttons
        private static readonly KeyCode[] s_candidates = ((KeyCode[])Enum.GetValues(typeof(KeyCode)))
            .Where(k => k != KeyCode.None && k < KeyCode.Mouse0 && !s_modifiers.Contains(k))
            .Distinct().ToArray();

        // vanilla default bindings (ZInput) that a quickslot key would shadow
        private static readonly Dictionary<KeyCode, string> s_vanilla = new Dictionary<KeyCode, string>
        {
            { KeyCode.E, "Use" }, { KeyCode.R, "Hide weapons" }, { KeyCode.Space, "Jump" }, { KeyCode.C, "Walk" },
            { KeyCode.Q, "Auto-run" }, { KeyCode.X, "Sit" }, { KeyCode.F, "Forsaken power" }, { KeyCode.Tab, "Inventory" },
            { KeyCode.M, "Map" }, { KeyCode.V, "Auto-pickup" }, { KeyCode.G, "Radial menu" }, { KeyCode.T, "Emotes" },
            { KeyCode.Return, "Chat" }, { KeyCode.F5, "Console" }, { KeyCode.W, "Forward" }, { KeyCode.A, "Left" },
            { KeyCode.S, "Backward" }, { KeyCode.D, "Right" }, { KeyCode.Comma, "Map zoom" }, { KeyCode.Period, "Map zoom" },
            { KeyCode.Alpha1, "Hotbar 1" }, { KeyCode.Alpha2, "Hotbar 2" }, { KeyCode.Alpha3, "Hotbar 3" }, { KeyCode.Alpha4, "Hotbar 4" },
            { KeyCode.Alpha5, "Hotbar 5" }, { KeyCode.Alpha6, "Hotbar 6" }, { KeyCode.Alpha7, "Hotbar 7" }, { KeyCode.Alpha8, "Hotbar 8" },
        };

        private static ConfigEntry<KeyboardShortcut> s_target;
        private static int s_startFrame;

        internal static bool IsWaitingFor(ConfigEntry<KeyboardShortcut> entry) => s_target != null && s_target == entry;

        internal static bool Active => s_target != null;

        internal static void Begin(ConfigEntry<KeyboardShortcut> entry)
        {
            s_target = s_target == entry ? null : entry; // a second click on the same button cancels
            s_startFrame = Time.frameCount;
        }

        /// <summary>Returns true while it is capturing, so no hotkey fires with the same key press.</summary>
        internal static bool Tick()
        {
            if (s_target == null)
            {
                return false;
            }
            if (!InventoryGui.IsVisible() || Player.m_localPlayer == null)
            {
                s_target = null;
                return false;
            }
            if (Time.frameCount == s_startFrame)
            {
                return true;
            }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                s_target = null;
                return true;
            }
            if (Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace))
            {
                Assign(KeyboardShortcut.Empty);
                return true;
            }
            foreach (KeyCode key in s_candidates)
            {
                if (Input.GetKeyDown(key))
                {
                    KeyCode[] held = s_modifiers.Where(Input.GetKey).ToArray();
                    Assign(new KeyboardShortcut(key, held));
                    return true;
                }
            }
            return true;
        }

        private static void Assign(KeyboardShortcut shortcut)
        {
            ConfigEntry<KeyboardShortcut> target = s_target;
            s_target = null;
            Player player = Player.m_localPlayer;
            if (shortcut.MainKey != KeyCode.None)
            {
                // one key, one action inside Gearhold: take it away from any other Gearhold hotkey
                foreach (ConfigEntry<KeyboardShortcut> other in AllHotkeys())
                {
                    if (other != target && Same(other.Value, shortcut))
                    {
                        other.Value = KeyboardShortcut.Empty;
                        player?.Message(MessageHud.MessageType.Center, $"{Plugin.FormatKey(shortcut)} was removed from {other.Definition.Key}");
                    }
                }
                if (shortcut.Modifiers.Count() == 0 && s_vanilla.TryGetValue(shortcut.MainKey, out string action))
                {
                    player?.Message(MessageHud.MessageType.Center, $"Note: {Plugin.FormatKey(shortcut)} is also vanilla \"{action}\"");
                }
            }
            target.Value = shortcut;
        }

        private static IEnumerable<ConfigEntry<KeyboardShortcut>> AllHotkeys() => Plugin.AllHotkeys();

        private static bool Same(KeyboardShortcut a, KeyboardShortcut b)
        {
            return a.MainKey == b.MainKey && a.Modifiers.OrderBy(k => k).SequenceEqual(b.Modifiers.OrderBy(k => k));
        }
    }
}
