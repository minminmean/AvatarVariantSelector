using System.IO;
using UnityEditor;
using UnityEngine;

namespace MinMinMart.AvatarVariant.Editor
{
    /// <summary>
    /// プロファイルアセットを作る。
    ///
    /// 置き場所は固定のフォルダにまとめ、ファイル名はアバター名にする。
    /// シーンの隣に置くとアバターを複数のシーンで扱ったときに散らばるので、
    /// 「どのアバターのプロファイルか」だけで探せる形にしている。
    /// </summary>
    internal static class AvatarVariantProfileFactory
    {
        // プロファイルアセットの置き場所。無ければ作る。
        private const string ProfileFolder = "Assets/MinMinMart/AvatarVariantSelector/Profiles";

        // アバター名が取れない、または使える文字が残らなかったときのファイル名。
        private const string FallbackFileName = "AvatarVariantProfile";

        private static AvatarVariantLocalizeDictionary LocalizeDict => AvatarVariantLocalize.Dictionary;

        /// <summary>
        /// プロファイルアセットを作り、<paramref name="selector"/> に割り当てる。
        /// </summary>
        internal static void CreateForSelector(AvatarVariantSelector selector)
        {
            EnsureFolder(ProfileFolder);

            string fileName = SanitizeFileName(FindAvatarName(selector));
            string path = AssetDatabase.GenerateUniqueAssetPath($"{ProfileFolder}/{fileName}.asset");

            AvatarVariantProfile profile = ScriptableObject.CreateInstance<AvatarVariantProfile>();
            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.SaveAssetIfDirty(profile);

            Undo.RecordObject(selector, "Assign variant profile");
            selector.Profile = profile;
            EditorUtility.SetDirty(selector);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(selector.gameObject.scene);

            Debug.Log(string.Format(LocalizeDict.log_created_asset, path), profile);
        }

        /// <summary>
        /// アバタールートの名前。ルートが見つからなければコンポーネントが付いているオブジェクトの名前。
        /// </summary>
        private static string FindAvatarName(AvatarVariantSelector selector)
        {
            Transform root = AvatarRootFinder.Find(selector.transform);
            return root != null ? root.name : selector.gameObject.name;
        }

        /// <summary>
        /// ファイル名に使えない文字を _ に置き換える。
        ///
        /// アバター名はユーザーが自由に付けられるので、そのままパスに埋めると
        /// スラッシュやコロンでアセットの生成に失敗する。
        /// </summary>
        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return FallbackFileName;

            string sanitized = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));

            // 末尾のドットと空白は Windows が扱えないので落とす。
            sanitized = sanitized.Trim().TrimEnd('.').Trim();

            return string.IsNullOrEmpty(sanitized.Replace("_", "")) ? FallbackFileName : sanitized;
        }

        /// <summary>
        /// フォルダが無ければ作る。Assets から 1 階層ずつ辿る。
        /// </summary>
        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;

            string[] parts = folder.Split('/');
            string current = parts[0];

            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }
}
