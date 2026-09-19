using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MinMinMart.AvatarVariant.Editor
{
    /// <summary>
    /// バリアント 1 つに登録された操作（削除・有効状態・マテリアル・ブレンドシェイプ）の編集欄。
    ///
    /// どの種類も「折りたたみ + 行の追加削除」という同じ形をしているので、
    /// バリアント一覧そのものの描画からは切り離してここにまとめている。
    /// </summary>
    internal static class VariantOperationGui
    {
        private static AvatarVariantLocalizeDictionary LocalizeDict => AvatarVariantLocalize.Dictionary;

        /// <summary>
        /// 削除するオブジェクトの一覧。
        /// </summary>
        internal static void DrawRemoveList(SerializedProperty paths, Transform root)
        {
            bool expanded = FoldoutState.GetExpanded(paths);
            expanded = EditorGUILayout.Foldout(expanded, string.Format(LocalizeDict.op_remove, paths.arraySize), true);
            FoldoutState.SetExpanded(paths, expanded);
            if (!expanded) return;

            using (new EditorGUI.IndentLevelScope())
            {
                for (int i = 0; i < paths.arraySize; i++)
                {
                    if (DrawPathRow(paths, i, paths.GetArrayElementAtIndex(i), root)) return;
                }

                ObjectPathField.DrawDropArea(paths, root);
            }
        }

        /// <summary>
        /// 有効・無効を切り替えるオブジェクトの一覧。
        /// </summary>
        internal static void DrawActiveList(SerializedProperty list, Transform root)
        {
            bool expanded = FoldoutState.GetExpanded(list);
            expanded = EditorGUILayout.Foldout(expanded, string.Format(LocalizeDict.op_active, list.arraySize), true);
            FoldoutState.SetExpanded(list, expanded);
            if (!expanded) return;

            using (new EditorGUI.IndentLevelScope())
            {
                for (int i = 0; i < list.arraySize; i++)
                {
                    SerializedProperty entry = list.GetArrayElementAtIndex(i);
                    SerializedProperty activeProp = entry.FindPropertyRelative("Active");
                    if (DrawPathRow(list, i, entry.FindPropertyRelative("ObjectPath"), root,
                            () => DrawActiveChoice(activeProp))) return;
                }

                // 今の状態は見ずに、常に ON で入れる。
                // 要素を増やすと直前の要素の値が複製されるので、明示的に書き込む。
                ObjectPathField.DrawDropArea(list, root, (element, go, path) =>
                {
                    element.FindPropertyRelative("ObjectPath").stringValue = path;
                    element.FindPropertyRelative("Active").boolValue = true;
                });
            }
        }

        /// <summary>
        /// ON と OFF のチェックボックスを並べ、どちらか一方だけにチェックが入るようにする。
        ///
        /// EditorGUI 系のトグルはインデント分だけ中身をずらすため、幅を詰めた枠からはみ出して
        /// クリックが隣の欄に吸われる。インデントの影響を受けない GUILayout 側で描く。
        /// </summary>
        private static void DrawActiveChoice(SerializedProperty activeProp)
        {
            bool active = activeProp.boolValue;

            // チェック済みの側を押しても外れないよう、押されたら常にその側を選ぶ。
            bool pickedOn = GUILayout.Toggle(active, LocalizeDict.toggle_on, EditorStyles.toggle, GUILayout.ExpandWidth(false));
            bool pickedOff = GUILayout.Toggle(!active, LocalizeDict.toggle_off, EditorStyles.toggle, GUILayout.ExpandWidth(false));

            bool next = active;
            if (pickedOn != active) next = true;
            else if (pickedOff != !active) next = false;
            if (next == active) return;

            // チェックの切り替えは 1 回きりの操作なので、その場で確定させる。
            activeProp.boolValue = next;
            AvatarVariantProfileSaver.Request();
        }

        /// <summary>
        /// 差し替えるマテリアルスロットの一覧。
        /// </summary>
        internal static void DrawMaterialList(SerializedProperty list, Transform root)
        {
            bool expanded = FoldoutState.GetExpanded(list);
            expanded = EditorGUILayout.Foldout(expanded, string.Format(LocalizeDict.op_material, list.arraySize), true);
            FoldoutState.SetExpanded(list, expanded);
            if (!expanded) return;

            using (new EditorGUI.IndentLevelScope())
            {
                for (int i = 0; i < list.arraySize; i++)
                {
                    SerializedProperty entry = list.GetArrayElementAtIndex(i);
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        if (DrawPathRow(list, i, entry.FindPropertyRelative("RendererPath"), root)) return;

                        SerializedProperty slotProp = entry.FindPropertyRelative("Slot");
                        AvatarVariantProfileSaver.NameWatchedField(slotProp.propertyPath);
                        EditorGUILayout.PropertyField(slotProp, new GUIContent(LocalizeDict.field_slot));

                        // 差し替え先の指定は 1 回きりの操作なので、その場で確定させる。
                        EditorGUI.BeginChangeCheck();
                        EditorGUILayout.PropertyField(entry.FindPropertyRelative("Material"), new GUIContent(LocalizeDict.field_material));
                        if (EditorGUI.EndChangeCheck()) AvatarVariantProfileSaver.Request();
                    }
                }

                if (GUILayout.Button(LocalizeDict.add_entry))
                {
                    list.arraySize++;
                    AvatarVariantProfileSaver.Request();
                }
            }
        }

        /// <summary>
        /// 設定するブレンドシェイプの一覧。
        /// </summary>
        internal static void DrawBlendShapeList(SerializedProperty list, Transform root)
        {
            bool expanded = FoldoutState.GetExpanded(list);
            expanded = EditorGUILayout.Foldout(expanded, string.Format(LocalizeDict.op_blendshape, list.arraySize), true);
            FoldoutState.SetExpanded(list, expanded);
            if (!expanded) return;

            using (new EditorGUI.IndentLevelScope())
            {
                for (int i = 0; i < list.arraySize; i++)
                {
                    SerializedProperty entry = list.GetArrayElementAtIndex(i);
                    SerializedProperty pathProp = entry.FindPropertyRelative("RendererPath");
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        if (DrawPathRow(list, i, pathProp, root)) return;

                        DrawShapePopup(entry.FindPropertyRelative("ShapeName"), pathProp, root);
                        SerializedProperty valueProp = entry.FindPropertyRelative("Value");
                        AvatarVariantProfileSaver.NameWatchedField(valueProp.propertyPath);
                        EditorGUILayout.Slider(valueProp, 0f, 100f, new GUIContent(LocalizeDict.field_value));
                    }
                }

                if (GUILayout.Button(LocalizeDict.add_entry))
                {
                    list.arraySize++;
                    AvatarVariantProfileSaver.Request();
                }
            }
        }

        /// <summary>
        /// 対象の指定欄と、その行を消すボタン。消したときは true を返す
        /// （呼び出し側は、その場で描画を打ち切ること）。
        /// <paramref name="drawTrailing"/> を渡すと、指定欄の右（消すボタンの手前）に同じ行で描く。
        /// </summary>
        private static bool DrawPathRow(SerializedProperty list, int index, SerializedProperty pathProp, Transform root,
            System.Action drawTrailing = null)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                ObjectPathField.Draw(pathProp, root);
                drawTrailing?.Invoke();
                if (GUILayout.Button("−", GUILayout.Width(22)))
                {
                    list.DeleteArrayElementAtIndex(index);
                    AvatarVariantProfileSaver.Request();
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 対象メッシュが実際に持つシェイプ名をドロップダウンで選ばせる。打ち間違いを防ぐため。
        /// </summary>
        private static void DrawShapePopup(SerializedProperty nameProp, SerializedProperty pathProp, Transform root)
        {
            SkinnedMeshRenderer smr = AvatarVariantProfile.FindComponentByPath<SkinnedMeshRenderer>(root, pathProp.stringValue);
            Mesh mesh = smr != null ? smr.sharedMesh : null;

            if (mesh == null || mesh.blendShapeCount == 0)
            {
                // 選択肢を出せる対象が無いときは、既存の値を編集できるよう素のテキスト欄にする。
                AvatarVariantProfileSaver.NameWatchedField(nameProp.propertyPath);
                EditorGUILayout.PropertyField(nameProp, new GUIContent(LocalizeDict.field_shape));
                return;
            }

            List<string> names = new List<string>(mesh.blendShapeCount + 1) { LocalizeDict.shape_none };
            for (int i = 0; i < mesh.blendShapeCount; i++) names.Add(mesh.GetBlendShapeName(i));

            int current = names.IndexOf(nameProp.stringValue);
            if (current < 0 && !string.IsNullOrEmpty(nameProp.stringValue))
            {
                // メッシュに無い名前でも、黙って消さずに見える形で残す。
                names.Add(nameProp.stringValue + LocalizeDict.shape_missing_suffix);
                current = names.Count - 1;
            }
            else if (current < 0)
            {
                current = 0;
            }

            EditorGUI.BeginChangeCheck();
            int picked = EditorGUILayout.Popup(LocalizeDict.field_shape, current, names.ToArray());
            if (EditorGUI.EndChangeCheck())
            {
                nameProp.stringValue = picked == 0 ? "" : names[picked];
                AvatarVariantProfileSaver.Request();
            }
        }
    }
}
