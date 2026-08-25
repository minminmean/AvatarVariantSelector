using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MinMinMart.AvatarVariant.Editor
{
    /// <summary>
    /// 設定がビルド前に破綻していないかを調べる。
    ///
    /// ビルド時に例外で止まることを編集中に先出しするのが役割。表示には関与せず、
    /// 見つけた問題を文字列で返すだけにしてある。
    /// </summary>
    internal static class AvatarVariantValidator
    {
        private static AvatarVariantLocalizeDictionary T => AvatarVariantLocalize.T;

        /// <summary>
        /// <paramref name="set"/> の問題を集める。問題が無ければ空のリスト。
        /// <paramref name="root"/> が null のときは、基準が無いのでパスの生存確認を飛ばす。
        /// </summary>
        internal static List<string> CollectProblems(AvatarVariantSet set, GameObject root)
        {
            List<string> problems = new List<string>();
            List<string> ids = set.Variants.Where(v => v != null).Select(v => v.BlueprintId).ToList();

            foreach (AvatarVariantDefinition v in set.Variants.Where(v => v != null))
            {
                if (!string.IsNullOrEmpty(v.BlueprintId) && ids.Count(id => id == v.BlueprintId) > 1)
                {
                    problems.Add(string.Format(T.warn_duplicate_id, v.BlueprintId));
                }

                if (root == null) continue;
                Transform rootT = root.transform;

                foreach (string path in v.RemoveObjectPaths.Where(p => !string.IsNullOrEmpty(p)))
                {
                    if (AvatarVariantSet.FindByPath(rootT, path) == null)
                    {
                        problems.Add(string.Format(T.warn_remove_missing, v.Name, path));
                    }
                }

                foreach (VariantMaterialOverride mo in v.MaterialOverrides.Where(mo => mo != null && !string.IsNullOrEmpty(mo.RendererPath)))
                {
                    Transform t = AvatarVariantSet.FindByPath(rootT, mo.RendererPath);
                    Renderer r = t != null ? t.GetComponent<Renderer>() : null;
                    if (r == null)
                    {
                        problems.Add(string.Format(T.warn_material_missing, v.Name, mo.RendererPath));
                    }
                    else if (mo.Slot < 0 || mo.Slot >= r.sharedMaterials.Length)
                    {
                        problems.Add(string.Format(T.warn_material_slot,
                            v.Name, mo.RendererPath, mo.Slot, r.sharedMaterials.Length));
                    }
                }

                foreach (VariantBlendShapeChange bs in v.BlendShapeChanges.Where(bs => bs != null && !string.IsNullOrEmpty(bs.RendererPath)))
                {
                    Transform t = AvatarVariantSet.FindByPath(rootT, bs.RendererPath);
                    SkinnedMeshRenderer smr = t != null ? t.GetComponent<SkinnedMeshRenderer>() : null;
                    if (smr == null || smr.sharedMesh == null)
                    {
                        problems.Add(string.Format(T.warn_shape_no_renderer, v.Name, bs.RendererPath));
                    }
                    else if (string.IsNullOrEmpty(bs.ShapeName))
                    {
                        problems.Add(string.Format(T.warn_shape_unselected, v.Name, bs.RendererPath));
                    }
                    else if (smr.sharedMesh.GetBlendShapeIndex(bs.ShapeName) < 0)
                    {
                        problems.Add(string.Format(T.warn_shape_missing, v.Name, bs.RendererPath, bs.ShapeName));
                    }
                }
            }

            if (set.AllowUnmatchedBlueprintId)
            {
                problems.Add(T.warn_allow_unmatched);
            }

            return problems;
        }
    }
}
