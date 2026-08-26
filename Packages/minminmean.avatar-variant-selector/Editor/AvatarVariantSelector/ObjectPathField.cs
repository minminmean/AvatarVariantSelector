using UnityEditor;
using UnityEngine;

namespace MinMinMart.AvatarVariant.Editor
{
    /// <summary>
    /// アバタールートからの相対パスを、オブジェクト参照欄のように編集させる GUI 部品。
    ///
    /// 設定アセットからシーン内オブジェクトは参照できないので保持しているのはパスだが、
    /// その制約を操作感に持ち込まないための層。パスが切れている場合はその場で知らせる。
    /// </summary>
    internal static class ObjectPathField
    {
        private static AvatarVariantLocalizeDictionary LocalizeDict => AvatarVariantLocalize.Dictionary;

        /// <summary>
        /// パス 1 件分の編集欄。
        /// </summary>
        internal static void Draw(SerializedProperty pathProp, Transform root)
        {
            string path = pathProp.stringValue;
            Transform found = root != null ? AvatarVariantSet.FindByPath(root, path) : null;
            bool broken = found == null && !string.IsNullOrEmpty(path);

            EditorGUI.BeginChangeCheck();
            GameObject picked = EditorGUILayout.ObjectField(
                found != null ? found.gameObject : null, typeof(GameObject), true) as GameObject;

            if (EditorGUI.EndChangeCheck())
            {
                if (picked == null)
                {
                    pathProp.stringValue = "";
                    AvatarVariantSetSaver.Request();
                }
                else
                {
                    string newPath = AvatarVariantSet.GetPath(root, picked.transform);
                    if (string.IsNullOrEmpty(newPath))
                    {
                        Debug.LogWarning(string.Format(LocalizeDict.outside_avatar_pick, picked.name));
                    }
                    else
                    {
                        pathProp.stringValue = newPath;
                        AvatarVariantSetSaver.Request();
                    }
                }
            }

            if (!broken) return;

            GUIStyle style = new GUIStyle(EditorStyles.miniLabel);
            style.normal.textColor = new Color(0.9f, 0.45f, 0.35f);
            EditorGUILayout.LabelField(string.Format(LocalizeDict.path_broken, path), style);
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

                string path = AvatarVariantSet.GetPath(root, go.transform);
                if (string.IsNullOrEmpty(path))
                {
                    Debug.LogWarning(string.Format(LocalizeDict.outside_avatar_drop, go.name));
                    continue;
                }

                paths.arraySize++;
                paths.GetArrayElementAtIndex(paths.arraySize - 1).stringValue = path;
                AvatarVariantSetSaver.Request();
            }

            e.Use();
        }
    }
}
