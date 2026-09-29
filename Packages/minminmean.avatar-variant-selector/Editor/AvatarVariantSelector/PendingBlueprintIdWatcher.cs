using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3A.Editor;
using Object = UnityEngine.Object;

namespace MinMinMart.AvatarVariant.Editor
{
    /// <summary>
    /// 新規アップロード待ちのバリアントがあるとき、アップロードに成功した時点で、
    /// 採番された Blueprint ID をそのバリアントへ書き写す。
    ///
    /// ID が PipelineManager に書かれた時点では書き写さない。新規アップロードでは SDK がビルドの前に
    /// ID を予約して書き込むが、ビルドやアップロードに失敗すると予約を取り消して ID を空に戻す。
    /// 予約の時点で書き写していると、取り消された ID がバリアントに残り、待ちの印も消えてしまう。
    /// そうなると次の試行で予約された別の ID がどのバリアントとも一致せず、ビルドが止まる。
    ///
    /// 成功の通知は SDK の公開 API（IVRCSdkAvatarBuilderApi.OnSdkUploadSuccess）で受け取る。
    /// ビルダーは SDK のコントロールパネルを開くたびに作り直されるので、開いたときに登録し直す。
    ///
    /// 書き写し先はアセットなので、その場で保存できる。シーンの保存は要らない。
    /// </summary>
    [InitializeOnLoad]
    internal static class PendingBlueprintIdWatcher
    {
        static PendingBlueprintIdWatcher()
        {
            VRCSdkControlPanel.OnSdkPanelEnable += OnSdkPanelEnable;
        }

        private static void OnSdkPanelEnable(object sender, EventArgs e)
        {
            if (!VRCSdkControlPanel.TryGetBuilder<IVRCSdkAvatarBuilderApi>(out IVRCSdkAvatarBuilderApi builder)) return;

            // 同じビルダーに二重に登録しないよう、外してから付け直す。
            builder.OnSdkUploadSuccess -= OnUploadSuccess;
            builder.OnSdkUploadSuccess += OnUploadSuccess;
        }

        /// <summary>
        /// アップロードに成功したアバターの ID を受け取り、その ID を持つアバターの
        /// 新規アップロード待ちのバリアントへ書き写す。
        /// </summary>
        private static void OnUploadSuccess(object sender, string blueprintId)
        {
            if (string.IsNullOrEmpty(blueprintId)) return;

            foreach (AvatarVariantSelector selector in Object.FindObjectsOfType<AvatarVariantSelector>(true))
            {
                TryWriteBack(selector, blueprintId);
            }
        }

        private static void TryWriteBack(AvatarVariantSelector selector, string blueprintId)
        {
            if (selector == null || selector.Profile == null) return;

            // アップロードしたのがこのアバターかどうかは、PipelineManager の ID で見分ける。
            VRC.Core.PipelineManager pm = AvatarRootFinder.FindPipelineManager(selector.transform);
            if (pm == null || pm.blueprintId != blueprintId) return;

            AvatarVariantProfile profile = selector.Profile;

            // 登録済みの ID なら既存アバターへの上書きなので、書き写すものは無い。
            // マルチプラットフォームのアップロードで 2 回目以降の成功が来たときもここで抜ける。
            if (profile.Resolve(blueprintId) != null) return;

            // ビルド時と同じ判定で、実際にビルドされたのが待ちのバリアントだったかを確かめる。
            AvatarVariantDefinition pending = profile.ResolveForBuild(blueprintId, out bool viaPending);
            if (pending == null || !viaPending) return;

            if (AvatarVariantProfileOwnership.Check(profile, selector) == AvatarVariantProfileOwnership.ProfileOwnershipState.Foreign)
            {
                // 持ち主が食い違うプロファイルには書き込めない。かといって通知を出して
                // ユーザーの判断を待つと、その間に ID の書き戻し先が分からなくなりかねない。
                // そのためここだけは確認を挟まず、自動で複製してから複製の方に書き込む。
                // 複製は自分だけが持ち主になるので、他のバリアントの ID を巻き込む心配がない。
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
            pending.BlueprintId = blueprintId;
            profile.PendingVariantKey = "";

            EditorUtility.SetDirty(profile);
            AvatarVariantProfileSaver.Save(profile);

            Debug.Log(string.Format(AvatarVariantLocalize.Dictionary.log_wrote_back, pending.Name, blueprintId), profile);
        }
    }
}
