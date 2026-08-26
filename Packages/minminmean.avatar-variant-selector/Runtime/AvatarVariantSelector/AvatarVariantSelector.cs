using UnityEngine;
using UnityEngine.Serialization;

namespace MinMinMart.AvatarVariant
{
    /// <summary>
    /// 1 つのシーンから、複数のアップロード先ぶんのアバターをビルドする。
    ///
    /// シーンには常に「全部入り」の構成を置いておき、各バリアントは NDMF のビルド中に
    /// 引き算して作る。そのためバリアントを切り替えてもシーンは汚れない。
    /// アバタールートに付けて使う。
    ///
    /// 設定の実体は <see cref="AvatarVariantProfile"/> アセット側にある。ここが持つのは
    /// その参照だけなので、アップロードで採番された Blueprint ID の保存に
    /// シーンの保存を必要としない。
    /// </summary>
    [AddComponentMenu("MinMinMart/Avatar Variant Selector")]
    [DisallowMultipleComponent]
    public class AvatarVariantSelector : MonoBehaviour, VRC.SDKBase.IEditorOnly
    {
        // バリアントプロファイル。設定はここに保存されるので、シーンを保存しなくても失われない。
        [FormerlySerializedAs("Set")] public AvatarVariantProfile Profile;
    }
}
