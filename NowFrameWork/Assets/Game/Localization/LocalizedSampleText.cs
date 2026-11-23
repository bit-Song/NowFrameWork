using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

namespace Game.Localization
{
    [RequireComponent(typeof(Text))]
    public sealed class LocalizedSampleText : MonoBehaviour
    {
        readonly LocalizedString _heading = new LocalizedString(LanguageSelection.Table, "panel.heading")
            { Arguments = new object[] { 3 } };
        readonly LocalizedString _hint = new LocalizedString(LanguageSelection.Table, "panel.fallback")
            { FallbackState = FallbackBehavior.UseFallback };

        Text _text;
        string _title;
        string _detail;

        void OnEnable()
        {
            _text = GetComponent<Text>();
            _title = _detail = null;
            _text.text = "";
            _heading.StringChanged += SetHeading;
            _hint.StringChanged += SetHint;
        }

        void OnDisable()
        {
            _heading.StringChanged -= SetHeading;
            _hint.StringChanged -= SetHint;
        }

        void SetHeading(string value) { _title = value; Refresh(); }
        void SetHint(string value) { _detail = value; Refresh(); }
        void Refresh() => _text.text = _title + "\n" + _detail;
    }
}
