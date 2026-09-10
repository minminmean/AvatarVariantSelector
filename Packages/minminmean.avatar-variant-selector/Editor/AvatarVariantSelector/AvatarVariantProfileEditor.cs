using System.Collections.Generic;
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

            DrawUsers(profile);

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

        // ボタンの幅を揃えるための定数。行ごとにラベルの長さが違っても、ボタンは同じ幅で並べたい。
        private const float UserButtonWidth = 90f;

        /// <summary>
        /// このプロファイルを使っているアバターの一覧を描く。
        /// </summary>
        private static void DrawUsers(AvatarVariantProfile profile)
        {
            List<AvatarVariantProfileUser> users = AvatarVariantProfileUserCollector.Collect(profile);

            EditorGUILayout.LabelField(string.Format(LocalizeDict.asset_users_header, users.Count), EditorStyles.boldLabel);

            if (users.Count == 0)
            {
                EditorGUILayout.HelpBox(LocalizeDict.asset_users_empty, MessageType.Info);
                return;
            }

            foreach (AvatarVariantProfileUser user in users)
            {
                DrawUserRow(user);
            }
        }

        /// <summary>
        /// 一覧の 1 行。左にラベル、右にボタン。
        /// </summary>
        private static void DrawUserRow(AvatarVariantProfileUser user)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(BuildUserLabel(user));

                if (user.Object != null)
                {
                    if (GUILayout.Button(LocalizeDict.asset_user_select, GUILayout.Width(UserButtonWidth)))
                    {
                        Selection.activeGameObject = user.Object;
                        EditorGUIUtility.PingObject(user.Object);
                    }
                }
                else if (!user.Missing)
                {
                    // 実体を引けないのがシーンを開いていないせいなら、シーンの方を指し示す。
                    using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(user.ScenePath)))
                    {
                        if (GUILayout.Button(LocalizeDict.asset_user_show_scene, GUILayout.Width(UserButtonWidth)))
                        {
                            Object sceneAsset = AssetDatabase.LoadAssetAtPath<Object>(user.ScenePath);
                            EditorGUIUtility.PingObject(sceneAsset);
                        }
                    }
                }
                else
                {
                    // 登録はあるのに実体が見つからない行には、押せる操作が無い。
                    // 幅だけ空けて、他の行とラベルの切れ目を揃える。
                    GUILayout.Space(UserButtonWidth);
                }
            }
        }

        /// <summary>
        /// シーン名とヒエラルキーパス、必要なら状態も添えたラベル。
        /// </summary>
        private static string BuildUserLabel(AvatarVariantProfileUser user)
        {
            string sceneName = string.IsNullOrEmpty(user.SceneName) ? "?" : user.SceneName;
            string label = $"{sceneName} / {user.ObjectPath}";

            if (user.Missing) return $"{label}  [{LocalizeDict.asset_user_missing}]";
            if (!user.Registered) return $"{label}  [{LocalizeDict.asset_user_unregistered}]";

            return label;
        }
    }
}
