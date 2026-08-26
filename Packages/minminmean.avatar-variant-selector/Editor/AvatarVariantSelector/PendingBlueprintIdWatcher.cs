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
            if (selector == null || selector.Set == null) return;

            AvatarVariantSet set = selector.Set;
            AvatarVariantDefinition pending = set.PendingVariant;
            if (pending == null) return;

            VRC.Core.PipelineManager pm = AvatarRootFinder.FindPipelineManager(selector.transform);
            if (pm == null || string.IsNullOrEmpty(pm.blueprintId)) return;

            // 他のバリアントが既に使っている ID なら、採番されたものではないので触らない。
            if (set.Variants.Any(v => v != null && v != pending && v.BlueprintId == pm.blueprintId)) return;

            Undo.RecordObject(set, "Write back blueprint ID");
            pending.BlueprintId = pm.blueprintId;
            set.PendingVariantKey = "";

            // このアセットだけを保存する。AssetDatabase.SaveAssets() は
            // 他の未保存アセットまで巻き添えで書き込んでしまうので使わない。
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssetIfDirty(set);

            Debug.Log(string.Format(AvatarVariantLocalize.Dictionary.log_wrote_back, pending.Name, pm.blueprintId), set);
        }
    }
}
