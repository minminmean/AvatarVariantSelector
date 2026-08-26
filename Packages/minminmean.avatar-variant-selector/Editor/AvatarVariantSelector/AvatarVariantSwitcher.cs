using UnityEditor;
using UnityEngine;

namespace MinMinMart.AvatarVariant.Editor
{
    /// <summary>
    /// アップロード先を切り替える。
    ///
    /// どのバリアントをビルドするかは PipelineManager の Blueprint ID で決まるので、
    /// 切り替えとは ID を書き換えることそのものになる。シーンと設定アセットの両方に
    /// 書き込みが発生するため、描画側から分けてここにまとめている。
    /// </summary>
    internal static class AvatarVariantSwitcher
    {
        private static AvatarVariantLocalizeDictionary LocalizeDict => AvatarVariantLocalize.Dictionary;

        /// <summary>
        /// 既存のアップロード先へ切り替える。
        /// </summary>
        internal static void SwitchTo(VRC.Core.PipelineManager pm, AvatarVariantDefinition variant)
        {
            WriteBlueprintId(pm, variant.BlueprintId);
            Debug.Log(string.Format(LocalizeDict.log_switched, variant.Name, variant.BlueprintId));
        }

        /// <summary>
        /// バリアントを新規アップロード対象にする。
        /// 既存アバターを上書きしないよう、PipelineManager の ID も空にする。
        /// </summary>
        internal static void MarkPending(AvatarVariantSet set, VRC.Core.PipelineManager pm,
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

            Debug.Log(string.Format(LocalizeDict.log_marked_pending, variant.Name));
        }

        /// <summary>
        /// 新規アップロード待ちの指定を取り消す。
        /// </summary>
        internal static void CancelPending(AvatarVariantSet set)
        {
            Undo.RecordObject(set, "Cancel pending variant");
            set.PendingVariantKey = "";
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssetIfDirty(set);
        }

        /// <summary>
        /// PipelineManager の Blueprint ID を書き換える。
        /// このフィールドは Inspector に出ないので SerializedProperty 経由で触る。
        /// </summary>
        private static void WriteBlueprintId(VRC.Core.PipelineManager pm, string value)
        {
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
