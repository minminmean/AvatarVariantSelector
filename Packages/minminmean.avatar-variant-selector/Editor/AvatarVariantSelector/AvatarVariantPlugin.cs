using System.Collections.Generic;
using System.Linq;
using MinMinMart.AvatarVariant;
using MinMinMart.AvatarVariant.Editor;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEngine;

[assembly: ExportsPlugin(typeof(AvatarVariantPlugin))]

namespace MinMinMart.AvatarVariant.Editor
{
    /// <summary>
    /// アバターの Blueprint ID に一致するバリアントを、NDMF のビルド中に適用する。
    ///
    /// 「そもそもどのアバターをビルドするか」を決める処理なので、最初のフェーズで
    /// 他のどのプラグインよりも先に走らせる。理由は 2 つ。
    ///
    /// 1. 他のプラグインが先にオブジェクトを消すと、こちらの削除対象が「パス切れ」に
    ///    見えてしまう。実際 VRCQuestTools の Platform GameObject Remover は
    ///    Resolving フェーズで動くため、Android ビルドで衝突していた。
    ///    どちらも「Modular Avatar より前」としか宣言しておらず、順序が未定義だった。
    /// 2. FirstChance では EditorOnly の除去もまだ走っていないので、
    ///    EditorOnly なオブジェクトを削除対象に入れても正しく解決できる。
    ///
    /// 削除されるオブジェクトが持つメニューインストーラー・パラメータ・アーマチュア統合が
    /// 統合処理に拾われる前に消えている必要がある、という当初の条件も満たす。
    /// </summary>
    public class AvatarVariantPlugin : Plugin<AvatarVariantPlugin>
    {
        public override string QualifiedName => "minminmart.avatar-variant";
        public override string DisplayName => "Avatar Variant";

        private static AvatarVariantLocalizeDictionary LocalizeDict => AvatarVariantLocalize.Dictionary;

        protected override void Configure()
        {
            InPhase(BuildPhase.FirstChance)
                .Run("Apply avatar variant", Apply);
        }

        private static void Apply(BuildContext ctx)
        {
            GameObject root = ctx.AvatarRootObject;
            AvatarVariantSelector[] selectors = root.GetComponentsInChildren<AvatarVariantSelector>(true);
            if (selectors.Length == 0) return;

            if (selectors.Length > 1)
            {
                throw new System.Exception(string.Format(LocalizeDict.build_multiple_selectors, root.name, selectors.Length));
            }

            AvatarVariantSelector selector = selectors[0];
            AvatarVariantProfile profile = selector.Profile;

            if (profile == null)
            {
                throw new System.Exception(string.Format(LocalizeDict.build_no_profile, root.name));
            }

            // 編集中はディスクに書かず変更済みの印だけ付けているので、ここで書き出す。
            AvatarVariantProfileSaver.Save(profile);

            string blueprintId = GetBlueprintId(root);
            AvatarVariantDefinition variant = profile.ResolveForBuild(blueprintId, out bool viaPending);

            // Blueprint ID が入っているのに一致するバリアントが無いときは止める。
            // どれを上書きするか決まらないまま既存アバターへ上げてしまうため、推測で進めない。
            if (variant == null && !string.IsNullOrEmpty(blueprintId))
            {
                IEnumerable<string> known = profile.Variants
                    .Where(v => v != null)
                    .Select(v => $"  {v.Name}: {(string.IsNullOrEmpty(v.BlueprintId) ? LocalizeDict.blueprint_id_unassigned : v.BlueprintId)}");

                throw new System.Exception(string.Format(LocalizeDict.build_cannot_resolve,
                    blueprintId,
                    string.Join("\n", known),
                    LocalizeDict.build_hint_switch));
            }

            if (variant == null)
            {
                // どのバリアントも選ばれていない。Blueprint ID も空で上書き先が無いので、
                // 何も適用せず PipelineManager に入っている指定のままアップロードさせる。
                Debug.Log(LocalizeDict.build_no_selection);
            }
            else
            {
                if (viaPending)
                {
                    Debug.Log(string.Format(LocalizeDict.build_via_pending, variant.Name));
                }

                ApplyVariant(variant, root);
            }

            // ビルド成果物に残さない。
            foreach (AvatarVariantSelector s in selectors)
            {
                Object.DestroyImmediate(s);
            }
        }

        private static void ApplyVariant(AvatarVariantDefinition variant, GameObject root)
        {
            // 削除 → マテリアル → ブレンドシェイプ の順に適用する。
            // パスはビルド用コピーのルートを基準に引くので、実シーンに触れることは無い。
            List<string> removedPaths = new List<string>();

            foreach (string path in variant.RemoveObjectPaths)
            {
                if (string.IsNullOrEmpty(path)) continue;

                Transform target = AvatarVariantProfile.FindByPath(root.transform, path);
                if (target != null)
                {
                    Object.DestroyImmediate(target.gameObject);
                    removedPaths.Add(path);
                    continue;
                }

                // 先に親ごと消していれば見つからないのが正しい。それ以外は設定ミスなので止める。
                if (IsAlreadyRemoved(path, removedPaths)) continue;

                throw new System.Exception(string.Format(LocalizeDict.build_remove_missing, variant.Name, path));
            }

            foreach (VariantMaterialOverride mo in variant.MaterialOverrides)
            {
                if (mo == null || string.IsNullOrEmpty(mo.RendererPath) || mo.Material == null) continue;

                Renderer renderer = ResolveRenderer(root, mo.RendererPath, variant.Name, removedPaths);
                if (renderer == null) continue;

                Material[] mats = renderer.sharedMaterials;
                if (mo.Slot < 0 || mo.Slot >= mats.Length)
                {
                    throw new System.Exception(string.Format(LocalizeDict.build_slot_out_of_range,
                        variant.Name, mo.RendererPath, mo.Slot, mats.Length));
                }

                mats[mo.Slot] = mo.Material;
                renderer.sharedMaterials = mats;
            }

            foreach (VariantBlendShapeChange bs in variant.BlendShapeChanges)
            {
                if (bs == null || string.IsNullOrEmpty(bs.RendererPath) || string.IsNullOrEmpty(bs.ShapeName)) continue;

                Renderer resolved = ResolveRenderer(root, bs.RendererPath, variant.Name, removedPaths);
                if (resolved == null) continue;

                // 削除済みで飛ばすのと、型が違う設定ミスとを分ける。
                // まとめて null 扱いにすると、設定ミスが黙って通って別の内容が上がる。
                SkinnedMeshRenderer renderer = resolved as SkinnedMeshRenderer;
                if (renderer == null)
                {
                    throw new System.Exception(string.Format(LocalizeDict.build_no_skinned_renderer,
                        variant.Name, bs.RendererPath));
                }

                Mesh mesh = renderer.sharedMesh;
                int index = mesh != null ? mesh.GetBlendShapeIndex(bs.ShapeName) : -1;
                if (index < 0)
                {
                    throw new System.Exception(string.Format(LocalizeDict.build_shape_missing,
                        variant.Name, bs.RendererPath, bs.ShapeName));
                }

                renderer.SetBlendShapeWeight(index, bs.Value);
            }

            Debug.Log(string.Format(LocalizeDict.build_done, variant.Name,
                removedPaths.Count, variant.MaterialOverrides.Count, variant.BlendShapeChanges.Count));
        }

        /// <summary>
        /// 既に消したオブジェクト自身か、その配下か。
        ///
        /// 自分自身も含める。同じパスが 2 回並んでいると、2 回目は消えた後になって
        /// 見つからないが、これは設定ミスではないので止めてはいけない。
        /// パスの比較なので、カルチャに左右されない Ordinal で行う。
        /// </summary>
        private static bool IsAlreadyRemoved(string path, List<string> removedPaths)
        {
            return removedPaths.Any(p => path == p
                                         || path.StartsWith(p + "/", System.StringComparison.Ordinal));
        }

        /// <summary>
        /// パスから Renderer を引く。既に削除された配下なら null を返して黙って飛ばし、
        /// そうでなければ設定ミスなので例外にする。
        /// </summary>
        private static Renderer ResolveRenderer(GameObject root, string path, string variantName,
            List<string> removedPaths)
        {
            Transform t = AvatarVariantProfile.FindByPath(root.transform, path);
            if (t == null)
            {
                if (IsAlreadyRemoved(path, removedPaths)) return null;

                throw new System.Exception(string.Format(LocalizeDict.build_target_missing, variantName, path));
            }

            Renderer renderer = t.GetComponent<Renderer>();
            if (renderer == null)
            {
                throw new System.Exception(string.Format(LocalizeDict.build_no_renderer, variantName, path));
            }

            return renderer;
        }

        private static string GetBlueprintId(GameObject root)
        {
            VRC.Core.PipelineManager pm = root.GetComponent<VRC.Core.PipelineManager>();
            return pm != null ? pm.blueprintId : null;
        }
    }
}
