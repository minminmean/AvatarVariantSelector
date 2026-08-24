using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MinMinMart.AvatarVariant.Editor
{
    [CustomEditor(typeof(AvatarVariantSelector))]
    public class AvatarVariantSelectorEditor : UnityEditor.Editor
    {
        // バリアント 1 行の桁を揃えるための固定幅。
        private const float FoldoutWidth = 14f;
        private const float MarkerWidth = 14f;
        private const float ButtonWidth = 44f;
        private const float Gap = 4f;

        // 入力欄の残り幅を、バリアント名とブループリントIDでどう分けるか。
        private const float NameFieldRatio = 0.34f;

        private static AvatarVariantLocalizeDictionary T => AvatarVariantLocalize.T;

        public override void OnInspectorGUI()
        {
            var selector = (AvatarVariantSelector)target;
            var root = FindAvatarRoot(selector.transform);
            var pm = root != null ? root.GetComponent<VRC.Core.PipelineManager>() : null;

            AvatarVariantLocalize.DrawLanguagePopup();
            EditorGUILayout.Space();

            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Set"), new GUIContent(T.set_asset));
            serializedObject.ApplyModifiedProperties();

            if (selector.Set == null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(T.set_asset_help, MessageType.Info);

                if (GUILayout.Button(T.create_set_asset, GUILayout.Height(26)))
                {
                    CreateSetAsset(selector);
                }

                return;
            }

            var set = selector.Set;
            var setSo = new SerializedObject(set);
            setSo.Update();

            DrawStatus(set, root, pm);
            EditorGUILayout.Space();
            DrawPendingBanner(set);
            DrawSwitcher(set, pm);
            EditorGUILayout.Space();
            DrawWarnings(set, root);
            EditorGUILayout.Space();

            EditorGUI.BeginChangeCheck();
            DrawVariants(setSo, root, pm);
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(setSo.FindProperty("AllowUnmatchedBlueprintId"),
                new GUIContent(T.allow_unmatched));

            if (setSo.ApplyModifiedProperties() || EditorGUI.EndChangeCheck())
            {
                // 設定アセットはその場で保存する。シーン保存に依存させない。
                EditorUtility.SetDirty(set);
                AssetDatabase.SaveAssetIfDirty(set);
            }
        }

        // ---------- バリアント一覧 ----------

        private static void DrawVariants(SerializedObject setSo, GameObject root, VRC.Core.PipelineManager pm)
        {
            var variants = setSo.FindProperty("Variants");
            EditorGUILayout.LabelField(string.Format(T.variants_header, variants.arraySize), EditorStyles.boldLabel);

            for (var i = 0; i < variants.arraySize; i++)
            {
                var variant = variants.GetArrayElementAtIndex(i);
                var nameProp = variant.FindPropertyRelative("Name");
                var idProp = variant.FindPropertyRelative("BlueprintId");
                var isCurrent = pm != null
                                && !string.IsNullOrEmpty(idProp.stringValue)
                                && idProp.stringValue == pm.blueprintId;

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    // 1 バリアント = 1 行。レイアウト要素を並べるとインデントと余白でズレるので、
                    // 行ぶんの矩形を 1 つ取って自前で分割する。
                    var row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                    var indent = EditorGUI.indentLevel;
                    EditorGUI.indentLevel = 0;

                    var x = row.x;
                    var foldRect = new Rect(x, row.y, FoldoutWidth, row.height);
                    x += FoldoutWidth;
                    var markRect = new Rect(x, row.y, MarkerWidth, row.height);
                    x += MarkerWidth;

                    var delRect = new Rect(row.xMax - ButtonWidth, row.y, ButtonWidth, row.height);
                    var dupRect = new Rect(delRect.x - ButtonWidth - 2f, row.y, ButtonWidth, row.height);

                    var fieldsWidth = dupRect.x - Gap - x;
                    var nameWidth = Mathf.Max(70f, fieldsWidth * NameFieldRatio);
                    var nameRect = new Rect(x, row.y, nameWidth, row.height);
                    var idRect = new Rect(x + nameWidth + Gap, row.y, fieldsWidth - nameWidth - Gap, row.height);

                    variant.isExpanded = EditorGUI.Foldout(foldRect, variant.isExpanded, GUIContent.none, true);
                    DrawCurrentMarker(markRect, isCurrent);
                    DrawFieldWithPlaceholder(nameRect, nameProp, T.placeholder_name);
                    DrawFieldWithPlaceholder(idRect, idProp, T.placeholder_id);

                    var duplicate = GUI.Button(dupRect, T.duplicate);
                    var delete = GUI.Button(delRect, T.delete);
                    EditorGUI.indentLevel = indent;

                    if (duplicate)
                    {
                        DuplicateVariant(variants, i);
                        return;
                    }

                    if (delete)
                    {
                        var title = string.IsNullOrEmpty(nameProp.stringValue)
                            ? string.Format(T.variant_unnamed, i)
                            : nameProp.stringValue;

                        if (EditorUtility.DisplayDialog(T.delete_dialog_title,
                                string.Format(T.delete_dialog_message, title), T.delete, T.delete_dialog_cancel))
                        {
                            variants.DeleteArrayElementAtIndex(i);
                        }

                        return;
                    }

                    if (!variant.isExpanded) continue;

                    EditorGUILayout.Space(2);
                    EditorGUILayout.LabelField(T.operations_header, EditorStyles.miniBoldLabel);

                    var rootT = root != null ? root.transform : null;
                    DrawRemoveList(variant.FindPropertyRelative("RemoveObjectPaths"), rootT);
                    DrawMaterialList(variant.FindPropertyRelative("MaterialOverrides"), rootT);
                    DrawBlendShapeList(variant.FindPropertyRelative("BlendShapeChanges"), rootT);
                }
            }

            if (GUILayout.Button(T.add_variant))
            {
                AddBlankVariant(variants);
            }
        }

        // ---------- 操作リスト ----------

        private static void DrawRemoveList(SerializedProperty paths, Transform root)
        {
            paths.isExpanded = EditorGUILayout.Foldout(paths.isExpanded,
                string.Format(T.op_remove, paths.arraySize), true);
            if (!paths.isExpanded) return;

            using (new EditorGUI.IndentLevelScope())
            {
                for (var i = 0; i < paths.arraySize; i++)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        DrawObjectPathField(paths.GetArrayElementAtIndex(i), root);
                        if (GUILayout.Button("−", GUILayout.Width(22)))
                        {
                            paths.DeleteArrayElementAtIndex(i);
                            return;
                        }
                    }
                }

                DrawDropArea(paths, root);
            }
        }

        private static void DrawMaterialList(SerializedProperty list, Transform root)
        {
            list.isExpanded = EditorGUILayout.Foldout(list.isExpanded,
                string.Format(T.op_material, list.arraySize), true);
            if (!list.isExpanded) return;

            using (new EditorGUI.IndentLevelScope())
            {
                for (var i = 0; i < list.arraySize; i++)
                {
                    var e = list.GetArrayElementAtIndex(i);
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            DrawObjectPathField(e.FindPropertyRelative("RendererPath"), root);
                            if (GUILayout.Button("−", GUILayout.Width(22)))
                            {
                                list.DeleteArrayElementAtIndex(i);
                                return;
                            }
                        }

                        EditorGUILayout.PropertyField(e.FindPropertyRelative("Slot"), new GUIContent(T.field_slot));
                        EditorGUILayout.PropertyField(e.FindPropertyRelative("Material"), new GUIContent(T.field_material));
                    }
                }

                if (GUILayout.Button(T.add_entry)) list.arraySize++;
            }
        }

        private static void DrawBlendShapeList(SerializedProperty list, Transform root)
        {
            list.isExpanded = EditorGUILayout.Foldout(list.isExpanded,
                string.Format(T.op_blendshape, list.arraySize), true);
            if (!list.isExpanded) return;

            using (new EditorGUI.IndentLevelScope())
            {
                for (var i = 0; i < list.arraySize; i++)
                {
                    var e = list.GetArrayElementAtIndex(i);
                    var pathProp = e.FindPropertyRelative("RendererPath");
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            DrawObjectPathField(pathProp, root);
                            if (GUILayout.Button("−", GUILayout.Width(22)))
                            {
                                list.DeleteArrayElementAtIndex(i);
                                return;
                            }
                        }

                        DrawShapePopup(e.FindPropertyRelative("ShapeName"), pathProp, root);
                        EditorGUILayout.Slider(e.FindPropertyRelative("Value"), 0f, 100f, new GUIContent(T.field_value));
                    }
                }

                if (GUILayout.Button(T.add_entry)) list.arraySize++;
            }
        }

        /// <summary>
        /// 対象メッシュが実際に持つシェイプ名をドロップダウンで選ばせる。打ち間違いを防ぐため。
        /// </summary>
        private static void DrawShapePopup(SerializedProperty nameProp, SerializedProperty pathProp, Transform root)
        {
            var t = root != null ? AvatarVariantSet.FindByPath(root, pathProp.stringValue) : null;
            var smr = t != null ? t.GetComponent<SkinnedMeshRenderer>() : null;
            var mesh = smr != null ? smr.sharedMesh : null;

            if (mesh == null || mesh.blendShapeCount == 0)
            {
                // 選択肢を出せる対象が無いときは、既存の値を編集できるよう素のテキスト欄にする。
                EditorGUILayout.PropertyField(nameProp, new GUIContent(T.field_shape));
                return;
            }

            var names = new List<string>(mesh.blendShapeCount + 1) { T.shape_none };
            for (var i = 0; i < mesh.blendShapeCount; i++) names.Add(mesh.GetBlendShapeName(i));

            var current = names.IndexOf(nameProp.stringValue);
            if (current < 0 && !string.IsNullOrEmpty(nameProp.stringValue))
            {
                // メッシュに無い名前でも、黙って消さずに見える形で残す。
                names.Add(nameProp.stringValue + T.shape_missing_suffix);
                current = names.Count - 1;
            }
            else if (current < 0)
            {
                current = 0;
            }

            EditorGUI.BeginChangeCheck();
            var picked = EditorGUILayout.Popup(T.field_shape, current, names.ToArray());
            if (EditorGUI.EndChangeCheck())
            {
                nameProp.stringValue = picked == 0 ? "" : names[picked];
            }
        }

        /// <summary>
        /// パス文字列を、オブジェクト参照欄のように編集させる。
        /// アセットからシーンを参照できないので保持はパスだが、操作感は据え置く。
        /// </summary>
        private static void DrawObjectPathField(SerializedProperty pathProp, Transform root)
        {
            var path = pathProp.stringValue;
            var found = root != null ? AvatarVariantSet.FindByPath(root, path) : null;
            var broken = found == null && !string.IsNullOrEmpty(path);

            EditorGUI.BeginChangeCheck();
            var picked = EditorGUILayout.ObjectField(
                found != null ? found.gameObject : null, typeof(GameObject), true) as GameObject;

            if (EditorGUI.EndChangeCheck())
            {
                if (picked == null)
                {
                    pathProp.stringValue = "";
                }
                else
                {
                    var newPath = AvatarVariantSet.GetPath(root, picked.transform);
                    if (string.IsNullOrEmpty(newPath))
                    {
                        Debug.LogWarning(string.Format(T.outside_avatar_pick, picked.name));
                    }
                    else
                    {
                        pathProp.stringValue = newPath;
                    }
                }
            }

            if (!broken) return;

            var style = new GUIStyle(EditorStyles.miniLabel);
            style.normal.textColor = new Color(0.9f, 0.45f, 0.35f);
            EditorGUILayout.LabelField(string.Format(T.path_broken, path), style);
        }

        /// <summary>
        /// 複数まとめてドラッグ＆ドロップで追加できる領域。
        /// </summary>
        private static void DrawDropArea(SerializedProperty paths, Transform root)
        {
            var rect = GUILayoutUtility.GetRect(0, 24, GUILayout.ExpandWidth(true));
            GUI.Box(rect, T.drop_area, EditorStyles.helpBox);

            var e = Event.current;
            if (!rect.Contains(e.mousePosition)) return;
            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (e.type != EventType.DragPerform) return;

            DragAndDrop.AcceptDrag();
            foreach (var obj in DragAndDrop.objectReferences)
            {
                var go = obj as GameObject;
                if (go == null) continue;

                var path = AvatarVariantSet.GetPath(root, go.transform);
                if (string.IsNullOrEmpty(path))
                {
                    Debug.LogWarning(string.Format(T.outside_avatar_drop, go.name));
                    continue;
                }

                paths.arraySize++;
                paths.GetArrayElementAtIndex(paths.arraySize - 1).stringValue = path;
            }

            e.Use();
        }

        // ---------- ヘッダー ----------

        private static void DrawStatus(AvatarVariantSet set, GameObject root, VRC.Core.PipelineManager pm)
        {
            if (root == null)
            {
                EditorGUILayout.HelpBox(T.no_avatar_root, MessageType.Error);
                return;
            }

            if (pm == null)
            {
                EditorGUILayout.HelpBox(string.Format(T.no_pipeline_manager, root.name), MessageType.Error);
                return;
            }

            var current = set.ResolveForBuild(pm.blueprintId, out var viaPending);
            var style = new GUIStyle(EditorStyles.helpBox)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(8, 8, 10, 10),
            };
            style.normal.textColor = current != null
                ? new Color(0.35f, 0.75f, 0.35f)
                : new Color(0.9f, 0.5f, 0.3f);

            var caption = current == null
                ? T.build_target_none
                : string.Format(viaPending ? T.build_target_new : T.build_target, current.Name);

            EditorGUILayout.LabelField(caption, style);
            EditorGUILayout.LabelField(T.blueprint_id,
                string.IsNullOrEmpty(pm.blueprintId) ? T.blueprint_id_unassigned : pm.blueprintId);

            EditorGUILayout.HelpBox(T.scene_untouched_help, MessageType.None);
        }

        private static void DrawPendingBanner(AvatarVariantSet set)
        {
            var pending = set.PendingVariant;
            if (pending == null) return;

            EditorGUILayout.HelpBox(string.Format(T.pending_banner, pending.Name), MessageType.Info);

            if (GUILayout.Button(T.cancel_pending, GUILayout.Width(110)))
            {
                Undo.RecordObject(set, "Cancel pending variant");
                set.PendingVariantKey = "";
                EditorUtility.SetDirty(set);
                AssetDatabase.SaveAssetIfDirty(set);
            }

            EditorGUILayout.Space();
        }

        private static void DrawSwitcher(AvatarVariantSet set, VRC.Core.PipelineManager pm)
        {
            if (pm == null) return;

            EditorGUILayout.LabelField(T.switch_header, EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                foreach (var variant in set.Variants)
                {
                    if (variant == null) continue;

                    // ID が未採番のバリアントは、切り替えではなく新規アップロードの対象として選ぶ。
                    if (string.IsNullOrEmpty(variant.BlueprintId))
                    {
                        var isPending = set.PendingVariant == variant;
                        using (new EditorGUI.DisabledScope(isPending))
                        {
                            var label = string.Format(isPending ? T.pending_label : T.mark_new_upload, variant.Name);
                            if (GUILayout.Button(label, GUILayout.Height(26)))
                            {
                                MarkPending(set, pm, variant);
                            }
                        }

                        continue;
                    }

                    var isCurrent = variant.BlueprintId == pm.blueprintId;
                    using (new EditorGUI.DisabledScope(isCurrent))
                    {
                        var label = string.Format(isCurrent ? T.switch_current : T.switch_to, variant.Name);
                        if (GUILayout.Button(label, GUILayout.Height(26)))
                        {
                            WriteBlueprintId(pm, variant.BlueprintId);
                            Debug.Log(string.Format(T.log_switched, variant.Name, variant.BlueprintId));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// バリアントを新規アップロード対象にする。
        /// 既存アバターを上書きしないよう、PipelineManager の ID も空にする。
        /// </summary>
        private static void MarkPending(AvatarVariantSet set, VRC.Core.PipelineManager pm,
            AvatarVariantDefinition variant)
        {
            Undo.RecordObject(set, "Mark pending variant");
            if (string.IsNullOrEmpty(variant.Key))
            {
                variant.Key = System.Guid.NewGuid().ToString("N");
            }

            set.PendingVariantKey = variant.Key;
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssetIfDirty(set);

            if (pm != null && !string.IsNullOrEmpty(pm.blueprintId))
            {
                WriteBlueprintId(pm, "");
            }

            Debug.Log(string.Format(T.log_marked_pending, variant.Name));
        }

        /// <summary>
        /// PipelineManager の Blueprint ID を書き換える。
        /// このフィールドは Inspector に出ないので SerializedProperty 経由で触る。
        /// </summary>
        private static void WriteBlueprintId(VRC.Core.PipelineManager pm, string value)
        {
            Undo.RecordObject(pm, "Set blueprint ID");

            var so = new SerializedObject(pm);
            var prop = so.FindProperty("blueprintId");
            if (prop != null)
            {
                prop.stringValue = value;
                so.ApplyModifiedProperties();
            }
            else
            {
                pm.blueprintId = value;
            }

            EditorUtility.SetDirty(pm);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(pm.gameObject.scene);
        }

        // ---------- 検証 ----------

        private static void DrawWarnings(AvatarVariantSet set, GameObject root)
        {
            var problems = new List<string>();
            var ids = set.Variants.Where(v => v != null).Select(v => v.BlueprintId).ToList();

            foreach (var v in set.Variants.Where(v => v != null))
            {
                if (!string.IsNullOrEmpty(v.BlueprintId) && ids.Count(id => id == v.BlueprintId) > 1)
                {
                    problems.Add(string.Format(T.warn_duplicate_id, v.BlueprintId));
                }

                if (root == null) continue;
                var rootT = root.transform;

                foreach (var path in v.RemoveObjectPaths.Where(p => !string.IsNullOrEmpty(p)))
                {
                    if (AvatarVariantSet.FindByPath(rootT, path) == null)
                    {
                        problems.Add(string.Format(T.warn_remove_missing, v.Name, path));
                    }
                }

                foreach (var mo in v.MaterialOverrides.Where(mo => mo != null && !string.IsNullOrEmpty(mo.RendererPath)))
                {
                    var t = AvatarVariantSet.FindByPath(rootT, mo.RendererPath);
                    var r = t != null ? t.GetComponent<Renderer>() : null;
                    if (r == null)
                    {
                        problems.Add(string.Format(T.warn_material_missing, v.Name, mo.RendererPath));
                    }
                    else if (mo.Slot < 0 || mo.Slot >= r.sharedMaterials.Length)
                    {
                        problems.Add(string.Format(T.warn_material_slot,
                            v.Name, mo.RendererPath, mo.Slot, r.sharedMaterials.Length));
                    }
                }

                foreach (var bs in v.BlendShapeChanges.Where(bs => bs != null && !string.IsNullOrEmpty(bs.RendererPath)))
                {
                    var t = AvatarVariantSet.FindByPath(rootT, bs.RendererPath);
                    var smr = t != null ? t.GetComponent<SkinnedMeshRenderer>() : null;
                    if (smr == null || smr.sharedMesh == null)
                    {
                        problems.Add(string.Format(T.warn_shape_no_renderer, v.Name, bs.RendererPath));
                    }
                    else if (string.IsNullOrEmpty(bs.ShapeName))
                    {
                        problems.Add(string.Format(T.warn_shape_unselected, v.Name, bs.RendererPath));
                    }
                    else if (smr.sharedMesh.GetBlendShapeIndex(bs.ShapeName) < 0)
                    {
                        problems.Add(string.Format(T.warn_shape_missing, v.Name, bs.RendererPath, bs.ShapeName));
                    }
                }
            }

            if (set.AllowUnmatchedBlueprintId)
            {
                problems.Add(T.warn_allow_unmatched);
            }

            if (problems.Count > 0)
            {
                EditorGUILayout.HelpBox(string.Join("\n", problems), MessageType.Warning);
            }
        }

        // ---------- 補助 ----------

        private static void DrawCurrentMarker(Rect rect, bool isCurrent)
        {
            if (!isCurrent) return;

            var style = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(0, 0, 0, 0),
            };
            style.normal.textColor = new Color(0.35f, 0.75f, 0.35f);
            EditorGUI.LabelField(rect, "●", style);
        }

        private static void DrawFieldWithPlaceholder(Rect rect, SerializedProperty prop, string placeholder)
        {
            EditorGUI.PropertyField(rect, prop, GUIContent.none);
            if (!string.IsNullOrEmpty(prop.stringValue)) return;

            var style = new GUIStyle(EditorStyles.label)
            {
                fontStyle = FontStyle.Italic,
                padding = new RectOffset(2, 2, 0, 0),
            };
            style.normal.textColor = new Color(0.5f, 0.5f, 0.5f, 0.9f);

            // ラベルは操作を奪わないので、下のテキスト欄はそのままクリック・入力できる。
            EditorGUI.LabelField(new Rect(rect.x + 2f, rect.y, rect.width - 4f, rect.height), placeholder, style);
        }

        /// <summary>
        /// 空のバリアントを末尾に足す。
        /// arraySize を増やすだけだと Unity が直前の要素をコピーしてしまい、
        /// 追加した瞬間に Blueprint ID が重複するので、明示的に初期化する。
        /// </summary>
        private static void AddBlankVariant(SerializedProperty variants)
        {
            var index = variants.arraySize;
            variants.arraySize++;

            var v = variants.GetArrayElementAtIndex(index);
            v.isExpanded = false;
            v.FindPropertyRelative("Name").stringValue = "";
            v.FindPropertyRelative("Key").stringValue = System.Guid.NewGuid().ToString("N");
            v.FindPropertyRelative("BlueprintId").stringValue = "";
            v.FindPropertyRelative("RemoveObjectPaths").ClearArray();
            v.FindPropertyRelative("MaterialOverrides").ClearArray();
            v.FindPropertyRelative("BlendShapeChanges").ClearArray();
        }

        /// <summary>
        /// 既存バリアントを複製する。操作内容は引き継ぐが、
        /// Blueprint ID は取り違えを防ぐために空にする。
        /// </summary>
        private static void DuplicateVariant(SerializedProperty variants, int index)
        {
            variants.InsertArrayElementAtIndex(index);

            var copy = variants.GetArrayElementAtIndex(index + 1);
            copy.isExpanded = false;
            var nameProp = copy.FindPropertyRelative("Name");
            nameProp.stringValue = nameProp.stringValue + T.copy_suffix;
            copy.FindPropertyRelative("Key").stringValue = System.Guid.NewGuid().ToString("N");
            copy.FindPropertyRelative("BlueprintId").stringValue = "";
        }

        private static void CreateSetAsset(AvatarVariantSelector selector)
        {
            var scenePath = selector.gameObject.scene.path;
            var dir = string.IsNullOrEmpty(scenePath) ? "Assets" : Path.GetDirectoryName(scenePath);
            var baseName = string.IsNullOrEmpty(scenePath)
                ? selector.gameObject.name
                : Path.GetFileNameWithoutExtension(scenePath);

            var path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{baseName}_Variants.asset");
            var set = ScriptableObject.CreateInstance<AvatarVariantSet>();
            AssetDatabase.CreateAsset(set, path);
            AssetDatabase.SaveAssetIfDirty(set);

            Undo.RecordObject(selector, "Assign variant set");
            selector.Set = set;
            EditorUtility.SetDirty(selector);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(selector.gameObject.scene);

            Debug.Log(string.Format(T.log_created_asset, path), set);
        }

        private static GameObject FindAvatarRoot(Transform t)
        {
            while (t != null)
            {
                if (t.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>() != null)
                {
                    return t.gameObject;
                }

                t = t.parent;
            }

            return null;
        }
    }
}
