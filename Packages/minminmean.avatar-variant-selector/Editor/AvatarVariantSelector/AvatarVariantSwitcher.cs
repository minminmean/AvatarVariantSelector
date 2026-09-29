using UnityEditor;
using UnityEngine;

namespace MinMinMart.AvatarVariant.Editor
{
    /// <summary>
    /// アップロード先を切り替える。
    ///
    /// どのバリアントをビルドするかは PipelineManager の Blueprint ID で決まるので、
    /// 切り替えとは ID を書き換えることそのものになる。シーンとプロファイルの両方に
    /// 書き込みが発生するため、描画側から分けてここにまとめている。
    /// </summary>
    internal static class AvatarVariantSwitcher
    {
        private static AvatarVariantLocalizeDictionary LocalizeDict => AvatarVariantLocalize.Dictionary;

        /// <summary>
        /// アップロード先をこのバリアントに切り替える。
        ///
        /// 上書きになるか新規になるかは、バリアントの Blueprint ID の有無だけで決まる。
        /// 入っていればその ID を PipelineManager に書き、そのアバターへ上書きアップロードになる。
        /// 空欄なら PipelineManager の ID も空にして、新規アバターとしてアップロードさせる。
        /// </summary>
        internal static void SwitchTo(AvatarVariantProfile profile, VRC.Core.PipelineManager pm,
            AvatarVariantDefinition variant)
        {
            bool isNew = string.IsNullOrEmpty(variant.BlueprintId);

            Undo.RecordObject(profile, "Switch variant");

            SetPendingFor(profile, variant);

            EditorUtility.SetDirty(profile);
            AvatarVariantProfileSaver.Save(profile);

            WriteBlueprintId(pm, variant.BlueprintId);

            Debug.Log(isNew
                ? string.Format(LocalizeDict.log_marked_pending, variant.Name)
                : string.Format(LocalizeDict.log_switched, variant.Name, variant.BlueprintId));
        }

        /// <summary>
        /// 選択中のバリアントの Blueprint ID 欄が書き換えられたとき、PipelineManager をそれに合わせる。
        ///
        /// 選択中のバリアントの ID を直すのは「このバリアントの上げ先を変える」という操作なので、
        /// 選択はそのまま保つ。合わせないと古い ID が PipelineManager に残り、どのバリアントにも無い
        /// ID として扱われてビルドが止まる。
        ///
        /// 入力の 1 文字ごとに呼ばれるので、ログは出さずプロファイルもディスクへは書かない。
        /// 書き出しは入力欄の書き換えと同じく AvatarVariantProfileSaver のきっかけに任せる。
        /// </summary>
        internal static void FollowEditedId(AvatarVariantProfile profile, VRC.Core.PipelineManager pm,
            AvatarVariantDefinition variant)
        {
            Undo.RecordObject(profile, "Edit blueprint ID");
            SetPendingFor(profile, variant);
            EditorUtility.SetDirty(profile);

            WriteBlueprintId(pm, variant.BlueprintId);
        }

        /// <summary>
        /// <paramref name="variant"/> を選ぶにあたって、新規アップロード待ちの控えを付け外しする。
        /// </summary>
        private static void SetPendingFor(AvatarVariantProfile profile, AvatarVariantDefinition variant)
        {
            if (!string.IsNullOrEmpty(variant.BlueprintId))
            {
                // ID で決まるので控えは要らない。残すとバナーが古い選択を指したままになる。
                profile.PendingVariantKey = "";
                return;
            }

            // ID が空のバリアントは ID で見分けられない。どれを選んだかを控えておき、
            // アップロードに成功したら PendingBlueprintIdWatcher が採番された ID をこのバリアントへ書き写す。
            if (string.IsNullOrEmpty(variant.Key))
            {
                variant.Key = System.Guid.NewGuid().ToString("N");
            }

            profile.PendingVariantKey = variant.Key;
        }

        /// <summary>
        /// PipelineManager に入っている、どのバリアントにも無い Blueprint ID を消す。
        ///
        /// アップロードを途中でキャンセルすると、アバターは作られないまま ID だけが採番されて残る。
        /// その後始末に使う。消した後は、新規アップロード待ちのバリアントがあればそれが選ばれる。
        /// </summary>
        internal static void ClearBlueprintId(VRC.Core.PipelineManager pm)
        {
            string old = pm.blueprintId;
            WriteBlueprintId(pm, "");

            Debug.Log(string.Format(LocalizeDict.log_cleared_blueprint_id, old));
        }

        /// <summary>
        /// PipelineManager に入っている Blueprint ID を、ID が未採番のバリアントに登録する。
        ///
        /// ID で選ばれるようになるので、そのバリアントが新規アップロード待ちだった場合は控えを消す。
        /// </summary>
        internal static void RegisterBlueprintId(AvatarVariantProfile profile, VRC.Core.PipelineManager pm,
            AvatarVariantDefinition variant)
        {
            Undo.RecordObject(profile, "Register blueprint ID");

            variant.BlueprintId = pm.blueprintId;
            if (!string.IsNullOrEmpty(variant.Key) && profile.PendingVariantKey == variant.Key)
            {
                profile.PendingVariantKey = "";
            }

            EditorUtility.SetDirty(profile);
            AvatarVariantProfileSaver.Save(profile);

            Debug.Log(string.Format(LocalizeDict.log_registered_blueprint_id, pm.blueprintId, variant.Name));
        }

        /// <summary>
        /// PipelineManager の Blueprint ID を書き換える。
        /// このフィールドは Inspector に出ないので SerializedProperty 経由で触る。
        /// </summary>
        private static void WriteBlueprintId(VRC.Core.PipelineManager pm, string value)
        {
            // 同じ値なら触らない。書くとシーンに変更済みの印が付くので、
            // 中身が変わらないのに保存を促されることになる。
            if (pm.blueprintId == value) return;

            Undo.RecordObject(pm, "Set blueprint ID");

            SerializedObject so = new SerializedObject(pm);
            SerializedProperty prop = so.FindProperty("blueprintId");
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
    }
}
