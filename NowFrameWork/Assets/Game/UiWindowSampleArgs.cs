using System;
using UnityEngine;

namespace Game
{
    public sealed class UiWindowSampleArgs
    {
        public string Title;
        public Color Color;
        public bool Fullscreen;
        public Action Next, Settings, Confirm, Back;
    }
}
