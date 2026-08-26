using UnityEditor;
using UnityEngine;

namespace MinMinMart.AvatarVariant.Editor
{
    /// <summary>
    /// プロファイル単体を選んだときの表示。
    ///
    /// 対象の指定はアバタールートからの相対パスなので、どのアバターを基準に見るかが
    /// 決まらないここでは編集させない。パスの生存確認もできないため、実際の編集は
    /// アバター側の Avatar Variant Selector で行ってもらう。
    /// </summary>
    [CustomEditor(typeof(AvatarVariantProfile))]
    public class AvatarVariantProfileEditor : UnityEditor.Editor
    {
        private static AvatarVariantLocalizeDictionary LocalizeDict => AvatarVariantLocalize.Dictionary;

        public override void OnInspectorGUI()
        {
            AvatarVariantProfile profile = (AvatarVariantProfile)target;

            AvatarVariantLocalize.DrawLanguagePopup();
            EditorGUILayout.Space();

            EditorGUILayout.HelpBox(LocalizeDict.asset_edit_hint, MessageType.Info);

            if (GUILayout.Button(LocalizeDict.asset_select_user))
            {
                SelectUser(profile);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(string.Format(LocalizeDict.variants_header, profile.Variants.Count), EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(true))
            {
                foreach (AvatarVariantDefinition v in profile.Variants)
                {
                    if (v == null) continue;

                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.LabelField(string.IsNullOrEmpty(v.Name) ? LocalizeDict.asset_unnamed : v.Name,
                            EditorStyles.boldLabel);
                        EditorGUILayout.LabelField(LocalizeDict.blueprint_id,
                            string.IsNullOrEmpty(v.BlueprintId) ? LocalizeDict.blueprint_id_unassigned : v.BlueprintId);
                        EditorGUILayout.LabelField(LocalizeDict.asset_operations,
                            string.Format(LocalizeDict.asset_operations_value,
                                v.RemoveObjectPaths.Count, v.MaterialOverrides.Count, v.BlendShapeChanges.Count));
                    }
                }
            }

            AvatarVariantDefinition pending = profile.PendingVariant;
            if (pending != null)
            {
                EditorGUILayout.HelpBox(string.Format(LocalizeDict.asset_pending, pending.Name), MessageType.Info);
            }
        }

        private static void SelectUser(AvatarVariantProfile profile)
        {
            foreach (AvatarVariantSelector selector in Object.FindObjectsOfType<AvatarVariantSelector>(true))
            {
                if (selector.Profile != profile) continue;

                Selection.activeGameObject = selector.gameObject;
                EditorGUIUtility.PingObject(selector.gameObject);
                return;
            }

            Debug.LogWarning(LocalizeDict.asset_user_not_found);
        }
    }
}
