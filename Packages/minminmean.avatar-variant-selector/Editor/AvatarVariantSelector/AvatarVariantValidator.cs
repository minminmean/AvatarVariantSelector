using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MinMinMart.AvatarVariant.Editor
{
    /// <summary>
    /// 設定がビルド前に破綻していないかを調べる。
    ///
    /// ビルド時に例外で止まることを編集中に先出しするのが役割。表示には関与せず、
    /// 見つけた問題を文字列で返すだけにしてある。問題が無ければ null を返し、
    /// 呼び出し側が描くコントロールの数を揃えられるようにする。
    /// </summary>
    internal static class AvatarVariantValidator
    {
        private static AvatarVariantLocalizeDictionary LocalizeDict => AvatarVariantLocalize.Dictionary;

        /// <summary>
        /// 警告として出す内容をすべて集める。
        ///
        /// 調べた箇所ごとに1つずつ足し、問題が無ければ null を入れる。
        ///
        /// <paramref name="root"/> が null のときは、基準が無いのでパスの生存確認を飛ばす。
        /// </summary>
        internal static List<string> CollectProblems(AvatarVariantSet set, GameObject root)
        {
            List<string> problems = new List<string>();

            List<string> ids = set.Variants.Where(v => v != null).Select(v => v.BlueprintId).ToList();
            Transform rootT = root != null ? root.transform : null;

            foreach (AvatarVariantDefinition v in set.Variants.Where(v => v != null))
            {
                problems.Add(CheckDuplicateId(v, ids));

                if (rootT == null) continue;

                problems.AddRange(CheckRemovePaths(v, rootT));
                problems.AddRange(CheckMaterialOverrides(v, rootT));
                problems.AddRange(CheckBlendShapes(v, rootT));
            }


            return problems;
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
        /// 削除対象を 1 件ずつ確かめる。見つかるものは null。
        /// </summary>
        private static List<string> CheckRemovePaths(AvatarVariantDefinition variant, Transform root)
        {
            List<string> problems = new List<string>();

            foreach (string path in variant.RemoveObjectPaths.Where(p => !string.IsNullOrEmpty(p)))
            {
                bool missing = AvatarVariantSet.FindByPath(root, path) == null;
                problems.Add(missing ? string.Format(LocalizeDict.warn_remove_missing, variant.Name, path) : null);
            }

            return problems;
        }

        /// <summary>
        /// マテリアルの差し替えを 1 件ずつ確かめる。差し替えられるものは null。
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
                else
                {
                    problems.Add(null);
                }
            }

            return problems;
        }

        /// <summary>
        /// ブレンドシェイプの設定を 1 件ずつ確かめる。設定できるものは null。
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
                else
                {
                    problems.Add(null);
                }
            }

            return problems;
        }




    }
}
