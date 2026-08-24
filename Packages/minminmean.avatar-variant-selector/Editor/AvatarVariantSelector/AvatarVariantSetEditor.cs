using UnityEditor;
using UnityEngine;

namespace MinMinMart.AvatarVariant.Editor
{
    /// <summary>
    /// 設定アセット単体を選んだときの表示。
    ///
    /// 対象の指定はアバタールートからの相対パスなので、どのアバターを基準に見るかが
    /// 決まらないここでは編集させない。パスの生存確認もできないため、実際の編集は
    /// アバター側の Avatar Variant Selector で行ってもらう。
    /// </summary>
    [CustomEditor(typeof(AvatarVariantSet))]
    public class AvatarVariantSetEditor : UnityEditor.Editor
    {
        private static AvatarVariantLocalizeDictionary T => AvatarVariantLocalize.T;

        public override void OnInspectorGUI()
        {
            var set = (AvatarVariantSet)target;

            AvatarVariantLocalize.DrawLanguagePopup();
            EditorGUILayout.Space();

            EditorGUILayout.HelpBox(T.asset_edit_hint, MessageType.Info);

            if (GUILayout.Button(T.asset_select_user))
            {
                SelectUser(set);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(string.Format(T.variants_header, set.Variants.Count), EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(true))
            {
                foreach (var v in set.Variants)
                {
                    if (v == null) continue;

                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.LabelField(string.IsNullOrEmpty(v.Name) ? T.asset_unnamed : v.Name,
                            EditorStyles.boldLabel);
                        EditorGUILayout.LabelField(T.blueprint_id,
                            string.IsNullOrEmpty(v.BlueprintId) ? T.blueprint_id_unassigned : v.BlueprintId);
                        EditorGUILayout.LabelField(T.asset_operations,
                            string.Format(T.asset_operations_value,
                                v.RemoveObjectPaths.Count, v.MaterialOverrides.Count, v.BlendShapeChanges.Count));
                    }
                }
            }

            var pending = set.PendingVariant;
            if (pending != null)
            {
                EditorGUILayout.HelpBox(string.Format(T.asset_pending, pending.Name), MessageType.Info);
            }
        }

        private static void SelectUser(AvatarVariantSet set)
        {
            foreach (var selector in Object.FindObjectsOfType<AvatarVariantSelector>(true))
            {
                if (selector.Set != set) continue;

                Selection.activeGameObject = selector.gameObject;
                EditorGUIUtility.PingObject(selector.gameObject);
                return;
            }

            Debug.LogWarning(T.asset_user_not_found);
        }
    }
}
