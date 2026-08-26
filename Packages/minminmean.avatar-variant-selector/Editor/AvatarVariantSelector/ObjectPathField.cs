using UnityEditor;
using UnityEngine;

namespace MinMinMart.AvatarVariant.Editor
{
    /// <summary>
    /// アバタールートからの相対パスを、オブジェクト参照欄のように編集させる GUI 部品。
    ///
    /// プロファイルからシーン内オブジェクトは参照できないので保持しているのはパスだが、
    /// その制約を操作感に持ち込まないための層。パスが切れている場合はその場で知らせる。
    /// </summary>
    internal static class ObjectPathField
    {
        private static AvatarVariantLocalizeDictionary LocalizeDict => AvatarVariantLocalize.Dictionary;

        /// <summary>
        /// 毎描画で new せず使い回す GUIStyle。
        ///
        /// 静的初期化子では作らない。EditorStyles がまだ用意できていない段階で
        /// 触るとエラーになるため、実際に使う直前（Draw の先頭）で遅延生成する。
        /// ライト/ダーク切り替えで EditorStyles 自体が作り直されるので、isProSkin の値が
        /// 変わったときも作り直す。
        /// </summary>
        private static class Styles
        {
            private static bool _initialized;
            private static bool _isProSkin;

            internal static GUIStyle BrokenPathLabel;

            internal static void EnsureInitialized()
            {
                bool isProSkin = EditorGUIUtility.isProSkin;
                if (_initialized && _isProSkin == isProSkin) return;

                _initialized = true;
                _isProSkin = isProSkin;

                BrokenPathLabel = new GUIStyle(EditorStyles.miniLabel);
                BrokenPathLabel.normal.textColor = new Color(0.9f, 0.45f, 0.35f);
            }
        }

        /// <summary>
        /// パス 1 件分の編集欄。
        /// </summary>
        internal static void Draw(SerializedProperty pathProp, Transform root)
        {
            Styles.EnsureInitialized();

            string path = pathProp.stringValue;
            Transform found = AvatarVariantProfile.FindByPath(root, path);
            bool broken = found == null && !string.IsNullOrEmpty(path);

            EditorGUI.BeginChangeCheck();
            GameObject picked = EditorGUILayout.ObjectField(
                found != null ? found.gameObject : null, typeof(GameObject), true) as GameObject;

            if (EditorGUI.EndChangeCheck())
            {
                if (picked == null)
                {
                    pathProp.stringValue = "";
                    AvatarVariantProfileSaver.Request();
                }
                else
                {
                    string newPath = AvatarVariantProfile.GetPath(root, picked.transform);
                    if (string.IsNullOrEmpty(newPath))
                    {
                        Debug.LogWarning(string.Format(LocalizeDict.outside_avatar_pick, picked.name));
                    }
                    else
                    {
                        pathProp.stringValue = newPath;
                        AvatarVariantProfileSaver.Request();
                    }
                }
            }

            if (!broken) return;

            // GUI.Label はコントロール ID を消費しない。この行は broken の真偽で出入りするので、
            // ID を消費すると後ろに続く入力欄の ID がズレてフォーカスが外れる。
            GUILayout.Label(string.Format(LocalizeDict.path_broken, path), Styles.BrokenPathLabel);
        }

        /// <summary>
        /// 複数まとめてドラッグ＆ドロップで追加できる領域。
        /// </summary>
        internal static void DrawDropArea(SerializedProperty paths, Transform root)
        {
            Rect rect = GUILayoutUtility.GetRect(0, 24, GUILayout.ExpandWidth(true));
            GUI.Box(rect, LocalizeDict.drop_area, EditorStyles.helpBox);

            Event e = Event.current;
            if (!rect.Contains(e.mousePosition)) return;
            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (e.type != EventType.DragPerform) return;

            DragAndDrop.AcceptDrag();
            foreach (Object obj in DragAndDrop.objectReferences)
            {
                GameObject go = obj as GameObject;
                if (go == null) continue;

                string path = AvatarVariantProfile.GetPath(root, go.transform);
                if (string.IsNullOrEmpty(path))
                {
                    Debug.LogWarning(string.Format(LocalizeDict.outside_avatar_drop, go.name));
                    continue;
                }

                paths.arraySize++;
                paths.GetArrayElementAtIndex(paths.arraySize - 1).stringValue = path;
                AvatarVariantProfileSaver.Request();
            }

            e.Use();
        }
    }
}
