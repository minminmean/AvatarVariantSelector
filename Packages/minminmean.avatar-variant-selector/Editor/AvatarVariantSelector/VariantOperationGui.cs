using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MinMinMart.AvatarVariant.Editor
{
    /// <summary>
    /// バリアント 1 つに登録された操作（削除・マテリアル・ブレンドシェイプ）の編集欄。
    ///
    /// 3 種類とも「折りたたみ + 行の追加削除」という同じ形をしているので、
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
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        ObjectPathField.Draw(paths.GetArrayElementAtIndex(i), root);
                        if (GUILayout.Button("−", GUILayout.Width(22)))
                        {
                            paths.DeleteArrayElementAtIndex(i);
                            AvatarVariantProfileSaver.Request();
                            return;
                        }
                    }
                }

                ObjectPathField.DrawDropArea(paths, root);
            }
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
                    SerializedProperty e = list.GetArrayElementAtIndex(i);
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            ObjectPathField.Draw(e.FindPropertyRelative("RendererPath"), root);
                            if (GUILayout.Button("−", GUILayout.Width(22)))
                            {
                                list.DeleteArrayElementAtIndex(i);
                                AvatarVariantProfileSaver.Request();
                                return;
                            }
                        }

                        SerializedProperty slotProp = e.FindPropertyRelative("Slot");
                        AvatarVariantProfileSaver.NameWatchedField(slotProp.propertyPath);
                        EditorGUILayout.PropertyField(slotProp, new GUIContent(LocalizeDict.field_slot));

                        // 差し替え先の指定は 1 回きりの操作なので、その場で確定させる。
                        EditorGUI.BeginChangeCheck();
                        EditorGUILayout.PropertyField(e.FindPropertyRelative("Material"), new GUIContent(LocalizeDict.field_material));
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
                    SerializedProperty e = list.GetArrayElementAtIndex(i);
                    SerializedProperty pathProp = e.FindPropertyRelative("RendererPath");
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            ObjectPathField.Draw(pathProp, root);
                            if (GUILayout.Button("−", GUILayout.Width(22)))
                            {
                                list.DeleteArrayElementAtIndex(i);
                                AvatarVariantProfileSaver.Request();
                                return;
                            }
                        }

                        DrawShapePopup(e.FindPropertyRelative("ShapeName"), pathProp, root);
                        SerializedProperty valueProp = e.FindPropertyRelative("Value");
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
        /// 対象メッシュが実際に持つシェイプ名をドロップダウンで選ばせる。打ち間違いを防ぐため。
        /// </summary>
        private static void DrawShapePopup(SerializedProperty nameProp, SerializedProperty pathProp, Transform root)
        {
            Transform t = root != null ? AvatarVariantProfile.FindByPath(root, pathProp.stringValue) : null;
            SkinnedMeshRenderer smr = t != null ? t.GetComponent<SkinnedMeshRenderer>() : null;
            Mesh mesh = smr != null ? smr.sharedMesh : null;

            if (mesh == null || mesh.blendShapeCount == 0)
            {
                // 選択肢を出せる対象が無いときは、既存の値を編集できるよう素のテキスト欄にする。
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
