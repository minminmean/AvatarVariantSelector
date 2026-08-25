using System;
using System.Collections.Generic;
using UnityEngine;

namespace MinMinMart.AvatarVariant
{
    /// <summary>
    /// ビルド時に上書きするマテリアルスロット 1 件分。
    /// </summary>
    [Serializable]
    public class VariantMaterialOverride
    {
        // アバタールートからの相対パス。
        public string RendererPath = "";

        public int Slot;
        public Material Material;
    }

    /// <summary>
    /// ビルド時に設定するブレンドシェイプ 1 件分。
    /// </summary>
    [Serializable]
    public class VariantBlendShapeChange
    {
        // アバタールートからの相対パス。
        public string RendererPath = "";

        public string ShapeName = "";
        [Range(0f, 100f)] public float Value = 100f;
    }

    /// <summary>
    /// アップロード先 1 つ分の定義。
    ///
    /// どのバリアントをビルドするかは、アバタールートの PipelineManager が持つ
    /// Blueprint ID と <see cref="BlueprintId"/> の一致で決まる。名前は表示用なので
    /// 自由に決めてよく、いくつ増やしても構わない。
    /// </summary>
    [Serializable]
    public class AvatarVariantDefinition
    {
        // 表示用の名前。自由に付けてよい。ビルド対象の判定には使わない。
        public string Name = "";

        // 並び替えや改名に影響されない内部用の識別子。新規アップロード待ちの指定に使う。
        [HideInInspector] public string Key = "";

        // アバターの Blueprint ID がこの値と一致したとき、このバリアントがビルドされる。
        public string BlueprintId = "";

        // このバリアントのビルドから削除するオブジェクト。アバタールートからの相対パス。
        public List<string> RemoveObjectPaths = new List<string>();

        // このバリアントのビルドで差し替えるマテリアルスロット。
        public List<VariantMaterialOverride> MaterialOverrides = new List<VariantMaterialOverride>();

        // このバリアントのビルドで設定するブレンドシェイプ。
        public List<VariantBlendShapeChange> BlendShapeChanges = new List<VariantBlendShapeChange>();
    }

    /// <summary>
    /// 1 つのシーンから複数のアップロード先ぶんのアバターをビルドするための設定。
    ///
    /// シーンではなくアセットに置くのは、アップロードで採番された Blueprint ID を
    /// スクリプトから保存できるようにするため。シーンを保存し忘れても設定を失わない。
    /// その代わりアセットからシーン内オブジェクトは参照できないので、対象は
    /// アバタールートからの相対パスで持つ。
    /// </summary>
    [CreateAssetMenu(fileName = "AvatarVariantSet", menuName = "MinMinMart/Avatar Variant Set")]
    public class AvatarVariantSet : ScriptableObject
    {
        // アップロード先の一覧。いくつでも追加できる。
        public List<AvatarVariantDefinition> Variants = new List<AvatarVariantDefinition>();

        // 新規アバターとしてアップロードする予定のバリアント。
        // Blueprint ID が採番されたら自動で書き写して空に戻る。
        [HideInInspector] public string PendingVariantKey = "";

        // Blueprint ID がどのバリアントにも一致しないとき、ビルドを止めずに続行する。
        // 通常は無効のままにする。ここで失敗させることが、取り違えを防ぐ仕組みそのものなので。
        public bool AllowUnmatchedBlueprintId = false;

        /// <summary>
        /// <paramref name="blueprintId"/> に一致するバリアントを返す。無ければ null。
        /// </summary>
        public AvatarVariantDefinition Resolve(string blueprintId)
        {
            if (string.IsNullOrEmpty(blueprintId)) return null;
            foreach (var v in Variants)
            {
                if (v != null && v.BlueprintId == blueprintId) return v;
            }

            return null;
        }

        /// <summary>
        /// 新規アップロード待ちに指定されているバリアント。無ければ null。
        /// </summary>
        public AvatarVariantDefinition PendingVariant
        {
            get
            {
                if (string.IsNullOrEmpty(PendingVariantKey)) return null;
                foreach (var v in Variants)
                {
                    if (v != null && !string.IsNullOrEmpty(v.Key) && v.Key == PendingVariantKey) return v;
                }

                return null;
            }
        }

        /// <summary>
        /// ビルドするバリアントを決める。
        ///
        /// Blueprint ID が入っていればそれが唯一の判断材料で、一致しなければ null を返す
        /// （既存アバターを上書きする可能性があるため、推測はしない）。
        /// ID が空の場合に限り、新規アップロード待ちの指定を使う。上書き先が存在しないので安全。
        /// </summary>
        public AvatarVariantDefinition ResolveForBuild(string blueprintId, out bool viaPending)
        {
            viaPending = false;

            var byId = Resolve(blueprintId);
            if (byId != null) return byId;
            if (!string.IsNullOrEmpty(blueprintId)) return null;

            var pending = PendingVariant;
            if (pending == null) return null;

            viaPending = true;
            return pending;
        }

        // ---------- パスの相互変換 ----------

        /// <summary>
        /// <paramref name="target"/> のアバタールートからの相対パス。
        /// ルート配下でなければ空文字を返す。
        /// </summary>
        public static string GetPath(Transform root, Transform target)
        {
            if (root == null || target == null || target == root) return "";

            var parts = new List<string>();
            var t = target;
            while (t != null && t != root)
            {
                parts.Add(t.name);
                t = t.parent;
            }

            if (t != root) return "";

            parts.Reverse();
            return string.Join("/", parts);
        }

        /// <summary>
        /// 相対パスから Transform を引く。非アクティブなオブジェクトも見つかる。
        /// </summary>
        public static Transform FindByPath(Transform root, string path)
        {
            if (root == null || string.IsNullOrEmpty(path)) return null;
            return root.Find(path);
        }
    }
}
