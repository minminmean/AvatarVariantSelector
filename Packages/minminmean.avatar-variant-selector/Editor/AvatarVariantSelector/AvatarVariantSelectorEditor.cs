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
    /// 通知欄の内容は <see cref="AvatarVariantNoticeCollector"/>、切り替えは <see cref="AvatarVariantSwitcher"/>、
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

        // 一覧を足し引きした直後に、選択を選び直す必要があるか。
        // 描画の途中では SerializedObject に反映されていないので、印だけ付けて後で処理する。
        private bool _selectionNeedsFixing;

        // 選択中のバリアントを削除したか。
        // このとき PipelineManager に残っている ID は、消えたバリアントのものになる。
        private bool _deletedCurrentVariant;



        public override void OnInspectorGUI()
        {
            AvatarVariantSelector selector = (AvatarVariantSelector)target;
            Transform rootTransform = AvatarRootFinder.Find(selector.transform);
            GameObject root = rootTransform != null ? rootTransform.gameObject : null;
            VRC.Core.PipelineManager pm = rootTransform != null
                ? rootTransform.GetComponent<VRC.Core.PipelineManager>()
                : null;

            AvatarVariantLocalize.DrawLanguagePopup();

            serializedObject.Update();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Profile"), new GUIContent(LocalizeDict.profile_asset));
            if (EditorGUI.EndChangeCheck()) _selectionNeedsFixing = true;
            serializedObject.ApplyModifiedProperties();

            if (selector.Profile == null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(LocalizeDict.profile_asset_help, MessageType.Info);

                if (GUILayout.Button(LocalizeDict.create_profile_asset, GUILayout.Height(26)))
                {
                    AvatarVariantProfileFactory.CreateForSelector(selector);
                }

                return;
            }

            AvatarVariantProfile profile = selector.Profile;
            SerializedObject profileSo = new SerializedObject(profile);
            profileSo.Update();
            EditorGUILayout.Space();

            DrawStatus(profile, root, pm);
            EditorGUILayout.Space();

            DrawVariants(profileSo, root, pm);
            EditorGUILayout.Space();

            // 切り替えボタンと警告は入力欄より後ろに置く。IMGUI はコントロールの並び順で
            // フォーカスを覚えているので、入力欄より手前に出入りするものがあると、
            // 打っている最中にフォーカスが隣のコントロールへ移ってしまう。
            DrawSwitcher(profile, pm);
            EditorGUILayout.Space();
            DrawNotices(profile, root, pm != null ? pm.blueprintId : null);

            // 編集はすべて SerializedProperty 経由なので、変更の検出はこれで足りる。
            // GUI.changed を見ると折りたたみの開閉まで拾ってしまう。
            if (profileSo.ApplyModifiedProperties())
            {
                // ここではディスクに書かない。1 文字ごとに書き出すと再インポートが走って重い。
                // 変更済みの印だけ付けておき、書き出しは AvatarVariantProfileSaver のきっかけに任せる。
                EditorUtility.SetDirty(profile);
            }

            // 反映が済んでから選び直す。追加したバリアントは、ここまで来ないと
            // profile.Variants に現れない。
            if (_selectionNeedsFixing)
            {
                FixSelection(profile, pm);
            }

            AvatarVariantProfileSaver.RequestOnFocusLost();
            AvatarVariantProfileSaver.SaveIfRequested(profile);
        }

        /// <summary>
        /// バリアントがあるのに未選択、という状態を残さない。
        ///
        /// 一覧を足し引きしたときとプロファイルを差し替えたときだけ呼ぶ。常時呼ぶと、
        /// Blueprint ID を打ち替えている途中の一瞬だけ一致しなくなった隙に選び直してしまう。
        /// </summary>
        private void FixSelection(AvatarVariantProfile profile, VRC.Core.PipelineManager pm)
        {
            bool staleId = _deletedCurrentVariant;
            _selectionNeedsFixing = false;
            _deletedCurrentVariant = false;

            if (pm == null || profile == null) return;
            if (profile.ResolveForBuild(pm.blueprintId, out bool _) != null) return;

            // 消したバリアントの ID が残っているだけなら選び直してよい。
            // そうでなく ID が入っている場合は、一覧に無い実在のアバターを指しているので触らない。
            // 勝手に選び直すと、そのアバターではない先へ上書きアップロードしてしまう。
            if (!staleId && !string.IsNullOrEmpty(pm.blueprintId)) return;

            AvatarVariantDefinition first = profile.Variants.FirstOrDefault(v => v != null);
            if (first == null) return;

            AvatarVariantSwitcher.SwitchTo(profile, pm, first);
        }

        // ---------- バリアント一覧 ----------

        private void DrawVariants(SerializedObject profileSo, GameObject root, VRC.Core.PipelineManager pm)
        {
            SerializedProperty variants = profileSo.FindProperty("Variants");
            EditorGUILayout.LabelField(string.Format(LocalizeDict.variants_header, variants.arraySize), EditorStyles.boldLabel);

            AvatarVariantProfile profile = (AvatarVariantProfile)profileSo.targetObject;
            AvatarVariantDefinition pending = profile.PendingVariant;

            for (int i = 0; i < variants.arraySize; i++)
            {
                SerializedProperty variant = variants.GetArrayElementAtIndex(i);
                SerializedProperty nameProp = variant.FindPropertyRelative("Name");
                SerializedProperty idProp = variant.FindPropertyRelative("BlueprintId");

                // 選択中の印は切り替えボタンと同じ基準で付ける。Blueprint ID がまだ無い
                // バリアントは ID で判別できないので、新規アップロード待ちの指定を見る。
                AvatarVariantDefinition definition = i < profile.Variants.Count ? profile.Variants[i] : null;
                bool isCurrent = string.IsNullOrEmpty(idProp.stringValue)
                    ? definition != null && definition == pending
                    : pm != null && idProp.stringValue == pm.blueprintId;

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
                    AvatarVariantProfileSaver.NameWatchedField(nameProp.propertyPath);
                    DrawFieldWithPlaceholder(nameRect, nameProp, LocalizeDict.placeholder_name);
                    AvatarVariantProfileSaver.NameWatchedField(idProp.propertyPath);
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
                            _selectionNeedsFixing = true;
                            _deletedCurrentVariant = isCurrent;
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
                _selectionNeedsFixing = true;
            }
        }

        // ---------- ヘッダー ----------

        private static void DrawStatus(AvatarVariantProfile profile, GameObject root, VRC.Core.PipelineManager pm)
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

            AvatarVariantDefinition current = profile.ResolveForBuild(pm.blueprintId, out bool _);
            // 枠と中身を分けて描く。1 つのラベルに改行を入れると行間を詰められないので、
            // 枠だけ先に用意して、その中に 1 行ずつ置く。
            GUIStyle boxStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(14, 8, 10, 10),
            };

            GUIStyle lineStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true,
            };
            lineStyle.normal.textColor = current != null
                ? new Color(0.35f, 0.75f, 0.35f)
                : new Color(0.9f, 0.5f, 0.3f);

            // 名前が空でも書式は変えない。差し込む文字だけ「(名前なし)」に替えて、
            // 「ビルド対象: 〜」の並びを崩さないようにする。
            string caption = LocalizeDict.build_target_none;
            if (current != null)
            {
                string name = string.IsNullOrWhiteSpace(current.Name) ? LocalizeDict.asset_unnamed : current.Name;
                caption = string.Format(LocalizeDict.build_target, name);
            }

            using (new EditorGUILayout.VerticalScope(boxStyle))
            {
                GUILayout.Label(caption, lineStyle);

                // 2 行目はアップロード先の Blueprint ID。まだ採番されていなければ、
                // 新規アバターとして上がる旨を出す。
                //
                // 未選択のときは出さない。バリアントを選んだ後に全部消すと PipelineManager 側に
                // ID が残るので、そのまま出すとどこにも紐づかない ID を掲げることになる。
                if (current != null)
                {
                    string id = string.IsNullOrEmpty(pm.blueprintId)
                        ? LocalizeDict.blueprint_id_new_upload
                        : pm.blueprintId;

                    GUILayout.Space(3f);
                    GUILayout.Label(string.Format(LocalizeDict.blueprint_id_line, id), lineStyle);
                }
            }
        }

        private void DrawSwitcher(AvatarVariantProfile profile, VRC.Core.PipelineManager pm)
        {
            if (pm == null) return;

            EditorGUILayout.LabelField(LocalizeDict.switch_header, EditorStyles.boldLabel);

            if (profile.Variants.All(v => v == null))
            {
                EditorGUILayout.HelpBox(LocalizeDict.switch_no_variants, MessageType.Info);
                return;
            }

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                for (int i = 0; i < profile.Variants.Count; i++)
                {
                    AvatarVariantDefinition variant = profile.Variants[i];
                    if (variant == null) continue;

                    string name = variant.Name;

                    // 名前が空のままでもボタンは出す。そのままだと文言が主語を欠くので、
                    // 未入力だと分かる差し込み文字に置き換える。
                    if (string.IsNullOrWhiteSpace(name)) name = LocalizeDict.asset_unnamed;

                    // 押す操作は 1 つ。上書きか新規かは Blueprint ID の有無で決まるので、
                    // 文言だけを変えて、どちらになるか分かるようにする。
                    bool isNew = string.IsNullOrEmpty(variant.BlueprintId);
                    bool isCurrent = isNew
                        ? profile.PendingVariant == variant
                        : variant.BlueprintId == pm.blueprintId;

                    using (new EditorGUI.DisabledScope(isCurrent))
                    {
                        string label = string.Format(SelectLabel(isNew, isCurrent), name);
                        if (GUILayout.Button(label, GUILayout.Height(26)))
                        {
                            AvatarVariantSwitcher.SwitchTo(profile, pm, variant);
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
        /// 切り替えボタンの書式。
        ///
        /// 選択中の文言は新規でも上書きでも共通。選択中でないときだけ、
        /// これから何が起きるかが変わるので新規と上書きで分ける。
        /// </summary>
        private static string SelectLabel(bool isNew, bool isCurrent)
        {
            if (isCurrent) return LocalizeDict.switch_current;

            return isNew ? LocalizeDict.mark_new_upload : LocalizeDict.switch_to;
        }


        // ---------- 検証 ----------

        /// <summary>
        /// WarnとInfoを書き出す。
        /// どちらも内容はAvatarVariantNoticeCollectorに責務がある。
        /// </summary>
        private void DrawNotices(AvatarVariantProfile profile, GameObject root, string blueprintId)
        {
            DrawHelpBoxs(AvatarVariantNoticeCollector.CollectProblems(profile, root, blueprintId), MessageType.Warning);
            DrawHelpBoxs(AvatarVariantNoticeCollector.CollectInfos(profile), MessageType.Info);
        }

        // ---------- 補助 ----------

        // 渡された List<string> で HelpBox を描画する。
        //
        // 同じ文言は最初の 1 件だけ描く。
        private static void DrawHelpBoxs(List<string> messages, MessageType messageType)
        {
            HashSet<string> drawnMessages = new HashSet<string>();

            foreach (string message in messages)
            {
                if (!drawnMessages.Add(message)) continue;

                EditorGUILayout.HelpBox(message, messageType);
            }
        }

        // 現在のバリアントに ● を付ける。
        //
        // EditorGUI.LabelField ではなく GUI.Label を使う。前者はコントロール ID を消費するので、
        // 切り替えで ● の位置が動くたびに、後ろにある入力欄の ID がズレてフォーカスが外れる。
        // GUI.Label は描くだけで ID を消費しないため、出ていても出ていなくても数が変わらない。
        private static void DrawCurrentMarker(Rect rect, bool isCurrent)
        {
            if (!isCurrent) return;

            GUIStyle style = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(0, 0, 0, 0),
            };
            style.normal.textColor = new Color(0.35f, 0.75f, 0.35f);
            GUI.Label(rect, "●", style);
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
            // GUI.Label にしているのは ● と同じ理由で、欄の出入りで ID をずらさないため。
            GUI.Label(new Rect(rect.x + 2f, rect.y, rect.width - 4f, rect.height), placeholder, style);
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
