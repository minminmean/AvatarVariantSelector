using System.Collections.Generic;
using System.Linq;
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

        private static AvatarVariantLocalizeDictionary LocalizeDict => AvatarVariantLocalize.Dictionary;



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
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Set"), new GUIContent(LocalizeDict.set_asset));
            serializedObject.ApplyModifiedProperties();

            if (selector.Set == null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(LocalizeDict.set_asset_help, MessageType.Info);

                if (GUILayout.Button(LocalizeDict.create_set_asset, GUILayout.Height(26)))
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
            DrawNotices(set, root);
            EditorGUILayout.Space();

            DrawVariants(setSo, root, pm);

            // 編集はすべて SerializedProperty 経由なので、変更の検出はこれで足りる。
            // GUI.changed を見ると折りたたみの開閉まで拾ってしまう。
            if (setSo.ApplyModifiedProperties())
            {
                // ここではディスクに書かない。1 文字ごとに書き出すと再インポートが走って重い。
                // 変更済みの印だけ付けておき、書き出しは AvatarVariantSetSaver のきっかけに任せる。
                EditorUtility.SetDirty(set);
            }

            AvatarVariantSetSaver.RequestOnFocusLost();
            AvatarVariantSetSaver.SaveIfRequested(set);
        }

        // ---------- バリアント一覧 ----------

        private static void DrawVariants(SerializedObject setSo, GameObject root, VRC.Core.PipelineManager pm)
        {
            SerializedProperty variants = setSo.FindProperty("Variants");
            EditorGUILayout.LabelField(string.Format(LocalizeDict.variants_header, variants.arraySize), EditorStyles.boldLabel);

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

                    bool expanded = FoldoutState.GetExpanded(variant);
                    expanded = EditorGUI.Foldout(foldRect, expanded, GUIContent.none, true);
                    FoldoutState.SetExpanded(variant, expanded);
                    DrawCurrentMarker(markRect, isCurrent);
                    AvatarVariantSetSaver.NameWatchedField(nameProp.propertyPath);
                    DrawFieldWithPlaceholder(nameRect, nameProp, LocalizeDict.placeholder_name);
                    AvatarVariantSetSaver.NameWatchedField(idProp.propertyPath);
                    DrawFieldWithPlaceholder(idRect, idProp, LocalizeDict.placeholder_id);

                    bool duplicate = GUI.Button(dupRect, LocalizeDict.duplicate);
                    bool delete = GUI.Button(delRect, LocalizeDict.delete);
                    EditorGUI.indentLevel = indent;

                    if (duplicate)
                    {
                        DuplicateVariant(variants, i);
                        return;
                    }

                    if (delete)
                    {
                        string title = string.IsNullOrEmpty(nameProp.stringValue)
                            ? string.Format(LocalizeDict.variant_unnamed, i)
                            : nameProp.stringValue;

                        if (EditorUtility.DisplayDialog(LocalizeDict.delete_dialog_title,
                                string.Format(LocalizeDict.delete_dialog_message, title), LocalizeDict.delete, LocalizeDict.delete_dialog_cancel))
                        {
                            variants.DeleteArrayElementAtIndex(i);
                        }

                        return;
                    }

                    if (!expanded) continue;

                    EditorGUILayout.Space(2);
                    EditorGUILayout.LabelField(LocalizeDict.operations_header, EditorStyles.miniBoldLabel);

                    Transform rootT = root != null ? root.transform : null;
                    VariantOperationGui.DrawRemoveList(variant.FindPropertyRelative("RemoveObjectPaths"), rootT);
                    VariantOperationGui.DrawMaterialList(variant.FindPropertyRelative("MaterialOverrides"), rootT);
                    VariantOperationGui.DrawBlendShapeList(variant.FindPropertyRelative("BlendShapeChanges"), rootT);
                }
            }

            if (GUILayout.Button(LocalizeDict.add_variant))
            {
                AddBlankVariant(variants);
            }
        }

        // ---------- ヘッダー ----------

        private static void DrawStatus(AvatarVariantSet set, GameObject root, VRC.Core.PipelineManager pm)
        {
            if (root == null)
            {
                EditorGUILayout.HelpBox(LocalizeDict.no_avatar_root, MessageType.Error);
                return;
            }

            if (pm == null)
            {
                EditorGUILayout.HelpBox(string.Format(LocalizeDict.no_pipeline_manager, root.name), MessageType.Error);
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
                ? LocalizeDict.build_target_none
                : string.Format(viaPending ? LocalizeDict.build_target_new : LocalizeDict.build_target, current.Name);

            EditorGUILayout.LabelField(caption, style);
            EditorGUILayout.LabelField(LocalizeDict.blueprint_id,
                string.IsNullOrEmpty(pm.blueprintId) ? LocalizeDict.blueprint_id_unassigned : pm.blueprintId);

            EditorGUILayout.HelpBox(LocalizeDict.scene_untouched_help, MessageType.None);
        }

        private static void DrawPendingBanner(AvatarVariantSet set)
        {
            AvatarVariantDefinition pending = set.PendingVariant;
            if (pending == null) return;

            EditorGUILayout.HelpBox(string.Format(LocalizeDict.pending_banner, pending.Name), MessageType.Info);
            EditorGUILayout.Space();
        }

        private void DrawSwitcher(AvatarVariantSet set, VRC.Core.PipelineManager pm)
        {
            if (pm == null) return;

            EditorGUILayout.LabelField(LocalizeDict.switch_header, EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                for (int i = 0; i < set.Variants.Count; i++)
                {
                    AvatarVariantDefinition variant = set.Variants[i];
                    if (variant == null) continue;

                    string name = variant.Name;

                    // 名前が空のままでもボタンは出す。そのままだと文言が主語を欠くので、
                    // 未入力だと分かる差し込み文字に置き換える。
                    if (string.IsNullOrWhiteSpace(name)) name = LocalizeDict.asset_unnamed;

                    // 押す操作は 1 つ。上書きか新規かは Blueprint ID の有無で決まるので、
                    // 文言だけを変えて、どちらになるか分かるようにする。
                    bool isNew = string.IsNullOrEmpty(variant.BlueprintId);
                    bool isCurrent = isNew
                        ? set.PendingVariant == variant
                        : variant.BlueprintId == pm.blueprintId;

                    using (new EditorGUI.DisabledScope(isCurrent))
                    {
                        string label = string.Format(SelectLabel(isNew, isCurrent), name);
                        if (GUILayout.Button(label, GUILayout.Height(26)))
                        {
                            AvatarVariantSwitcher.SwitchTo(set, pm, variant);
                        }
                    }
                }
            }
        }

        // ---------- 名前の適用 ----------

        /// <summary>
        /// 「適用」ボタンを描き、押されたかどうかを返す。
        ///
        /// 押せないときも必ず描く。ボタン自体が出入りすると、それがコントロール ID を
        /// ずらしてしまい、防ごうとしているフォーカス外れをこのボタンが起こしてしまう。
        /// </summary>




        /// <summary>
        /// 切り替えボタンの書式。新規か上書きか、選択中かどうかで 4 通り。
        /// </summary>
        private static string SelectLabel(bool isNew, bool isCurrent)
        {
            if (isNew) return isCurrent ? LocalizeDict.pending_label : LocalizeDict.mark_new_upload;

            return isCurrent ? LocalizeDict.switch_current : LocalizeDict.switch_to;
        }


        // ---------- 検証 ----------

        /// <summary>
        /// WarnとInfoを書き出す。
        /// Warnの内容はAvatarVariantValidatorに責務がある。
        /// </summary>
        private void DrawNotices(AvatarVariantSet set, GameObject root)
        {
            List<string> warnMessages = AvatarVariantValidator.CollectProblems(set, root);
            DrawHelpBoxs(warnMessages, MessageType.Warning);
        }

        // ---------- 補助 ----------

        // 渡されたList<string>でHelpBoxを描画する。
        // フォーカスズレを防ぐため、nullを受け取った時は高さゼロの空ラベルを描画する。
        private static void DrawHelpBoxs(List<string> messages, MessageType messageType)
        {
            foreach (string message in messages)
            {
                if (!string.IsNullOrEmpty(message))
                {
                    EditorGUILayout.HelpBox(message, messageType);
                }
                else
                {
                    EditorGUILayout.LabelField(GUIContent.none, GUIContent.none, GUIStyle.none, GUILayout.Height(0));  
                }
            }
        }

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
            SerializedProperty nameProp = copy.FindPropertyRelative("Name");
            nameProp.stringValue = nameProp.stringValue + LocalizeDict.copy_suffix;
            copy.FindPropertyRelative("Key").stringValue = System.Guid.NewGuid().ToString("N");
            copy.FindPropertyRelative("BlueprintId").stringValue = "";
        }
    }
}
