using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MinMinMart.AvatarVariant.Editor
{
    /// <summary>
    /// Inspector の通知欄に出す内容を集める。
    ///
    /// 出すのは 2 種類。ビルド時に例外で止まることを編集中に先出しする警告と、
    /// 今の状態を伝えるだけのお知らせ。表示には関与せず、文字列を返すだけにしてある。
    /// </summary>
    internal static class AvatarVariantNoticeCollector
    {
        private static AvatarVariantLocalizeDictionary LocalizeDict => AvatarVariantLocalize.Dictionary;

        /// <summary>
        /// 警告として出す内容をすべて集める。
        ///
        /// 見つかった問題だけを足す。問題が無かった箇所は何も残さない。
        ///
        /// <paramref name="root"/> が null のときは、基準が無いのでパスの生存確認を飛ばす。
        /// </summary>
        internal static List<string> CollectProblems(AvatarVariantSet set, GameObject root, string blueprintId)
        {
            List<string> problems = new List<string>();

            // アップロード先が決まっていないと、ビルドしても PipelineManager の指定のまま上がる。
            // 止めはしないが、意図しないアップロードになりやすいので先に知らせる。
            if (set.Variants.All(v => v == null))
            {
                problems.Add(LocalizeDict.warn_no_variants);
            }
            else if (set.ResolveForBuild(blueprintId, out bool _) == null)
            {
                problems.Add(LocalizeDict.warn_no_selection);
            }

            List<string> ids = set.Variants.Where(v => v != null).Select(v => v.BlueprintId).ToList();
            Transform rootT = root != null ? root.transform : null;

            foreach (AvatarVariantDefinition v in set.Variants.Where(v => v != null))
            {
                string duplicateId = CheckDuplicateId(v, ids);
                if (duplicateId != null) problems.Add(duplicateId);

                if (rootT == null) continue;

                problems.AddRange(CheckRemovePaths(v, rootT));
                problems.AddRange(CheckMaterialOverrides(v, rootT));
                problems.AddRange(CheckBlendShapes(v, rootT));
            }


            return problems;
        }

        /// <summary>
        /// 今の状態を伝えるだけのお知らせを集める。問題ではないので警告とは分けて返す。
        /// </summary>
        internal static List<string> CollectInfos(AvatarVariantSet set)
        {
            List<string> infos = new List<string>();

            AvatarVariantDefinition pending = set.PendingVariant;
            if (pending != null)
            {
                infos.Add(string.Format(LocalizeDict.pending_banner, pending.Name));
            }

            return infos;
        }

        /// <summary>
        /// 同じ Blueprint ID のバリアントが他にもあれば、その旨。無ければ null。
        /// どちらがビルドされるか決まらなくなるため。
        /// </summary>
        private static string CheckDuplicateId(AvatarVariantDefinition variant, List<string> ids)
        {
            if (string.IsNullOrEmpty(variant.BlueprintId)) return null;

            return ids.Count(id => id == variant.BlueprintId) > 1
                ? string.Format(LocalizeDict.warn_duplicate_id, variant.BlueprintId)
                : null;
        }

        /// <summary>
        /// 削除対象を 1 件ずつ確かめる。見つからないものだけを返す。
        /// </summary>
        private static List<string> CheckRemovePaths(AvatarVariantDefinition variant, Transform root)
        {
            List<string> problems = new List<string>();

            foreach (string path in variant.RemoveObjectPaths.Where(p => !string.IsNullOrEmpty(p)))
            {
                if (AvatarVariantSet.FindByPath(root, path) != null) continue;

                problems.Add(string.Format(LocalizeDict.warn_remove_missing, variant.Name, path));
            }

            return problems;
        }

        /// <summary>
        /// マテリアルの差し替えを 1 件ずつ確かめる。差し替えられないものだけを返す。
        /// </summary>
        private static List<string> CheckMaterialOverrides(AvatarVariantDefinition variant, Transform root)
        {
            List<string> problems = new List<string>();

            foreach (VariantMaterialOverride mo in variant.MaterialOverrides.Where(mo => mo != null && !string.IsNullOrEmpty(mo.RendererPath)))
            {
                Transform t = AvatarVariantSet.FindByPath(root, mo.RendererPath);
                Renderer r = t != null ? t.GetComponent<Renderer>() : null;

                if (r == null)
                {
                    problems.Add(string.Format(LocalizeDict.warn_material_missing, variant.Name, mo.RendererPath));
                }
                else if (mo.Slot < 0 || mo.Slot >= r.sharedMaterials.Length)
                {
                    problems.Add(string.Format(LocalizeDict.warn_material_slot,
                        variant.Name, mo.RendererPath, mo.Slot, r.sharedMaterials.Length));
                }
            }

            return problems;
        }

        /// <summary>
        /// ブレンドシェイプの設定を 1 件ずつ確かめる。設定できないものだけを返す。
        /// </summary>
        private static List<string> CheckBlendShapes(AvatarVariantDefinition variant, Transform root)
        {
            List<string> problems = new List<string>();

            foreach (VariantBlendShapeChange bs in variant.BlendShapeChanges.Where(bs => bs != null && !string.IsNullOrEmpty(bs.RendererPath)))
            {
                Transform t = AvatarVariantSet.FindByPath(root, bs.RendererPath);
                SkinnedMeshRenderer smr = t != null ? t.GetComponent<SkinnedMeshRenderer>() : null;

                if (smr == null || smr.sharedMesh == null)
                {
                    problems.Add(string.Format(LocalizeDict.warn_shape_no_renderer, variant.Name, bs.RendererPath));
                }
                else if (string.IsNullOrEmpty(bs.ShapeName))
                {
                    problems.Add(string.Format(LocalizeDict.warn_shape_unselected, variant.Name, bs.RendererPath));
                }
                else if (smr.sharedMesh.GetBlendShapeIndex(bs.ShapeName) < 0)
                {
                    problems.Add(string.Format(LocalizeDict.warn_shape_missing, variant.Name, bs.RendererPath, bs.ShapeName));
                }
            }

            return problems;
        }




    }
}
