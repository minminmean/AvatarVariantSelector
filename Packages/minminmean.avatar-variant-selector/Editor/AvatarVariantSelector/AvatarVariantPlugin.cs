using System.Collections.Generic;
using System.Linq;
using MinMinMart.AvatarVariant;
using MinMinMart.AvatarVariant.Editor;
using nadena.dev.ndmf;
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

        private static AvatarVariantLocalizeDictionary T => AvatarVariantLocalize.T;

        protected override void Configure()
        {
            InPhase(BuildPhase.FirstChance)
                .Run("Apply avatar variant", Apply);
        }

        private static void Apply(BuildContext ctx)
        {
            var root = ctx.AvatarRootObject;
            var selectors = root.GetComponentsInChildren<AvatarVariantSelector>(true);
            if (selectors.Length == 0) return;

            if (selectors.Length > 1)
            {
                throw new System.Exception(string.Format(T.build_multiple_selectors, root.name, selectors.Length));
            }

            var selector = selectors[0];
            var set = selector.Set;

            if (set == null)
            {
                throw new System.Exception(string.Format(T.build_no_set, root.name));
            }

            var blueprintId = GetBlueprintId(root);
            var variant = set.ResolveForBuild(blueprintId, out var viaPending);

            if (variant == null)
            {
                if (!set.AllowUnmatchedBlueprintId)
                {
                    var known = set.Variants
                        .Where(v => v != null)
                        .Select(v => $"  {v.Name}: {(string.IsNullOrEmpty(v.BlueprintId) ? T.blueprint_id_unassigned : v.BlueprintId)}");

                    var hint = string.IsNullOrEmpty(blueprintId) ? T.build_hint_new : T.build_hint_switch;

                    throw new System.Exception(string.Format(T.build_cannot_resolve,
                        string.IsNullOrEmpty(blueprintId) ? T.blueprint_id_unassigned : blueprintId,
                        string.Join("\n", known),
                        hint));
                }

                Debug.LogWarning(T.build_allow_unmatched_warn);
            }
            else
            {
                if (viaPending)
                {
                    Debug.Log(string.Format(T.build_via_pending, variant.Name));
                }

                ApplyVariant(variant, root);
            }

            // ビルド成果物に残さない。
            foreach (var s in selectors)
            {
                Object.DestroyImmediate(s);
            }
        }

        private static void ApplyVariant(AvatarVariantDefinition variant, GameObject root)
        {
            // 削除 → マテリアル → ブレンドシェイプ の順に適用する。
            // パスはビルド用コピーのルートを基準に引くので、実シーンに触れることは無い。
            var removedPaths = new List<string>();

            foreach (var path in variant.RemoveObjectPaths)
            {
                if (string.IsNullOrEmpty(path)) continue;

                var target = AvatarVariantSet.FindByPath(root.transform, path);
                if (target != null)
                {
                    Object.DestroyImmediate(target.gameObject);
                    removedPaths.Add(path);
                    continue;
                }

                // 先に親ごと消していれば見つからないのが正しい。それ以外は設定ミスなので止める。
                if (removedPaths.Any(p => path.StartsWith(p + "/")))
                {
                    continue;
                }

                throw new System.Exception(string.Format(T.build_remove_missing, variant.Name, path));
            }

            foreach (var mo in variant.MaterialOverrides)
            {
                if (mo == null || string.IsNullOrEmpty(mo.RendererPath) || mo.Material == null) continue;

                var renderer = ResolveRenderer(root, mo.RendererPath, variant.Name, removedPaths);
                if (renderer == null) continue;

                var mats = renderer.sharedMaterials;
                if (mo.Slot < 0 || mo.Slot >= mats.Length)
                {
                    throw new System.Exception(string.Format(T.build_slot_out_of_range,
                        variant.Name, mo.RendererPath, mo.Slot, mats.Length));
                }

                mats[mo.Slot] = mo.Material;
                renderer.sharedMaterials = mats;
            }

            foreach (var bs in variant.BlendShapeChanges)
            {
                if (bs == null || string.IsNullOrEmpty(bs.RendererPath) || string.IsNullOrEmpty(bs.ShapeName)) continue;

                var renderer = ResolveRenderer(root, bs.RendererPath, variant.Name, removedPaths) as SkinnedMeshRenderer;
                if (renderer == null) continue;

                var mesh = renderer.sharedMesh;
                var index = mesh != null ? mesh.GetBlendShapeIndex(bs.ShapeName) : -1;
                if (index < 0)
                {
                    throw new System.Exception(string.Format(T.build_shape_missing,
                        variant.Name, bs.RendererPath, bs.ShapeName));
                }

                renderer.SetBlendShapeWeight(index, bs.Value);
            }

            Debug.Log(string.Format(T.build_done, variant.Name,
                removedPaths.Count, variant.MaterialOverrides.Count, variant.BlendShapeChanges.Count));
        }

        /// <summary>
        /// パスから Renderer を引く。既に削除された配下なら null を返して黙って飛ばし、
        /// そうでなければ設定ミスなので例外にする。
        /// </summary>
        private static Renderer ResolveRenderer(GameObject root, string path, string variantName,
            List<string> removedPaths)
        {
            var t = AvatarVariantSet.FindByPath(root.transform, path);
            if (t == null)
            {
                if (removedPaths.Any(p => path == p || path.StartsWith(p + "/"))) return null;

                throw new System.Exception(string.Format(T.build_target_missing, variantName, path));
            }

            var renderer = t.GetComponent<Renderer>();
            if (renderer == null)
            {
                throw new System.Exception(string.Format(T.build_no_renderer, variantName, path));
            }

            return renderer;
        }

        private static string GetBlueprintId(GameObject root)
        {
            var pm = root.GetComponent<VRC.Core.PipelineManager>();
            return pm != null ? pm.blueprintId : null;
        }
    }
}
