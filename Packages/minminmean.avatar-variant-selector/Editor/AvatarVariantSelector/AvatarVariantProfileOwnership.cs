using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MinMinMart.AvatarVariant.Editor
{
    /// <summary>
    /// プロファイルアセットの「持ち主」を判定・記録する。
    ///
    /// シーンやアバターを複製すると、複製先のセレクターも同じプロファイルアセットを
    /// 参照したままになる。複製先で Blueprint ID を採番させると、書き戻し先が
    /// 共有プロファイルなので複製元のバリアントの ID まで書き換わってしまう。
    /// そこでプロファイルに持ち主のセレクターを記録しておき、食い違いを検知できるようにする。
    /// </summary>
    internal static class AvatarVariantProfileOwnership
    {
        /// <summary>
        /// 持ち主判定の結果。
        /// </summary>
        internal enum ProfileOwnershipState
        {
            // このセレクターがプロファイルの持ち主。
            Owned,

            // 記録された持ち主と一致しない。複製などで共有されている疑いがある。
            Foreign,
        }

        // 描画から呼ぶときのキャッシュ。GetGlobalObjectIdSlow は名前のとおり重く、
        // Inspector の再描画は頻繁に走るので、毎回引き直すと操作が重くなる。
        private const double GuiCacheSeconds = 0.5;
        private static AvatarVariantProfile _cachedProfile;
        private static AvatarVariantSelector _cachedSelector;
        private static ProfileOwnershipState _cachedState;
        private static double _cacheExpiry;

        /// <summary>
        /// 描画用の判定。短い間だけ結果を使い回す。
        ///
        /// 判定材料はユーザーが手を動かさない限り変わらないので、反映の遅れは実害にならない。
        /// 書き込み前の判定には使わないこと。そちらは <see cref="Check"/> を直接呼ぶ。
        /// </summary>
        internal static ProfileOwnershipState CheckForGui(AvatarVariantProfile profile, AvatarVariantSelector selector)
        {
            if (profile == _cachedProfile && selector == _cachedSelector &&
                EditorApplication.timeSinceStartup < _cacheExpiry)
            {
                return _cachedState;
            }

            _cachedState = Check(profile, selector);
            _cachedProfile = profile;
            _cachedSelector = selector;
            _cacheExpiry = EditorApplication.timeSinceStartup + GuiCacheSeconds;

            return _cachedState;
        }

        /// <summary>
        /// <paramref name="selector"/> が <paramref name="profile"/> の持ち主かどうかを判定する。
        ///
        /// 主キーは <see cref="GlobalObjectId"/>。一致しなくても、シーン GUID とヒエラルキーパスが
        /// 両方一致すれば「同じセレクターだが ID だけがずれた」とみなし、記録を追従させた上で
        /// Owned を返す。データは書き換えず記録を更新するだけなので、この追従は自動で行ってよい。
        /// </summary>
        internal static ProfileOwnershipState Check(AvatarVariantProfile profile, AvatarVariantSelector selector)
        {
            if (profile == null || selector == null) return ProfileOwnershipState.Owned;

            // 持ち主の記録が空＝この機能より前に作られた既存プロファイル。
            // 何も聞かずにこのセレクターを持ち主として記録する。
            if (string.IsNullOrEmpty(profile.OwnerGlobalId))
            {
                Claim(profile, selector);
                return ProfileOwnershipState.Owned;
            }

            string globalId = GlobalObjectId.GetGlobalObjectIdSlow(selector).ToString();
            if (profile.OwnerGlobalId == globalId) return ProfileOwnershipState.Owned;

            // シーンが一度も保存されていないとシーン GUID が空になり、GlobalObjectId も
            // 保存されるまで安定しない。ここでは補助キーによる救済ができないだけなので、
            // 事故にはならないと見て Owned として扱う。
            string sceneGuid = AssetDatabase.AssetPathToGUID(selector.gameObject.scene.path);
            if (string.IsNullOrEmpty(sceneGuid)) return ProfileOwnershipState.Owned;

            string objectPath = GetHierarchyPath(selector.transform);
            if (profile.OwnerSceneGuid == sceneGuid && profile.OwnerObjectPath == objectPath)
            {
                Claim(profile, selector);
                return ProfileOwnershipState.Owned;
            }

            return ProfileOwnershipState.Foreign;
        }

        /// <summary>
        /// <paramref name="selector"/> を <paramref name="profile"/> の持ち主として記録する。
        /// </summary>
        internal static void Claim(AvatarVariantProfile profile, AvatarVariantSelector selector)
        {
            if (profile == null || selector == null) return;

            // 一度も保存されていないシーンでは GlobalObjectId が確定していない。ここで記録すると、
            // シーンを保存した時点で値が変わって、同じセレクターなのに Foreign と判定されてしまう。
            // 記録せずに見送れば、保存後の Check で改めて持ち主として記録される。
            string sceneGuid = AssetDatabase.AssetPathToGUID(selector.gameObject.scene.path);
            if (string.IsNullOrEmpty(sceneGuid)) return;

            profile.OwnerGlobalId = GlobalObjectId.GetGlobalObjectIdSlow(selector).ToString();
            profile.OwnerSceneGuid = sceneGuid;
            profile.OwnerObjectPath = GetHierarchyPath(selector.transform);

            // 判定をやり直させる。押した直後の再描画で古い結果が残ると、通知が消えない。
            _cacheExpiry = 0.0;

            EditorUtility.SetDirty(profile);
            AvatarVariantProfileSaver.Save(profile);
        }

        /// <summary>
        /// シーンルートからのヒエラルキーパス。<see cref="AvatarVariantProfile.GetPath"/> は
        /// アバタールート基準の相対パスなので、シーン内の位置そのものを表すこちらは別に用意する。
        /// </summary>
        private static string GetHierarchyPath(Transform target)
        {
            if (target == null) return "";

            List<string> parts = new List<string>();
            Transform t = target;
            while (t != null)
            {
                parts.Add(t.name);
                t = t.parent;
            }

            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
