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

        // 「適用」を押した時点のバリアント名。切り替えボタンの表示にはこれを使う。
        // 入力のたびにボタンが増減すると IMGUI のコントロール ID が後ろへずれ、
        // 入力中のテキスト欄からフォーカスが外れてしまうため。
        private readonly List<string> _appliedNames = new List<string>();

        private void OnEnable()
        {
            // 対象が変わったので取り直す。
            _appliedNames.Clear();
        }

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

            // 追加・削除はボタン操作なので入力中ではない。件数が動いたときだけ取り直す。
            if (_appliedNames.Count != setSo.FindProperty("Variants").arraySize)
            {
                CaptureNames(setSo);
            }

            DrawStatus(set, root, pm);
            EditorGUILayout.Space();
            DrawPendingBanner(set);
            DrawSwitcher(set, pm);
            EditorGUILayout.Space();
            DrawNotices(set, setSo, root);
            EditorGUILayout.Space();

            DrawVariants(setSo, root, pm);
            bool apply = DrawApplyButton(set, setSo);
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(setSo.FindProperty("AllowUnmatchedBlueprintId"),
                new GUIContent(LocalizeDict.allow_unmatched));

            // 編集はすべて SerializedProperty 経由なので、変更の検出はこれで足りる。
            // GUI.changed を見ると折りたたみや「適用」自身の押下まで拾ってしまう。
            if (setSo.ApplyModifiedProperties())
            {
                // ここではディスクに書かない。1 文字ごとに書き出すと再インポートが走って重い。
                // 変更済みの印だけ付けておき、実際に書くのは「適用」を押したとき。
                EditorUtility.SetDirty(set);
            }

            if (apply)
            {
                // ボタンを描いた時点では、このフレームの入力がまだ setSo の中にしかない。
                // 反映を終えたここで確定させる。
                CaptureNames(setSo);
                AssetDatabase.SaveAssetIfDirty(set);

                // 一覧が変わるので、入力欄からフォーカスを外しておく。
                GUI.FocusControl(null);
            }
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
                    DrawFieldWithPlaceholder(nameRect, nameProp, LocalizeDict.placeholder_name);
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

            if (GUILayout.Button(LocalizeDict.cancel_pending, GUILayout.Width(110)))
            {
                AvatarVariantSwitcher.CancelPending(set);
            }

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

                    // 表示に使うのは「適用」時点の名前。入力中の値は反映しない。
                    string name = i < _appliedNames.Count ? _appliedNames[i] : variant.Name;

                    // 名前が空だとボタンの文言が「 を新規アップロード対象にする」のように
                    // 主語を欠いてしまう。未入力であることは検証の警告で知らせる。
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    // ID が未採番のバリアントは、切り替えではなく新規アップロードの対象として選ぶ。
                    if (string.IsNullOrEmpty(variant.BlueprintId))
                    {
                        bool isPending = set.PendingVariant == variant;
                        using (new EditorGUI.DisabledScope(isPending))
                        {
                            string label = string.Format(isPending ? LocalizeDict.pending_label : LocalizeDict.mark_new_upload, name);
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
                        string label = string.Format(isCurrent ? LocalizeDict.switch_current : LocalizeDict.switch_to, name);
                        if (GUILayout.Button(label, GUILayout.Height(26)))
                        {
                            AvatarVariantSwitcher.SwitchTo(pm, variant);
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
        private bool DrawApplyButton(AvatarVariantSet set, SerializedObject setSo)
        {
            using (new EditorGUI.DisabledScope(!HasPendingChanges(set, setSo)))
            {
                return GUILayout.Button(LocalizeDict.apply_changes);
            }
        }

        /// <summary>
        /// 「適用」で確定させるべき変更が残っているか。通知とボタンの活性で同じ判定を使う。
        /// </summary>
        private bool HasPendingChanges(AvatarVariantSet set, SerializedObject setSo)
        {
            return HasUnappliedNames(setSo) || EditorUtility.IsDirty(set);
        }

        private void CaptureNames(SerializedObject setSo)
        {
            SerializedProperty variants = setSo.FindProperty("Variants");

            _appliedNames.Clear();
            for (int i = 0; i < variants.arraySize; i++)
            {
                _appliedNames.Add(variants.GetArrayElementAtIndex(i).FindPropertyRelative("Name").stringValue);
            }
        }

        private bool HasUnappliedNames(SerializedObject setSo)
        {
            SerializedProperty variants = setSo.FindProperty("Variants");
            if (_appliedNames.Count != variants.arraySize) return true;

            for (int i = 0; i < variants.arraySize; i++)
            {
                string current = variants.GetArrayElementAtIndex(i).FindPropertyRelative("Name").stringValue;
                if (current != _appliedNames[i]) return true;
            }

            return false;
        }

        // ---------- 検証 ----------

        /// <summary>
        /// 警告と適用状態を 1 つの箱にまとめて出す。
        ///
        /// 箱は名前の入力欄より前に描かれる。HelpBox は内部の EditorGUI.LabelField で
        /// コントロール ID を確保するため、出したり消したりすると確保数が変わり、
        /// 入力欄の ID がずれて入力中にフォーカスが外れてしまう。そこで中身が無いときも
        /// 場所取りだけは描き、見た目にだけ現れないようにしている。
        /// 行数が増減して高さが変わるぶんには影響しない。
        /// </summary>
        private void DrawNotices(AvatarVariantSet set, SerializedObject setSo, GameObject root)
        {
            List<string> messages = new List<string>();

            // 名前の未入力は「適用」済みの内容だけで判断する。
            // 入力途中の空欄で警告を出すと、消すために入力を急かす表示になってしまう。
            if (_appliedNames.Any(string.IsNullOrWhiteSpace))
            {
                messages.Add(LocalizeDict.warn_name_required);
            }

            messages.AddRange(AvatarVariantValidator.CollectProblems(set, root));

            bool hasProblem = messages.Count > 0;
            if (HasPendingChanges(set, setSo))
            {
                messages.Add(LocalizeDict.unapplied_changes);
            }

            if (messages.Count == 0)
            {
                // 出すものが無くても、同じ経路で高さ 0 の場所取りだけ描いておく。
                // HelpBox は内部で EditorGUI.LabelField を通るので、こちらも同じ
                // オーバーロードを呼べば、確保されるコントロール ID の数が揃う。
                EditorGUILayout.LabelField(GUIContent.none, GUIContent.none, GUIStyle.none,
                    GUILayout.Height(0));
                return;
            }

            EditorGUILayout.HelpBox(string.Join("\n", messages),
                hasProblem ? MessageType.Warning : MessageType.Info);
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
