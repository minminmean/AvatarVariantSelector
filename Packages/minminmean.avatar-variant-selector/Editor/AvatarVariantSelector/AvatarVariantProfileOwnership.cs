using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        /// 見るのは <see cref="GlobalObjectId"/> の一致だけ。同じシーンの同じ位置にあることを
        /// 根拠にはしない。アバターを消して作り直したセレクターは、置き場所が同じでも別物として
        /// 登録し直させる。複製したアバターを扱うためのツールである以上、ここを取り違える余地を
        /// 残してはいけない。
        /// </summary>
        internal static ProfileOwnershipState Check(AvatarVariantProfile profile, AvatarVariantSelector selector)
        {
            if (profile == null || selector == null) return ProfileOwnershipState.Owned;

            PurgeMissingOwners(profile);

            // 登録が空＝この機能より前に作られた既存プロファイル、または持ち主が消えて
            // 一覧が空になった状態。何も聞かずにこのセレクターを登録する。
            if (profile.Owners.Count == 0)
            {
                Register(profile, selector);
                return ProfileOwnershipState.Owned;
            }

            // シーンが一度も保存されていないと GlobalObjectId が確定しない。判定材料が無いだけで
            // 事故にはならないので、Owned として扱って警告を出さない。
            string sceneGuid = AssetDatabase.AssetPathToGUID(selector.gameObject.scene.path);
            if (string.IsNullOrEmpty(sceneGuid)) return ProfileOwnershipState.Owned;

            string globalId = GlobalObjectId.GetGlobalObjectIdSlow(selector).ToString();
            if (profile.Owners.Any(o => o.GlobalId == globalId)) return ProfileOwnershipState.Owned;

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
        /// 実体が失われた持ち主の記録を落とす。
        ///
        /// 残したままにすると一覧が際限なく増えるうえ、後から同じ場所に置かれた無関係な
        /// セレクターを登録済みと取り違える余地が残る。
        /// </summary>
        private static void PurgeMissingOwners(AvatarVariantProfile profile)
        {
            // 落とすのは取り消せない操作なので、シーンやアセットの状態が固まっていない間は見送る。
            // 読み込みの途中では、生きているオブジェクトが一時的に引けないことがある。
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

            int removed = profile.Owners.RemoveAll(o => o == null || !IsOwnerAlive(o));
            if (removed > 0) MarkChanged(profile);
        }

        /// <summary>
        /// 登録されたセレクターが今も存在するか。
        ///
        /// 確かめられるのは、そのシーンが開かれている場合だけ。開かれていないシーンの登録は
        /// 生死を判断する材料が無いので、生きているものとして残す。閉じているシーンの登録を
        /// 落とすと、開き直すたびに登録し直しになってしまう。
        /// </summary>
        private static bool IsOwnerAlive(AvatarVariantProfileOwner owner)
        {
            string scenePath = AssetDatabase.GUIDToAssetPath(owner.SceneGuid);
            if (string.IsNullOrEmpty(scenePath)) return false;

            Scene scene = SceneManager.GetSceneByPath(scenePath);
            if (!scene.IsValid() || !scene.isLoaded) return true;

            if (!GlobalObjectId.TryParse(owner.GlobalId, out GlobalObjectId id)) return false;

            return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) != null;
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
        ///
        /// AvatarVariantProfileUserCollector からも同じ計算が要るため internal にして共有する。
        /// </summary>
        internal static string GetHierarchyPath(Transform target)
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
