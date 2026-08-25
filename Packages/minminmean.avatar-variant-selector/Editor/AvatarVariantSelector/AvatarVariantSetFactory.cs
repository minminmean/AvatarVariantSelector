using System.IO;
using UnityEditor;
using UnityEngine;

namespace MinMinMart.AvatarVariant.Editor
{
    /// <summary>
    /// 設定アセットを作る。
    ///
    /// 置き場所はシーンと同じフォルダにする。シーンが未保存のときだけ Assets 直下に逃がす。
    /// </summary>
    internal static class AvatarVariantSetFactory
    {
        private static AvatarVariantLocalizeDictionary T => AvatarVariantLocalize.T;

        /// <summary>
        /// 設定アセットを作り、<paramref name="selector"/> に割り当てる。
        /// </summary>
        internal static void CreateForSelector(AvatarVariantSelector selector)
        {
            string scenePath = selector.gameObject.scene.path;
            string dir = string.IsNullOrEmpty(scenePath) ? "Assets" : Path.GetDirectoryName(scenePath);
            string baseName = string.IsNullOrEmpty(scenePath)
                ? selector.gameObject.name
                : Path.GetFileNameWithoutExtension(scenePath);

            string path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{baseName}_Variants.asset");
            AvatarVariantSet set = ScriptableObject.CreateInstance<AvatarVariantSet>();
            AssetDatabase.CreateAsset(set, path);
            AssetDatabase.SaveAssetIfDirty(set);

            Undo.RecordObject(selector, "Assign variant set");
            selector.Set = set;
            EditorUtility.SetDirty(selector);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(selector.gameObject.scene);

            Debug.Log(string.Format(T.log_created_asset, path), set);
        }
    }
}
