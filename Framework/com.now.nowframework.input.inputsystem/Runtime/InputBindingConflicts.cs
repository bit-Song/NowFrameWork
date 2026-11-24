using System;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityInputSystem = UnityEngine.InputSystem.InputSystem;

namespace NowFrameWork.Input.InputSystem
{
    public static class InputBindingConflicts
    {
        /// <summary>在目标 Action Map 检查同一绑定组内的有效路径。空绑定组覆盖所有组。</summary>
        public static bool TryFind(InputAction action, int bindingIndex, string candidatePath, InputControl selectedControl,
            out InputAction conflictingAction, out int conflictingBindingIndex)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (bindingIndex < 0 || bindingIndex >= action.bindings.Count)
                throw new ArgumentOutOfRangeException(nameof(bindingIndex));
            var target = action.bindings[bindingIndex];
            if (target.isComposite) throw new ArgumentException("请指定复合绑定的子项。", nameof(bindingIndex));
            if (string.IsNullOrWhiteSpace(candidatePath)) throw new ArgumentException("候选绑定路径不能为空。", nameof(candidatePath));

            using var candidates = UnityInputSystem.FindControls(candidatePath);
            if (action.actionMap != null)
            {
                foreach (var other in action.actionMap.actions)
                {
                    int index = Find(action, bindingIndex, target.groups, other, candidatePath, selectedControl, candidates);
                    if (index < 0) continue;
                    conflictingAction = other;
                    conflictingBindingIndex = index;
                    return true;
                }
            }
            else
            {
                int index = Find(action, bindingIndex, target.groups, action, candidatePath, selectedControl, candidates);
                if (index >= 0)
                {
                    conflictingAction = action;
                    conflictingBindingIndex = index;
                    return true;
                }
            }

            conflictingAction = null;
            conflictingBindingIndex = -1;
            return false;
        }

        static int Find(InputAction target, int targetIndex, string groups, InputAction other, string path, InputControl control,
            InputControlList<InputControl> candidates)
        {
            var bindings = other.bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                if (ReferenceEquals(other, target) && i == targetIndex) continue;
                InputBinding binding = bindings[i];
                if (binding.isComposite || string.IsNullOrEmpty(binding.effectivePath) || !GroupsOverlap(groups, binding.groups)) continue;
                if (PathsOverlap(path, binding.effectivePath, control, candidates)) return i;
            }
            return -1;
        }

        static bool PathsOverlap(string candidate, string existing, InputControl selected, InputControlList<InputControl> controls)
        {
            if (string.Equals(candidate, existing, StringComparison.OrdinalIgnoreCase) ||
                (selected != null && InputControlPath.Matches(existing, selected))) return true;
            if (controls.Count > 0)
            {
                // 由 Input System 解析两条路径，保留设备布局和控件别名的匹配规则。
                using var existingControls = UnityInputSystem.FindControls(existing);
                foreach (var current in controls)
                    foreach (var previous in existingControls)
                        if (ReferenceEquals(current, previous)) return true;
            }

            // 设备未连接时，仍可比较同一个控件路径在父、子设备布局上的重叠。
            string candidateLayout = InputControlPath.TryGetDeviceLayout(candidate);
            string existingLayout = InputControlPath.TryGetDeviceLayout(existing);
            if (string.IsNullOrEmpty(candidateLayout) || string.IsNullOrEmpty(existingLayout)) return false;
            string candidatePrefix = "<" + candidateLayout + ">/";
            string existingPrefix = "<" + existingLayout + ">/";
            candidate = candidate.TrimStart('/');
            existing = existing.TrimStart('/');
            if (!candidate.StartsWith(candidatePrefix, StringComparison.OrdinalIgnoreCase) ||
                !existing.StartsWith(existingPrefix, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(candidate.Substring(candidatePrefix.Length), existing.Substring(existingPrefix.Length),
                    StringComparison.OrdinalIgnoreCase)) return false;

            return UnityInputSystem.IsFirstLayoutBasedOnSecond(candidateLayout, existingLayout) ||
                UnityInputSystem.IsFirstLayoutBasedOnSecond(existingLayout, candidateLayout);
        }

        static bool GroupsOverlap(string first, string second)
        {
            if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second)) return true;
            foreach (string group in first.Split(';'))
                foreach (string other in second.Split(';'))
                    if (string.Equals(group.Trim(), other.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
