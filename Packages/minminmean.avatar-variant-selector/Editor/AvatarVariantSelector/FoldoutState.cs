using System.Collections.Generic;
using UnityEditor;

namespace MinMinMart.AvatarVariant.Editor
{
    /// <summary>
    /// 折りたたみの開閉状態。
    ///
    /// SerializedProperty.isExpanded は SerializedObject の変更として扱われるため、
    /// 開閉しただけでプロファイルが変更済みになり、「適用」が押せる状態になってしまう。
    /// 開閉は見た目の状態でしかないので保存はせず、エディタ側だけで覚える。
    ///
    /// 覚えているのはドメインリロードまで。Unity を再起動すると畳まれた状態に戻る。
    /// </summary>
    internal static class FoldoutState
    {
        private static readonly HashSet<string> Expanded = new HashSet<string>();

        /// <summary>
        /// <paramref name="property"/> が開いているか。
        /// </summary>
        internal static bool GetExpanded(SerializedProperty property)
        {
            return Expanded.Contains(BuildKey(property));
        }

        /// <summary>
        /// <paramref name="property"/> の開閉を覚える。
        /// </summary>
        internal static void SetExpanded(SerializedProperty property, bool expanded)
        {
            string key = BuildKey(property);

            if (expanded)
            {
                Expanded.Add(key);
            }
            else
            {
                Expanded.Remove(key);
            }
        }

        /// <summary>
        /// 対象アセットとプロパティのパスで一意に決まる名前。
        /// 別のアセットの同じ位置と混ざらないよう、インスタンス ID を前に付ける。
        /// </summary>
        private static string BuildKey(SerializedProperty property)
        {
            return $"{property.serializedObject.targetObject.GetInstanceID()}:{property.propertyPath}";
        }
    }
}
