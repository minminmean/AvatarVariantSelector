using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEngine;
using VRC.SDK3A.Editor;
using VRC.SDKBase.Editor.Api;

namespace MinMinMart.AvatarVariant.Editor
{
    /// <summary>
    /// 全バリアントを、PC と Android の両方へ続けてアップロードする。
    ///
    /// いまのプラットフォームで全バリアントを上げ終えてから、もう一方へ切り替えてもう一周する。
    /// 切り替えを往復させないので、ビルドターゲットの切り替えは 1 回で済む。
    ///
    /// SDK のマルチプラットフォーム機能には乗らない。あちらは続きの状態の数え違いで、
    /// PC と Android を行き来しながら上げ続けることがあるため、プラットフォームは自前で回す。
    ///
    /// 切り替えるとドメインリロードが起きて静的な状態が消えるので、続きは SessionState に持ち越し、
    /// リロード後に <see cref="InitializeOnLoadAttribute"/> から再開する。SessionState は Unity を
    /// 閉じれば消えるので、開き直せば必ず止まる。
    ///
    /// アップロードには、パネルが持っているアバター情報ではなく、ここで API から取り直したものを渡す。
    /// パネルは Blueprint ID の変化を 1 秒おきに見て取り直しているので、切り替え直後に
    /// パネル経由で上げると、空のアバター情報のまま進んで落ちることがあるため。
    ///
    /// 対象は Blueprint ID を持つバリアントだけ。新規アップロードには名前とサムネイルが要り、
    /// 放っておいて進める形にできないので、先に一度ふつうに上げてもらう。
    /// </summary>
    [InitializeOnLoad]
    internal static class AvatarVariantBulkUploader
    {
        private const string KeyPrefix = "MinMinMart.AvatarVariant.BulkUpload.";
        private const string ActiveKey = KeyPrefix + "Active";
        private const string SelectorKey = KeyPrefix + "Selector";
        private const string BlueprintIdsKey = KeyPrefix + "BlueprintIds";
        private const string PlatformsKey = KeyPrefix + "Platforms";
        private const string PlatformIndexKey = KeyPrefix + "PlatformIndex";
        private const string VariantIndexKey = KeyPrefix + "VariantIndex";
        private const string OriginalIdKey = KeyPrefix + "OriginalId";

        // パネルが開いてログインが済んでも、ビルダーの中身が揃うまで少しかかる。
        // その間にビルドを呼ぶと「SDK パネルを開いてください」で落ちるので、揃ってから少し待つ。
        private const double SettleSeconds = 5.0;

        private static AvatarVariantLocalizeDictionary LocalizeDict => AvatarVariantLocalize.Dictionary;

        // このドメインでアップロードの手順を回しているか。二重に回さないための印。
        private static bool _running;

        // 準備が揃い始めた時刻。揃っていなければ負。
        private static double _readySince = -1;

        private static CancellationTokenSource _cancellation;

        static AvatarVariantBulkUploader()
        {
            if (IsActive) EditorApplication.delayCall += Kick;
        }

        // ---------- 状態 ----------

        internal static bool IsActive
        {
            get => SessionState.GetBool(ActiveKey, false);
            private set => SessionState.SetBool(ActiveKey, value);
        }

        private static List<string> BlueprintIds
        {
            get => Split(SessionState.GetString(BlueprintIdsKey, ""));
            set => SessionState.SetString(BlueprintIdsKey, string.Join("|", value));
        }

        private static List<BuildTarget> Platforms
        {
            get => Split(SessionState.GetString(PlatformsKey, "")).Select(s => (BuildTarget)int.Parse(s)).ToList();
            set => SessionState.SetString(PlatformsKey, string.Join("|", value.Select(t => ((int)t).ToString())));
        }

        private static int PlatformIndex
        {
            get => SessionState.GetInt(PlatformIndexKey, 0);
            set => SessionState.SetInt(PlatformIndexKey, value);
        }

        private static int VariantIndex
        {
            get => SessionState.GetInt(VariantIndexKey, 0);
            set => SessionState.SetInt(VariantIndexKey, value);
        }

        private static List<string> Split(string joined)
        {
            return joined.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        private static void ClearState()
        {
            SessionState.EraseBool(ActiveKey);
            SessionState.EraseString(SelectorKey);
            SessionState.EraseString(BlueprintIdsKey);
            SessionState.EraseString(PlatformsKey);
            SessionState.EraseInt(PlatformIndexKey);
            SessionState.EraseInt(VariantIndexKey);
            SessionState.EraseString(OriginalIdKey);
        }

        // ---------- 開始前の確認 ----------

        /// <summary>
        /// 全部アップロードを始められない理由。始められるなら null。
        /// </summary>
        internal static string GetBlockReason(AvatarVariantProfile profile)
        {
            List<AvatarVariantDefinition> variants = profile.Variants.Where(v => v != null).ToList();
            if (variants.Count == 0) return LocalizeDict.switch_no_variants;

            if (variants.Any(v => string.IsNullOrEmpty(v.BlueprintId))) return LocalizeDict.upload_all_needs_ids;

            if (variants.Select(v => v.BlueprintId).Distinct().Count() != variants.Count)
            {
                return LocalizeDict.upload_all_duplicate_ids;
            }

            foreach (BuildTarget target in PlannedPlatforms())
            {
                if (!BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(target), target))
                {
                    return string.Format(LocalizeDict.upload_all_no_build_support, PlatformName(target));
                }
            }

            if (!IsPanelReady()) return LocalizeDict.upload_all_no_panel;

            return null;
        }

        /// <summary>
        /// 回す順のプラットフォーム。いまのプラットフォームを先にして、切り替えを 1 回で済ませる。
        /// </summary>
        internal static List<BuildTarget> PlannedPlatforms()
        {
            BuildTarget current = EditorUserBuildSettings.activeBuildTarget;
            return current == BuildTarget.Android
                ? new List<BuildTarget> { BuildTarget.Android, BuildTarget.StandaloneWindows64 }
                : new List<BuildTarget> { BuildTarget.StandaloneWindows64, BuildTarget.Android };
        }

        internal static string PlatformName(BuildTarget target)
        {
            return target == BuildTarget.Android ? "Android" : "PC";
        }

        private static bool IsPanelReady()
        {
            // TryGetBuilder はパネルが閉じているとエラーを出すので、先に窓の有無を見る。
            if (VRCSdkControlPanel.window == null) return false;
            if (VRC.Core.APIUser.CurrentUser == null) return false;

            return VRCSdkControlPanel.TryGetBuilder<IVRCSdkAvatarBuilderApi>(out IVRCSdkAvatarBuilderApi _);
        }

        // ---------- 開始と中止 ----------

        /// <summary>
        /// 確認のダイアログを出し、了承されたら全部アップロードを始める。
        /// </summary>
        internal static void Start(AvatarVariantSelector selector, AvatarVariantProfile profile, VRC.Core.PipelineManager pm)
        {
            if (IsActive || GetBlockReason(profile) != null) return;

            List<string> ids = profile.Variants.Where(v => v != null).Select(v => v.BlueprintId).ToList();
            List<BuildTarget> platforms = PlannedPlatforms();

            string order = string.Join(" → ", platforms.Select(PlatformName));
            if (!EditorUtility.DisplayDialog(LocalizeDict.upload_all_confirm_title,
                    string.Format(LocalizeDict.upload_all_confirm_message, ids.Count, order, ids.Count * platforms.Count),
                    LocalizeDict.upload_all_confirm_ok, LocalizeDict.delete_dialog_cancel))
            {
                return;
            }

            // リロード後にシーン上のセレクターを見つけ直すための控え。
            string selectorId = GlobalObjectId.GetGlobalObjectIdSlow(selector).ToString();

            SessionState.SetString(SelectorKey, selectorId);
            SessionState.SetString(OriginalIdKey, pm.blueprintId ?? "");
            BlueprintIds = ids;
            Platforms = platforms;
            PlatformIndex = 0;
            VariantIndex = 0;
            IsActive = true;

            Kick();
        }

        /// <summary>
        /// 中止する。アップロード中なら SDK に取り消しを伝え、終わりの後始末はそちらの例外で行う。
        /// </summary>
        internal static void Cancel()
        {
            if (_running && _cancellation != null)
            {
                _cancellation.Cancel();
                return;
            }

            // 切り替え後の準備待ちなど、手順が回っていないときはその場で止める。
            Finish(LocalizeDict.log_upload_all_cancelled, null);
        }

        // ---------- 進行 ----------

        /// <summary>
        /// 準備が揃うのを待ってから、手順を回し始める。
        /// </summary>
        private static void Kick()
        {
            if (!IsActive || _running) return;

            _readySince = -1;
            EditorApplication.update -= WaitForReady;
            EditorApplication.update += WaitForReady;
        }

        private static void WaitForReady()
        {
            if (!IsActive)
            {
                EditorApplication.update -= WaitForReady;
                return;
            }

            bool ready = !EditorApplication.isCompiling
                         && !EditorApplication.isUpdating
                         && !EditorApplication.isPlayingOrWillChangePlaymode
                         && EditorUserBuildSettings.activeBuildTarget == CurrentPlatform
                         && IsPanelReady();

            if (!ready)
            {
                _readySince = -1;
                return;
            }

            if (_readySince < 0)
            {
                _readySince = EditorApplication.timeSinceStartup;
                return;
            }

            if (EditorApplication.timeSinceStartup - _readySince < SettleSeconds) return;

            EditorApplication.update -= WaitForReady;
            RunAsync();
        }

        private static BuildTarget CurrentPlatform
        {
            get
            {
                List<BuildTarget> platforms = Platforms;
                return PlatformIndex < platforms.Count ? platforms[PlatformIndex] : BuildTarget.NoTarget;
            }
        }

        /// <summary>
        /// いまのプラットフォームで、残りのバリアントを順に上げる。
        /// 上げ終えたら次のプラットフォームへ切り替える（続きはリロード後に再開する）。
        /// </summary>
        private static async void RunAsync()
        {
            if (_running) return;
            _running = true;
            _cancellation = new CancellationTokenSource();

            string failedVariant = null;
            try
            {
                List<string> ids = BlueprintIds;
                List<BuildTarget> platforms = Platforms;
                BuildTarget platform = CurrentPlatform;

                while (VariantIndex < ids.Count)
                {
                    if (!TryFindTarget(out AvatarVariantSelector selector, out VRC.Core.PipelineManager pm))
                    {
                        Finish(LocalizeDict.log_upload_all_selector_missing, LocalizeDict.log_upload_all_selector_missing);
                        return;
                    }

                    AvatarVariantProfile profile = selector.Profile;
                    AvatarVariantDefinition variant = profile.Resolve(ids[VariantIndex]);
                    if (variant == null)
                    {
                        string message = string.Format(LocalizeDict.log_upload_all_variant_missing, ids[VariantIndex]);
                        Finish(message, message);
                        return;
                    }

                    string name = string.IsNullOrWhiteSpace(variant.Name) ? LocalizeDict.asset_unnamed : variant.Name;
                    failedVariant = name;
                    Debug.Log(string.Format(LocalizeDict.log_upload_all_step, PlatformName(platform), VariantIndex + 1, ids.Count, name));
                    UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

                    AvatarVariantSwitcher.SwitchTo(profile, pm, variant);

                    VRCSdkControlPanel.TryGetBuilder<IVRCSdkAvatarBuilderApi>(out IVRCSdkAvatarBuilderApi builder);

                    // パネルの表示もこのバリアントに合わせておく。アップロード自体は下で渡す情報で進む。
                    builder.SelectAvatar(pm.gameObject);

                    VRCAvatar avatar = await VRCApi.GetAvatar(variant.BlueprintId, true, _cancellation.Token);
                    await builder.BuildAndUpload(pm.gameObject, avatar, null, _cancellation.Token);

                    VariantIndex++;
                }

                failedVariant = null;

                if (PlatformIndex + 1 >= platforms.Count)
                {
                    int total = ids.Count * platforms.Count;
                    string done = string.Format(LocalizeDict.upload_all_done, total, PlatformName(platform));
                    Finish(done, null, done);
                    return;
                }

                // 次のプラットフォームへ。切り替えの後はドメインリロードが起き、静的コンストラクタから再開する。
                PlatformIndex++;
                VariantIndex = 0;
                BuildTarget next = CurrentPlatform;
                Debug.Log(string.Format(LocalizeDict.log_upload_all_switching, PlatformName(next)));
                EditorUserBuildSettings.SwitchActiveBuildTargetAsync(BuildPipeline.GetBuildTargetGroup(next), next);
            }
            catch (Exception e)
            {
                if (_cancellation.IsCancellationRequested)
                {
                    Finish(LocalizeDict.log_upload_all_cancelled, null);
                }
                else
                {
                    string message = string.Format(LocalizeDict.upload_all_failed,
                        PlatformName(CurrentPlatform), failedVariant ?? LocalizeDict.asset_unnamed, e.Message);
                    Debug.LogException(e);
                    Finish(message, message);
                }
            }
            finally
            {
                _running = false;
                _cancellation = null;

                // リロードを待たずに終わった場合でも、表示を最新にする。
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

                // 切り替えが効かなかったときは、準備待ちに戻ってもう一度試す。
                if (IsActive) Kick();
            }
        }

        private static bool TryFindTarget(out AvatarVariantSelector selector, out VRC.Core.PipelineManager pm)
        {
            selector = null;
            pm = null;

            if (!GlobalObjectId.TryParse(SessionState.GetString(SelectorKey, ""), out GlobalObjectId id)) return false;

            selector = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as AvatarVariantSelector;
            if (selector == null || selector.Profile == null) return false;

            pm = AvatarRootFinder.FindPipelineManager(selector.transform);
            return pm != null;
        }

        /// <summary>
        /// 状態を消して終える。始める前に選んでいたバリアントへ戻す。
        /// </summary>
        /// <param name="log">コンソールに出す一文。</param>
        /// <param name="error">失敗として知らせる文。null なら失敗ではない。</param>
        /// <param name="notice">成功として知らせる文。null なら出さない。</param>
        private static void Finish(string log, string error, string notice = null)
        {
            string originalId = SessionState.GetString(OriginalIdKey, "");

            if (TryFindTarget(out AvatarVariantSelector selector, out VRC.Core.PipelineManager pm))
            {
                AvatarVariantDefinition original = selector.Profile.Resolve(originalId);
                if (original != null) AvatarVariantSwitcher.SwitchTo(selector.Profile, pm, original);
            }

            ClearState();
            EditorApplication.update -= WaitForReady;

            if (error != null)
            {
                Debug.LogError(log);
                EditorUtility.DisplayDialog(LocalizeDict.upload_all_confirm_title, error, "OK");
            }
            else
            {
                Debug.Log(log);
                if (notice != null) EditorUtility.DisplayDialog(LocalizeDict.upload_all_confirm_title, notice, "OK");
            }
        }

        // ---------- 表示用 ----------

        /// <summary>
        /// 進み具合の一文。動いていなければ null。
        /// </summary>
        internal static string GetProgressText()
        {
            if (!IsActive) return null;

            List<string> ids = BlueprintIds;
            BuildTarget platform = CurrentPlatform;

            if (_running) return string.Format(LocalizeDict.upload_all_progress, PlatformName(platform), Mathf.Min(VariantIndex + 1, ids.Count), ids.Count);

            return EditorUserBuildSettings.activeBuildTarget == platform
                ? string.Format(LocalizeDict.upload_all_waiting, PlatformName(platform))
                : string.Format(LocalizeDict.upload_all_switching, PlatformName(platform));
        }
    }
}
