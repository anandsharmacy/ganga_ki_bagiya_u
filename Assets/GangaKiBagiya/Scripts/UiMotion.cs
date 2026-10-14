using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace GangaKiBagiya
{
    /// <summary>Fade/slide/scale helpers built on UI Toolkit style transitions. Everything collapses to instant under Reduced motion.</summary>
    public static class UiMotion
    {
        public const float Fast = 0.15f;
        public const float Normal = 0.28f;
        public const float Slow = 0.42f;

        public static float Duration(float seconds)
        {
            return UiSettings.ReducedMotion ? 0f : seconds;
        }

        public static void SetTransition(VisualElement element, float seconds, params string[] properties)
        {
            var names = new List<StylePropertyName>(properties.Length);
            foreach (string property in properties)
                names.Add(new StylePropertyName(property));
            element.style.transitionProperty = names;
            element.style.transitionDuration = new List<TimeValue> { new TimeValue(Duration(seconds), TimeUnit.Second) };
            element.style.transitionTimingFunction = new List<EasingFunction> { new EasingFunction(EasingMode.EaseOut) };
        }

        /// <summary>Fades/slides/scales an element in. Safe to call before the element is attached.</summary>
        public static void Appear(VisualElement element, float delay = 0f, float rise = 14f, float fromScale = 1f, float seconds = Normal)
        {
            if (UiSettings.ReducedMotion)
            {
                Rest(element);
                return;
            }

            SetTransition(element, 0f, "opacity", "translate", "scale");
            element.style.opacity = 0f;
            element.style.translate = new Translate(0f, rise);
            element.style.scale = new Scale(new Vector3(fromScale, fromScale, 1f));
            void Go() => element.schedule.Execute(() =>
            {
                SetTransition(element, seconds, "opacity", "translate", "scale");
                Rest(element);
            }).StartingIn((long)(Mathf.Max(0.03f, delay) * 1000f));
            if (element.panel != null)
                Go();
            else
                element.RegisterCallbackOnce<AttachToPanelEvent>(_ => Go());
        }

        public static void Rest(VisualElement element)
        {
            element.style.opacity = 1f;
            element.style.translate = new Translate(0f, 0f);
            element.style.scale = new Scale(Vector3.one);
        }

        /// <summary>Fades out, then invokes <paramref name="done"/> (immediately under Reduced motion).</summary>
        public static void Disappear(VisualElement element, float seconds, Action done, float drop = 0f, float toScale = 1f)
        {
            if (UiSettings.ReducedMotion || element.panel == null)
            {
                done?.Invoke();
                return;
            }
            SetTransition(element, seconds, "opacity", "translate", "scale");
            element.style.opacity = 0f;
            element.style.translate = new Translate(0f, drop);
            element.style.scale = new Scale(new Vector3(toScale, toScale, 1f));
            element.schedule.Execute(() => done?.Invoke()).StartingIn((long)(seconds * 1000f) + 20);
        }

        public static void Stagger(IList<VisualElement> elements, float step, float firstDelay = 0f)
        {
            for (int i = 0; i < elements.Count; i++)
                Appear(elements[i], firstDelay + step * i);
        }

        /// <summary>Brief scale pop (e.g. a changed counter).</summary>
        public static void Pop(VisualElement element, float peak = 1.12f)
        {
            if (UiSettings.ReducedMotion || element.panel == null)
                return;
            SetTransition(element, 0.09f, "scale");
            element.style.scale = new Scale(new Vector3(peak, peak, 1f));
            element.schedule.Execute(() => element.style.scale = new Scale(Vector3.one)).StartingIn(90);
        }
    }
}
