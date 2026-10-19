using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MordasGearhold.UI
{
    /// <summary>
    /// Live 3D view of your own character for the look/colour menu.
    ///  - Only the character: just while the preview camera renders (Camera.onPreCull .. onPostRender) the
    ///    character's renderers are moved to an unused layer, and the camera only sees that layer. The game's
    ///    own camera never sees the change. (0.13.x rendered every layer the character used, which included the
    ///    world's default layer, so walls and floor showed up.)
    ///  - Drag with the mouse to turn around the character, mouse wheel to zoom.
    /// Active only while the menu is open.
    /// </summary>
    internal static class CharacterPreview
    {
        private const int TextureWidth = 512;
        private const int TextureHeight = 768;
        private const float MinDistance = 1.6f;
        private const float MaxDistance = 4.5f;

        private static Camera s_camera;
        private static RenderTexture s_texture;
        private static int s_layer = -1;
        private static float s_yaw;
        private static float s_distance = 3.4f;

        private static readonly List<Renderer> s_renderers = new List<Renderer>();
        private static readonly List<int> s_layers = new List<int>();
        private static float s_nextScan;

        /// <summary>The image that shows the preview, placed like the other menu parts (design coordinates).</summary>
        internal static RawImage CreateView(Transform parent, float x, float y, float width, float height)
        {
            EnsureTexture();
            RectTransform rect = UiKit.Rect("CharacterPreview", parent);
            UiKit.Place(rect, x, y, width, height);
            RawImage image = rect.gameObject.AddComponent<RawImage>();
            image.texture = s_texture;
            image.raycastTarget = true; // receives drag / scroll
            float aspect = TextureWidth / (float)TextureHeight;
            if (width / height > aspect) rect.sizeDelta = new Vector2(height * aspect, height);
            else rect.sizeDelta = new Vector2(width, width / aspect);
            rect.gameObject.AddComponent<PreviewControls>();
            return image;
        }

        internal static void Begin()
        {
            EnsureTexture();
            if (s_layer < 0)
            {
                s_layer = FindFreeLayer();
            }
            if (s_camera == null)
            {
                GameObject go = new GameObject("MordasGearholdPreviewCamera");
                Object.DontDestroyOnLoad(go);
                s_camera = go.AddComponent<Camera>();
                s_camera.clearFlags = CameraClearFlags.SolidColor;
                s_camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                s_camera.fieldOfView = 34f;
                s_camera.nearClipPlane = 0.1f;
                s_camera.farClipPlane = 12f;
                s_camera.depth = -50f;
                s_camera.allowHDR = false;
                s_camera.targetTexture = s_texture;
                s_camera.cullingMask = 1 << s_layer;
                Camera.onPreCull += OnPreCull;
                Camera.onPostRender += OnPostRender;
            }
            s_nextScan = 0f;
            s_camera.enabled = true;
            Tick();
        }

        internal static void End()
        {
            if (s_camera != null)
            {
                s_camera.enabled = false;
            }
        }

        internal static void Rotate(float degrees) => s_yaw += degrees;

        internal static void Zoom(float delta) => s_distance = Mathf.Clamp(s_distance - delta * 0.4f, MinDistance, MaxDistance);

        /// <summary>Every frame while the menu is open: keep the camera in front of the (moving, animated) player.</summary>
        internal static void Tick()
        {
            Player player = Player.m_localPlayer;
            if (s_camera == null || !s_camera.enabled)
            {
                return;
            }
            if (player == null)
            {
                End();
                return;
            }
            Transform t = player.transform;
            Vector3 focus = t.position + Vector3.up * 1.0f;
            Vector3 direction = Quaternion.Euler(0f, s_yaw, 0f) * t.forward;
            s_camera.transform.position = focus + direction * s_distance + Vector3.up * 0.25f;
            s_camera.transform.LookAt(focus);
            if (Time.unscaledTime >= s_nextScan)
            {
                // new gear brings new renderers: look again now and then
                s_nextScan = Time.unscaledTime + 0.5f;
                s_renderers.Clear();
                player.GetComponentsInChildren(false, s_renderers);
            }
        }

        private static void OnPreCull(Camera camera)
        {
            if (camera != s_camera || s_layer < 0)
            {
                return;
            }
            s_layers.Clear();
            foreach (Renderer renderer in s_renderers)
            {
                if (renderer == null)
                {
                    s_layers.Add(-1);
                    continue;
                }
                s_layers.Add(renderer.gameObject.layer);
                renderer.gameObject.layer = s_layer;
            }
        }

        private static void OnPostRender(Camera camera)
        {
            if (camera != s_camera)
            {
                return;
            }
            for (int i = 0; i < s_renderers.Count && i < s_layers.Count; i++)
            {
                if (s_renderers[i] != null && s_layers[i] >= 0)
                {
                    s_renderers[i].gameObject.layer = s_layers[i];
                }
            }
            s_layers.Clear();
        }

        /// <summary>A layer no one named (and so no one uses), from the top down.</summary>
        private static int FindFreeLayer()
        {
            for (int i = 31; i >= 8; i--)
            {
                if (string.IsNullOrEmpty(LayerMask.LayerToName(i)))
                {
                    return i;
                }
            }
            Plugin.Log.LogWarning("Gearhold preview: no free layer found, the preview may show the surroundings.");
            return 31;
        }

        private static void EnsureTexture()
        {
            if (s_texture == null)
            {
                s_texture = new RenderTexture(TextureWidth, TextureHeight, 24, RenderTextureFormat.ARGB32)
                {
                    name = "MordasGearholdPreview",
                    antiAliasing = 2,
                };
            }
        }
    }

    /// <summary>Drag on the preview to turn around the character, mouse wheel to zoom.</summary>
    internal sealed class PreviewControls : MonoBehaviour, IDragHandler, IScrollHandler
    {
        public void OnDrag(PointerEventData eventData) => CharacterPreview.Rotate(eventData.delta.x * 0.6f);

        public void OnScroll(PointerEventData eventData) => CharacterPreview.Zoom(eventData.scrollDelta.y);
    }
}

namespace MordasGearhold.UI
{
    /// <summary>Swallows clicks so they do not travel up to a parent's click handler.</summary>
    internal sealed class ClickSink : UnityEngine.MonoBehaviour, UnityEngine.EventSystems.IPointerClickHandler
    {
        public void OnPointerClick(UnityEngine.EventSystems.PointerEventData eventData)
        {
        }
    }
}
