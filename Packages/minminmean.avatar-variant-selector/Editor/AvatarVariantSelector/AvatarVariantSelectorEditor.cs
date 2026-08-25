using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MinMinMart.AvatarVariant.Editor
{
    /// <summary>
    /// アバターに付けたコンポーネントの Inspector。
    ///
    /// 表示の組み立てと、バリアント一覧の行レイアウトだけを持つ。
    /// 検証は <see cref="AvatarVariantValidator"/>、切り替えは <see cref="AvatarVariantSwitcher"/>、
    /// 操作リストは <see cref="VariantOperationGui"/> にそれぞれ委ねている。
    /// </summary>
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
            AvatarVariantSelector selector = (AvatarVariantSelector)target;
            Transform rootTransform = AvatarRootFinder.Find(selector.transform);
            GameObject root = rootTransform != null ? rootTransform.gameObject : null;
            VRC.Core.PipelineManager pm = rootTransform != null
                ? rootTransform.GetComponent<VRC.Core.PipelineManager>()
                : null;

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
                    AvatarVariantSetFactory.CreateForSelector(selector);
                }

                return;
            }

            AvatarVariantSet set = selector.Set;
            SerializedObject setSo = new SerializedObject(set);
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
            SerializedProperty variants = setSo.FindProperty("Variants");
            EditorGUILayout.LabelField(string.Format(T.variants_header, variants.arraySize), EditorStyles.boldLabel);

            for (int i = 0; i < variants.arraySize; i++)
            {
                SerializedProperty variant = variants.GetArrayElementAtIndex(i);
                SerializedProperty nameProp = variant.FindPropertyRelative("Name");
                SerializedProperty idProp = variant.FindPropertyRelative("BlueprintId");
                bool isCurrent = pm != null
                                 && !string.IsNullOrEmpty(idProp.stringValue)
                                 && idProp.stringValue == pm.blueprintId;

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    // 1 バリアント = 1 行。レイアウト要素を並べるとインデントと余白でズレるので、
                    // 行ぶんの矩形を 1 つ取って自前で分割する。
                    Rect row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                    int indent = EditorGUI.indentLevel;
                    EditorGUI.indentLevel = 0;

                    float x = row.x;
                    Rect foldRect = new Rect(x, row.y, FoldoutWidth, row.height);
                    x += FoldoutWidth;
                    Rect markRect = new Rect(x, row.y, MarkerWidth, row.height);
                    x += MarkerWidth;

                    Rect delRect = new Rect(row.xMax - ButtonWidth, row.y, ButtonWidth, row.height);
                    Rect dupRect = new Rect(delRect.x - ButtonWidth - 2f, row.y, ButtonWidth, row.height);

                    float fieldsWidth = dupRect.x - Gap - x;
                    float nameWidth = Mathf.Max(70f, fieldsWidth * NameFieldRatio);
                    Rect nameRect = new Rect(x, row.y, nameWidth, row.height);
                    Rect idRect = new Rect(x + nameWidth + Gap, row.y, fieldsWidth - nameWidth - Gap, row.height);

                    variant.isExpanded = EditorGUI.Foldout(foldRect, variant.isExpanded, GUIContent.none, true);
                    DrawCurrentMarker(markRect, isCurrent);
                    DrawFieldWithPlaceholder(nameRect, nameProp, T.placeholder_name);
                    DrawFieldWithPlaceholder(idRect, idProp, T.placeholder_id);

                    bool duplicate = GUI.Button(dupRect, T.duplicate);
                    bool delete = GUI.Button(delRect, T.delete);
                    EditorGUI.indentLevel = indent;

                    if (duplicate)
                    {
                        DuplicateVariant(variants, i);
                        return;
                    }

                    if (delete)
                    {
                        string title = string.IsNullOrEmpty(nameProp.stringValue)
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

                    Transform rootT = root != null ? root.transform : null;
                    VariantOperationGui.DrawRemoveList(variant.FindPropertyRelative("RemoveObjectPaths"), rootT);
                    VariantOperationGui.DrawMaterialList(variant.FindPropertyRelative("MaterialOverrides"), rootT);
                    VariantOperationGui.DrawBlendShapeList(variant.FindPropertyRelative("BlendShapeChanges"), rootT);
                }
            }

            if (GUILayout.Button(T.add_variant))
            {
                AddBlankVariant(variants);
            }
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

            AvatarVariantDefinition current = set.ResolveForBuild(pm.blueprintId, out bool viaPending);
            GUIStyle style = new GUIStyle(EditorStyles.helpBox)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(8, 8, 10, 10),
            };
            style.normal.textColor = current != null
                ? new Color(0.35f, 0.75f, 0.35f)
                : new Color(0.9f, 0.5f, 0.3f);

            string caption = current == null
                ? T.build_target_none
                : string.Format(viaPending ? T.build_target_new : T.build_target, current.Name);

            EditorGUILayout.LabelField(caption, style);
            EditorGUILayout.LabelField(T.blueprint_id,
                string.IsNullOrEmpty(pm.blueprintId) ? T.blueprint_id_unassigned : pm.blueprintId);

            EditorGUILayout.HelpBox(T.scene_untouched_help, MessageType.None);
        }

        private static void DrawPendingBanner(AvatarVariantSet set)
        {
            AvatarVariantDefinition pending = set.PendingVariant;
            if (pending == null) return;

            EditorGUILayout.HelpBox(string.Format(T.pending_banner, pending.Name), MessageType.Info);

            if (GUILayout.Button(T.cancel_pending, GUILayout.Width(110)))
            {
                AvatarVariantSwitcher.CancelPending(set);
            }

            EditorGUILayout.Space();
        }

        private static void DrawSwitcher(AvatarVariantSet set, VRC.Core.PipelineManager pm)
        {
            if (pm == null) return;

            EditorGUILayout.LabelField(T.switch_header, EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                foreach (AvatarVariantDefinition variant in set.Variants)
                {
                    if (variant == null) continue;

                    // ID が未採番のバリアントは、切り替えではなく新規アップロードの対象として選ぶ。
                    if (string.IsNullOrEmpty(variant.BlueprintId))
                    {
                        bool isPending = set.PendingVariant == variant;
                        using (new EditorGUI.DisabledScope(isPending))
                        {
                            string label = string.Format(isPending ? T.pending_label : T.mark_new_upload, variant.Name);
                            if (GUILayout.Button(label, GUILayout.Height(26)))
                            {
                                AvatarVariantSwitcher.MarkPending(set, pm, variant);
                            }
                        }

                        continue;
                    }

                    bool isCurrent = variant.BlueprintId == pm.blueprintId;
                    using (new EditorGUI.DisabledScope(isCurrent))
                    {
                        string label = string.Format(isCurrent ? T.switch_current : T.switch_to, variant.Name);
                        if (GUILayout.Button(label, GUILayout.Height(26)))
                        {
                            AvatarVariantSwitcher.SwitchTo(pm, variant);
                        }
                    }
                }
            }
        }

        // ---------- 検証 ----------

        private static void DrawWarnings(AvatarVariantSet set, GameObject root)
        {
            List<string> problems = AvatarVariantValidator.CollectProblems(set, root);
            if (problems.Count == 0) return;

            EditorGUILayout.HelpBox(string.Join("\n", problems), MessageType.Warning);
        }

        // ---------- 補助 ----------

        private static void DrawCurrentMarker(Rect rect, bool isCurrent)
        {
            if (!isCurrent) return;

            GUIStyle style = new GUIStyle(EditorStyles.label)
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

            GUIStyle style = new GUIStyle(EditorStyles.label)
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
            int index = variants.arraySize;
            variants.arraySize++;

            SerializedProperty v = variants.GetArrayElementAtIndex(index);
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

            SerializedProperty copy = variants.GetArrayElementAtIndex(index + 1);
            copy.isExpanded = false;
            SerializedProperty nameProp = copy.FindPropertyRelative("Name");
            nameProp.stringValue = nameProp.stringValue + T.copy_suffix;
            copy.FindPropertyRelative("Key").stringValue = System.Guid.NewGuid().ToString("N");
            copy.FindPropertyRelative("BlueprintId").stringValue = "";
        }
    }
}
