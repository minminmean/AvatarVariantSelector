using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MinMinMart.AvatarVariant.Editor
{
    /// <summary>
    /// 新規アップロード待ちのバリアントがあるとき、PipelineManager に Blueprint ID が
    /// 採番されたのを検知して、そのバリアントへ書き写す。
    ///
    /// SDK には「採番された ID」を直接渡してくれるコールバックが無い
    /// （IVRCSDKPostprocessAvatarCallback は引数なし）。ID はアップロード時に
    /// シーン上の PipelineManager へ書き込まれるので、そこを監視するのが
    /// SDK のバージョンに依存しない確実な方法になる。
    ///
    /// 書き写し先はアセットなので、その場で保存できる。シーンの保存は要らない。
    /// </summary>
    [InitializeOnLoad]
    internal static class PendingBlueprintIdWatcher
    {
        private const double IntervalSeconds = 1.0;
        private static double _nextCheck;

        static PendingBlueprintIdWatcher()
        {
            EditorApplication.update += Update;
        }

        private static void Update()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (EditorApplication.timeSinceStartup < _nextCheck) return;
            _nextCheck = EditorApplication.timeSinceStartup + IntervalSeconds;

            foreach (AvatarVariantSelector selector in Object.FindObjectsOfType<AvatarVariantSelector>(true))
            {
                TryWriteBack(selector);
            }
        }

        private static void TryWriteBack(AvatarVariantSelector selector)
        {
            if (selector == null || selector.Profile == null) return;

            AvatarVariantProfile profile = selector.Profile;
            AvatarVariantDefinition pending = profile.PendingVariant;
            if (pending == null) return;

            VRC.Core.PipelineManager pm = AvatarRootFinder.FindPipelineManager(selector.transform);
            if (pm == null || string.IsNullOrEmpty(pm.blueprintId)) return;

            // 他のバリアントが既に使っている ID なら、採番されたものではないので触らない。
            if (profile.Variants.Any(v => v != null && v != pending && v.BlueprintId == pm.blueprintId)) return;

            if (AvatarVariantProfileOwnership.Check(profile, selector) == AvatarVariantProfileOwnership.ProfileOwnershipState.Foreign)
            {
                // 持ち主が食い違うプロファイルには書き込めない。かといって通知を出して
                // ユーザーの判断を待つと、次の Update が来るまでに採番された ID の書き戻し機会を
                // 取りこぼしかねない。そのためここだけは確認を挟まず、自動で複製してから
                // 複製の方に書き込む。複製は自分だけが持ち主になるので、他のバリアントの
                // ID を巻き込む心配がない。
                AvatarVariantProfile duplicate = AvatarVariantProfileFactory.DuplicateForSelector(selector);
                AvatarVariantDefinition duplicatedPending = duplicate.PendingVariant;
                if (duplicatedPending == null) return;

                duplicate.AutoDuplicatedNotice = true;
                Debug.LogWarning(string.Format(AvatarVariantLocalize.Dictionary.log_auto_duplicated_profile,
                    AssetDatabase.GetAssetPath(duplicate)), duplicate);

                profile = duplicate;
                pending = duplicatedPending;
            }

            Undo.RecordObject(profile, "Write back blueprint ID");
            pending.BlueprintId = pm.blueprintId;
            profile.PendingVariantKey = "";

            EditorUtility.SetDirty(profile);
            AvatarVariantProfileSaver.Save(profile);

            Debug.Log(string.Format(AvatarVariantLocalize.Dictionary.log_wrote_back, pending.Name, pm.blueprintId), profile);
        }
    }
}
