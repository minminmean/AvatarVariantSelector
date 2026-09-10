using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MinMinMart.AvatarVariant.Editor
{
    /// <summary>
    /// プロファイルを使っているアバター 1 件分。
    /// </summary>
    internal class AvatarVariantProfileUser
    {
        // 所属シーンの名前（拡張子なし）。解決できなければ空。
        public string SceneName = "";

        // シーンアセットのパス。シーンを開いていなくても分かるので、Ping に使う。
        public string ScenePath = "";

        // シーンルートからのヒエラルキーパス。
        public string ObjectPath = "";

        // 解決できた実体。シーンを開いていない場合は null。
        public GameObject Object;

        // プロファイルの Owners に登録済みかどうか。
        public bool Registered;

        // 登録はあるが、シーンが開いているのに実体が見つからないかどうか。
        public bool Missing;
    }

    /// <summary>
    /// プロファイルを使っているセレクターを集める。
    ///
    /// AvatarVariantNoticeCollector と同じく、ここは一覧を作って返すだけで表示には関与しない。
    /// また Owners を書き換えることもしない。見ただけでアセットが変わる作りは避けたい。
    /// 古い登録の削除は AvatarVariantProfileOwnership の判定側の責務。
    /// </summary>
    internal static class AvatarVariantProfileUserCollector
    {
        // GlobalObjectIdentifierToObjectSlow / GetGlobalObjectIdSlow は名前のとおり重く、
        // Inspector の再描画は頻繁に走るので、AvatarVariantProfileOwnership.CheckForGui と同じ作りで
        // 短い間だけ結果を使い回す。
        private const double GuiCacheSeconds = 0.5;
        private static AvatarVariantProfile _cachedProfile;
        private static List<AvatarVariantProfileUser> _cachedUsers;
        private static double _cacheExpiry;

        /// <summary>
        /// <paramref name="profile"/> を使っているセレクターの一覧。登録済みを先に、未登録を後に並べる。
        /// </summary>
        internal static List<AvatarVariantProfileUser> Collect(AvatarVariantProfile profile)
        {
            if (profile == _cachedProfile && EditorApplication.timeSinceStartup < _cacheExpiry)
            {
                return _cachedUsers;
            }

            _cachedUsers = CollectUncached(profile);
            _cachedProfile = profile;
            _cacheExpiry = EditorApplication.timeSinceStartup + GuiCacheSeconds;

            return _cachedUsers;
        }

        private static List<AvatarVariantProfileUser> CollectUncached(AvatarVariantProfile profile)
        {
            List<AvatarVariantProfileUser> users = new List<AvatarVariantProfileUser>();
            if (profile == null) return users;

            HashSet<string> registeredIds = new HashSet<string>();

            foreach (AvatarVariantProfileOwner owner in profile.Owners)
            {
                if (owner == null) continue;

                registeredIds.Add(owner.GlobalId);
                users.Add(ResolveOwner(owner));
            }

            // Owners のどれとも一致しない、開いているシーン上の参照を未登録として拾う。
            // 未登録は登録済みより後ろに積む。
            foreach (AvatarVariantSelector selector in Object.FindObjectsOfType<AvatarVariantSelector>(true))
            {
                if (selector.Profile != profile) continue;

                string globalId = GlobalObjectId.GetGlobalObjectIdSlow(selector).ToString();
                if (registeredIds.Contains(globalId)) continue;

                users.Add(ResolveUnregistered(selector));
            }

            return users;
        }

        /// <summary>
        /// Owners の 1 件を実体まで解決する。
        /// </summary>
        private static AvatarVariantProfileUser ResolveOwner(AvatarVariantProfileOwner owner)
        {
            string scenePath = AssetDatabase.GUIDToAssetPath(owner.SceneGuid);
            string sceneName = string.IsNullOrEmpty(scenePath) ? "" : Path.GetFileNameWithoutExtension(scenePath);

            bool sceneOpen = IsSceneOpen(scenePath);

            GameObject resolvedObject = null;
            if (GlobalObjectId.TryParse(owner.GlobalId, out GlobalObjectId id))
            {
                AvatarVariantSelector selector = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as AvatarVariantSelector;
                if (selector != null) resolvedObject = selector.gameObject;
            }

            return new AvatarVariantProfileUser
            {
                SceneName = sceneName,
                ScenePath = scenePath,
                ObjectPath = owner.ObjectPath,
                Object = resolvedObject,
                Registered = true,
                // 実体が見つからないのが「シーンを開いていないから」なのか「本当に無くなったから」なのかは、
                // シーンが開いているかどうかでしか区別できない。
                Missing = sceneOpen && resolvedObject == null,
            };
        }

        /// <summary>
        /// Owners に無い、開いているシーン上のセレクター 1 件分。
        /// </summary>
        private static AvatarVariantProfileUser ResolveUnregistered(AvatarVariantSelector selector)
        {
            Scene scene = selector.gameObject.scene;
            string scenePath = scene.path;
            string sceneName = string.IsNullOrEmpty(scenePath) ? "" : Path.GetFileNameWithoutExtension(scenePath);

            return new AvatarVariantProfileUser
            {
                SceneName = sceneName,
                ScenePath = scenePath,
                ObjectPath = AvatarVariantProfileOwnership.GetHierarchyPath(selector.transform),
                Object = selector.gameObject,
                Registered = false,
                Missing = false,
            };
        }

        private static bool IsSceneOpen(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath)) return false;

            Scene scene = SceneManager.GetSceneByPath(scenePath);
            return scene.IsValid() && scene.isLoaded;
        }
    }
}
