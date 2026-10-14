using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace GangaKiBagiya
{
    /// <summary>Design tokens and interactive component styling shared by every screen (Indian folk paper-craft look).</summary>
    public static class UiTheme
    {
        public static readonly Color Kraft = Hex("#F3E3C3");
        public static readonly Color Cream = Hex("#FFF6E0");
        public static readonly Color Ink = Hex("#2B2A33");
        public static readonly Color Terracotta = Hex("#B5532E");
        public static readonly Color Marigold = Hex("#F2A900");
        public static readonly Color RaniPink = Hex("#D81B72");
        public static readonly Color Indigo = Hex("#2C3E8C");
        public static readonly Color RiverBlue = Hex("#3C7FD1");
        public static readonly Color LeafGreen = Hex("#4E9A3F");
        public static readonly Color Muted = Hex("#6E6858");
        public static readonly Color Error = Hex("#B3261E");

        // Folk-motif avatar badge colours.
        public static readonly Color[] Avatars = { Hex("#D81B72"), Hex("#2C3E8C"), Hex("#4E9A3F"), Hex("#F2A900"), Hex("#B5532E"), Hex("#3C7FD1") };

        public const float Space = 8f;
        public const float RadiusButton = 14f;
        public const float RadiusPanel = 18f;
        public const float MinTouch = 48f;

        private static Texture2D scrim;

        public static Color Hex(string html)
        {
            ColorUtility.TryParseHtmlString(html, out Color color);
            return color;
        }

        public static Color Shade(Color color, float amount)
        {
            return amount >= 0f ? Color.Lerp(color, Color.white, amount) : Color.Lerp(color, Color.black, -amount);
        }

        /// <summary>Vertical gradient: translucent at the top (world shows through) to solid kraft at the bottom.</summary>
        public static StyleBackground ScrimBackground(float topAlpha = 0.45f, float fullAt = 0.55f)
        {
            if (scrim == null)
            {
                scrim = new Texture2D(1, 256, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                for (int y = 0; y < 256; y++)
                {
                    float fromTop = 1f - y / 255f;
                    float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(fromTop / fullAt));
                    Color c = Kraft;
                    c.a = Mathf.Lerp(topAlpha, 0.97f, t);
                    scrim.SetPixel(0, y, c);
                }
                scrim.Apply();
            }
            return new StyleBackground(scrim);
        }

        public static void Round(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }

        public static void Border(VisualElement element, float width, Color color)
        {
            element.style.borderTopWidth = width;
            element.style.borderRightWidth = width;
            element.style.borderBottomWidth = width;
            element.style.borderLeftWidth = width;
            element.style.borderTopColor = color;
            element.style.borderRightColor = color;
            element.style.borderBottomColor = color;
            element.style.borderLeftColor = color;
        }

        /// <summary>Paper-card panel: accent border with a thicker, darker bottom edge for depth.</summary>
        public static VisualElement Panel(Color accent)
        {
            var panel = new VisualElement();
            panel.style.backgroundColor = Cream;
            panel.style.paddingLeft = 16f;
            panel.style.paddingRight = 16f;
            panel.style.paddingTop = 12f;
            panel.style.paddingBottom = 12f;
            panel.style.marginBottom = Space;
            Round(panel, RadiusPanel);
            Border(panel, 3f, accent);
            panel.style.borderBottomWidth = 7f;
            panel.style.borderBottomColor = Shade(accent, -0.25f);
            return panel;
        }

        /// <summary>Applies normal/hover/pressed/focus visuals to a button. Disabled is handled by <see cref="SetEnabled"/>.</summary>
        public static void StyleInteractive(Button button, Color background, Color text)
        {
            StyleInteractive(button, () => background, () => text);
        }

        /// <summary>Getter-based variant for selectable controls; the returned action re-applies the current colours.</summary>
        public static Action StyleInteractive(Button button, Func<Color> background, Func<Color> text,
            float borderWidth = 0f, Func<Color> borderColor = null)
        {
            button.style.minHeight = MinTouch;
            button.style.marginLeft = 0f;
            button.style.marginRight = 0f;
            button.focusable = true;
            Round(button, RadiusButton);
            UiMotion.SetTransition(button, UiMotion.Fast, "scale", "background-color");

            bool pressed = false, hover = false, focused = false;
            void Apply()
            {
                Color bg = background();
                button.style.backgroundColor = pressed ? Shade(bg, -0.14f) : hover ? Shade(bg, 0.1f) : bg;
                button.style.color = text();
                button.style.scale = new Scale(pressed ? new Vector3(0.97f, 0.97f, 1f) : Vector3.one);
                Border(button, focused ? 3f : borderWidth, focused ? Marigold : (borderColor != null ? borderColor() : bg));
            }
            button.RegisterCallback<PointerEnterEvent>(_ => { hover = true; Apply(); });
            button.RegisterCallback<PointerLeaveEvent>(_ => { hover = false; pressed = false; Apply(); });
            button.RegisterCallback<PointerDownEvent>(_ => { pressed = true; Apply(); });
            button.RegisterCallback<PointerUpEvent>(_ => { pressed = false; Apply(); });
            button.RegisterCallback<PointerCancelEvent>(_ => { pressed = false; hover = false; Apply(); });
            button.RegisterCallback<FocusInEvent>(e => { focused = e.direction != FocusChangeDirection.unspecified; Apply(); });
            button.RegisterCallback<FocusOutEvent>(_ => { focused = false; Apply(); });
            button.clicked += () => UiAudio.Play(UiCue.Tap);
            Apply();
            return Apply;
        }

        public static void SetEnabled(VisualElement element, bool enabled)
        {
            element.SetEnabled(enabled);
            element.style.opacity = enabled ? 1f : 0.45f;
        }
    }
}
