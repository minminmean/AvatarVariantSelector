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
        /// アップロード先をこのバリアントに切り替える。
        ///
        /// 上書きになるか新規になるかは、バリアントの Blueprint ID の有無だけで決まる。
        /// 入っていればその ID を PipelineManager に書き、そのアバターへ上書きアップロードになる。
        /// 空欄なら PipelineManager の ID も空にして、新規アバターとしてアップロードさせる。
        /// </summary>
        internal static void SwitchTo(AvatarVariantSet set, VRC.Core.PipelineManager pm,
            AvatarVariantDefinition variant)
        {
            bool isNew = string.IsNullOrEmpty(variant.BlueprintId);

            Undo.RecordObject(set, "Switch variant");

            if (isNew)
            {
                // ID が空のバリアントは ID で見分けられない。どれを選んだかを控えておき、
                // 採番されたら PendingBlueprintIdWatcher がこのバリアントへ書き写す。
                if (string.IsNullOrEmpty(variant.Key))
                {
                    variant.Key = System.Guid.NewGuid().ToString("N");
                }

                set.PendingVariantKey = variant.Key;
            }
            else
            {
                // ID で決まるので控えは要らない。残すとバナーが古い選択を指したままになる。
                set.PendingVariantKey = "";
            }

            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssetIfDirty(set);

            WriteBlueprintId(pm, variant.BlueprintId);

            Debug.Log(isNew
                ? string.Format(LocalizeDict.log_marked_pending, variant.Name)
                : string.Format(LocalizeDict.log_switched, variant.Name, variant.BlueprintId));
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
