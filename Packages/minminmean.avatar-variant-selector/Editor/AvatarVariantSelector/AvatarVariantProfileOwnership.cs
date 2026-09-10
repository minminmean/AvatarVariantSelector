using System.Collections.Generic;
using System.Linq;
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
    ///
    /// 持ち主は 1 つに固定しない。「このプロファイルを使ってよいと登録されたセレクターの一覧」として
    /// 複数持てるようにし、同じアバター構成を意図して共有している状態を一級に扱う。
    /// 一覧に無いセレクターからの参照だけを、複製したまま気付いていない疑いのある状態として検知する。
    /// </summary>
    internal static class AvatarVariantProfileOwnership
    {
        /// <summary>
        /// 持ち主判定の結果。
        /// </summary>
        internal enum ProfileOwnershipState
        {
            // このセレクターが登録済みの持ち主の 1 つ。
            Owned,

            // 登録された持ち主のどれとも一致しない。複製などで共有されている疑いがある。
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
        /// <paramref name="selector"/> が <paramref name="profile"/> の登録済みの持ち主かどうかを判定する。
        ///
        /// 主キーは <see cref="GlobalObjectId"/>。一致しなくても、シーン GUID とヒエラルキーパスが
        /// 両方一致する登録があれば「同じセレクターだが ID だけがずれた」とみなし、その登録を
        /// 追従させた上で Owned を返す。データは書き換えず記録を更新するだけなので、この追従は
        /// 自動で行ってよい。
        /// </summary>
        internal static ProfileOwnershipState Check(AvatarVariantProfile profile, AvatarVariantSelector selector)
        {
            if (profile == null || selector == null) return ProfileOwnershipState.Owned;

            PurgeDeletedScenes(profile);

            // 登録が空＝この機能より前に作られた既存プロファイル、またはシーンごと持ち主が
            // 消えて一覧が空になった状態。何も聞かずにこのセレクターを登録する。
            if (profile.Owners.Count == 0)
            {
                Register(profile, selector);
                return ProfileOwnershipState.Owned;
            }

            string globalId = GlobalObjectId.GetGlobalObjectIdSlow(selector).ToString();
            if (profile.Owners.Any(o => o.GlobalId == globalId)) return ProfileOwnershipState.Owned;

            // シーンが一度も保存されていないとシーン GUID が空になり、GlobalObjectId も
            // 保存されるまで安定しない。ここでは補助キーによる救済ができないだけなので、
            // 事故にはならないと見て Owned として扱う。
            string sceneGuid = AssetDatabase.AssetPathToGUID(selector.gameObject.scene.path);
            if (string.IsNullOrEmpty(sceneGuid)) return ProfileOwnershipState.Owned;

            string objectPath = GetHierarchyPath(selector.transform);
            AvatarVariantProfileOwner matched =
                profile.Owners.FirstOrDefault(o => o.SceneGuid == sceneGuid && o.ObjectPath == objectPath);

            if (matched != null)
            {
                matched.GlobalId = globalId;
                MarkChanged(profile);
                return ProfileOwnershipState.Owned;
            }

            return ProfileOwnershipState.Foreign;
        }

        /// <summary>
        /// <paramref name="selector"/> を <paramref name="profile"/> の持ち主の一覧に加える。
        /// 既に登録済みなら何もしない。共有プロファイルとして扱ってよい、という宣言になる。
        /// </summary>
        internal static void Register(AvatarVariantProfile profile, AvatarVariantSelector selector)
        {
            if (profile == null || selector == null) return;
            if (!AddOwnerEntry(profile, selector)) return;

            MarkChanged(profile);
        }

        /// <summary>
        /// <paramref name="profile"/> の持ち主を <paramref name="selector"/> だけにする。
        ///
        /// プロファイルを複製・新規作成した直後に使う。複製元の登録一覧をそのまま引き継ぐと、
        /// 複製したそばから共有状態になってしまうため、まず空にしてから登録し直す。
        /// </summary>
        internal static void SetSoleOwner(AvatarVariantProfile profile, AvatarVariantSelector selector)
        {
            if (profile == null || selector == null) return;

            profile.Owners.Clear();
            AddOwnerEntry(profile, selector);

            MarkChanged(profile);
        }

        /// <summary>
        /// <paramref name="selector"/> を持ち主として一覧に加える。
        /// 未保存のシーンや既に登録済みの場合は何もせず false を返す。
        /// </summary>
        private static bool AddOwnerEntry(AvatarVariantProfile profile, AvatarVariantSelector selector)
        {
            // 一度も保存されていないシーンでは GlobalObjectId が確定していない。ここで記録すると、
            // シーンを保存した時点で値が変わって、同じセレクターなのに Foreign と判定されてしまう。
            // 記録せずに見送れば、保存後の Check で改めて持ち主として登録される。
            string sceneGuid = AssetDatabase.AssetPathToGUID(selector.gameObject.scene.path);
            if (string.IsNullOrEmpty(sceneGuid)) return false;

            string globalId = GlobalObjectId.GetGlobalObjectIdSlow(selector).ToString();
            if (profile.Owners.Any(o => o.GlobalId == globalId)) return false;

            profile.Owners.Add(new AvatarVariantProfileOwner
            {
                GlobalId = globalId,
                SceneGuid = sceneGuid,
                ObjectPath = GetHierarchyPath(selector.transform),
            });

            return true;
        }

        /// <summary>
        /// シーンごと削除された持ち主の記録を落とす。
        ///
        /// シーンを消しても登録だけが残り続けると一覧が肥大化するうえ、別のシーンが偶然同じ
        /// ヒエラルキーパスを持ったときに誤って Owned と判定されかねない。
        /// </summary>
        private static void PurgeDeletedScenes(AvatarVariantProfile profile)
        {
            int removed = profile.Owners.RemoveAll(o => string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(o.SceneGuid)));
            if (removed > 0) MarkChanged(profile);
        }

        /// <summary>
        /// 登録内容を書き換えた後の後始末。判定をやり直させ、ディスクにも書き出す。
        ///
        /// 押した直後の再描画で古いキャッシュが残ると、通知が消えない。
        /// </summary>
        private static void MarkChanged(AvatarVariantProfile profile)
        {
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
